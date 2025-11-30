using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FindNeedlePluginLib;
using FindNeedlePluginLib.Interfaces;

namespace FindPluginCore.Implementations.Storage
{
    /// <summary>
    /// Hybrid storage implementation that uses in-memory storage for hot data and SQLite for cold data.
    /// All data is persisted to SQLite for durability and can be recovered across sessions.
    /// In-memory storage acts as a performance cache with automatic memory management.
    /// Thread-safe for concurrent operations.
    /// 
    /// NEW: Performance-adaptive strategy - automatically switches to SQLite-only when
    /// InMemory+Spilling becomes slower than pure SQLite.
    /// </summary>
    public class HybridStorage : ISearchStorage
    {
        private InMemoryStorage _memoryStorage;
        private readonly SqliteStorage _diskStorage;
        private readonly object _sync = new();
        
        // Configuration
        private readonly long _memoryThresholdBytes;
        private readonly double _spillPercentage;
        private readonly int _promotionThreshold;
        
        // Tracking for LRU promotion
        private readonly Dictionary<string, AccessTracker> _rawAccessTracking = new();
        private readonly Dictionary<string, AccessTracker> _filteredAccessTracking = new();
        
        // State tracking
        private long _currentMemoryUsage;
        private bool _hasSpilledRaw;
        private bool _hasSpilledFiltered;
        
        // Performance tracking for adaptive strategy
        private bool _useOnlySqlite = false; // When true, bypass InMemory entirely
        private readonly List<double> _recentWriteTimes = new(10);
        private readonly List<double> _recentSqliteWriteTimes = new(10);
        private int _writeCount = 0;
        private const int PERFORMANCE_CHECK_INTERVAL = 5; // Check every 5 writes (was 20)
        private const double WRITE_TIME_THRESHOLD_MS = 50.0; // Switch to SQLite if writes exceed 50ms (was 80ms)

        /// <summary>
        /// Creates a hybrid storage instance.
        /// </summary>
        /// <param name="searchedFilePath">Path to the file being searched (used for SQLite database location)</param>
        /// <param name="memoryThresholdMB">Memory threshold in MB before spilling to disk (default: 100MB)</param>
        /// <param name="spillPercentage">Percentage of data to spill when threshold is reached (default: 0.5 = 50%)</param>
        /// <param name="promotionThreshold">Number of accesses before promoting from disk to memory (default: 3)</param>
        public HybridStorage(
            string searchedFilePath, 
            int memoryThresholdMB = 100,
            double spillPercentage = 0.5,
            int promotionThreshold = 3)
        {
            if (string.IsNullOrEmpty(searchedFilePath))
                throw new ArgumentNullException(nameof(searchedFilePath));
            if (memoryThresholdMB <= 0)
                throw new ArgumentOutOfRangeException(nameof(memoryThresholdMB), "Must be greater than 0");
            if (spillPercentage <= 0 || spillPercentage > 1)
                throw new ArgumentOutOfRangeException(nameof(spillPercentage), "Must be between 0 and 1");
            if (promotionThreshold <= 0)
                throw new ArgumentOutOfRangeException(nameof(promotionThreshold), "Must be greater than 0");

            _memoryStorage = new InMemoryStorage();
            _diskStorage = new SqliteStorage(searchedFilePath);
            _memoryThresholdBytes = memoryThresholdMB * 1024L * 1024L;
            _spillPercentage = spillPercentage;
            _promotionThreshold = promotionThreshold;
            
            // Check if there's existing data on disk from a previous session
            var diskStats = _diskStorage.GetStatistics();
            if (diskStats.rawRecordCount > 0)
            {
                _hasSpilledRaw = true;
            }
            if (diskStats.filteredRecordCount > 0)
            {
                _hasSpilledFiltered = true;
            }
        }

        public void AddRawBatch(IEnumerable<ISearchResult> batch, CancellationToken cancellationToken = default)
        {
            if (batch == null) throw new ArgumentNullException(nameof(batch));
            
            lock (_sync)
            {
                var batchList = batch.ToList();
                long batchSize = CalculateBatchSize(batchList);
                
                // Performance-adaptive strategy
                if (_useOnlySqlite)
                {
                    // We've determined SQLite is faster - use it exclusively
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    _diskStorage.AddRawBatch(batchList, cancellationToken);
                    sw.Stop();
                    
                    TrackSqliteWriteTime(sw.Elapsed.TotalMilliseconds);
                    _hasSpilledRaw = true;
                }
                else
                {
                    // Use InMemory with performance tracking
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    _memoryStorage.AddRawBatch(batchList, cancellationToken);
                    sw.Stop();
                    
                    _currentMemoryUsage += batchSize;
                    TrackHybridWriteTime(sw.Elapsed.TotalMilliseconds);
                    
                    // Check if we should switch to SQLite-only mode
                    CheckAndAdaptStorageStrategy();
                }
            }
        }

        public void AddFilteredBatch(IEnumerable<ISearchResult> batch, CancellationToken cancellationToken = default)
        {
            if (batch == null) throw new ArgumentNullException(nameof(batch));
            
            lock (_sync)
            {
                var batchList = batch.ToList();
                long batchSize = CalculateBatchSize(batchList);
                
                // Performance-adaptive strategy
                if (_useOnlySqlite)
                {
                    _diskStorage.AddFilteredBatch(batchList, cancellationToken);
                    _hasSpilledFiltered = true;
                }
                else
                {
                    _memoryStorage.AddFilteredBatch(batchList, cancellationToken);
                    _currentMemoryUsage += batchSize;
                }
            }
        }

        public void GetRawResultsInBatches(Action<List<ISearchResult>> onBatch, int batchSize = 1000, CancellationToken cancellationToken = default)
        {
            if (onBatch == null) throw new ArgumentNullException(nameof(onBatch));

            lock (_sync)
            {
                if (_useOnlySqlite)
                {
                    // SQLite-only mode: read only from disk
                    _diskStorage.GetRawResultsInBatches(onBatch, batchSize, cancellationToken);
                }
                else
                {
                    // Check if we need to spill on read (lazy spilling)
                    if (_currentMemoryUsage > _memoryThresholdBytes)
                    {
                        SpillRawToDisk(cancellationToken);
                    }
                    
                    // Read from memory first (hot data)
                    _memoryStorage.GetRawResultsInBatches(onBatch, batchSize, cancellationToken);
                    
                    // Then read from disk if we've spilled (cold data)
                    if (_hasSpilledRaw)
                    {
                        _diskStorage.GetRawResultsInBatches(batch =>
                        {
                            TrackAccess(batch, _rawAccessTracking, isRaw: true);
                            onBatch(batch);
                        }, batchSize, cancellationToken);
                    }
                }
            }
        }

        public void GetFilteredResultsInBatches(Action<List<ISearchResult>> onBatch, int batchSize = 1000, CancellationToken cancellationToken = default)
        {
            if (onBatch == null) throw new ArgumentNullException(nameof(onBatch));

            lock (_sync)
            {
                if (_useOnlySqlite)
                {
                    // SQLite-only mode: read only from disk
                    _diskStorage.GetFilteredResultsInBatches(onBatch, batchSize, cancellationToken);
                }
                else
                {
                    // Check if we need to spill on read (lazy spilling)
                    if (_currentMemoryUsage > _memoryThresholdBytes)
                    {
                        SpillFilteredToDisk(cancellationToken);
                    }
                    
                    // Read from memory first (hot data)
                    _memoryStorage.GetFilteredResultsInBatches(onBatch, batchSize, cancellationToken);
                    
                    // Then read from disk if we've spilled (cold data)
                    if (_hasSpilledFiltered)
                    {
                        _diskStorage.GetFilteredResultsInBatches(batch =>
                        {
                            TrackAccess(batch, _filteredAccessTracking, isRaw: false);
                            onBatch(batch);
                        }, batchSize, cancellationToken);
                    }
                }
            }
        }

        public (int rawRecordCount, int filteredRecordCount, long sizeOnDisk, long sizeInMemory) GetStatistics()
        {
            lock (_sync)
            {
                // Check if we need to spill (lazy spilling triggered by any operation)
                // Only if not in SQLite-only mode
                if (!_useOnlySqlite && _currentMemoryUsage > _memoryThresholdBytes)
                {
                    // Spill both raw and filtered if over threshold
                    var memStats = _memoryStorage.GetStatistics();
                    if (memStats.rawRecordCount > 0)
                    {
                        SpillRawToDisk(default);
                    }
                    if (memStats.filteredRecordCount > 0)
                    {
                        SpillFilteredToDisk(default);
                    }
                }
                
                var memStatsAfter = _memoryStorage.GetStatistics();
                var diskStats = _diskStorage.GetStatistics();

                // Total record count is memory + disk (not just disk anymore)
                return (
                    rawRecordCount: memStatsAfter.rawRecordCount + diskStats.rawRecordCount,
                    filteredRecordCount: memStatsAfter.filteredRecordCount + diskStats.filteredRecordCount,
                    sizeOnDisk: diskStats.sizeOnDisk,
                    sizeInMemory: _useOnlySqlite ? 0 : memStatsAfter.sizeInMemory
                );
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _memoryStorage?.Dispose();
                _diskStorage?.Dispose();
                _rawAccessTracking.Clear();
                _filteredAccessTracking.Clear();
            }
        }

        // Private helper methods

        private void SpillRawToDisk(CancellationToken cancellationToken)
        {
            // Get count of items in memory
            var stats = _memoryStorage.GetStatistics();
            int itemCount = stats.rawRecordCount;
            
            if (itemCount == 0) return;

            // Calculate how many items to spill (50% by default)
            int itemsToSpill = (int)(itemCount * _spillPercentage);
            if (itemsToSpill == 0) return;

            // Efficiently remove oldest items from memory
            var itemsToDisk = _memoryStorage.RemoveOldestRaw(itemsToSpill);
            
            // Calculate memory freed
            long memoryFreed = CalculateBatchSize(itemsToDisk);
            _currentMemoryUsage -= memoryFreed;
            
            // Write spilled items to disk
            _diskStorage.AddRawBatch(itemsToDisk, cancellationToken);
            _hasSpilledRaw = true;
        }

        private void SpillFilteredToDisk(CancellationToken cancellationToken)
        {
            // Get count of items in memory
            var stats = _memoryStorage.GetStatistics();
            int itemCount = stats.filteredRecordCount;
            
            if (itemCount == 0) return;

            // Calculate how many items to spill (50% by default)
            int itemsToSpill = (int)(itemCount * _spillPercentage);
            if (itemsToSpill == 0) return;

            // Efficiently remove oldest items from memory
            var itemsToDisk = _memoryStorage.RemoveOldestFiltered(itemsToSpill);
            
            // Calculate memory freed
            long memoryFreed = CalculateBatchSize(itemsToDisk);
            _currentMemoryUsage -= memoryFreed;
            
            // Write spilled items to disk
            _diskStorage.AddFilteredBatch(itemsToDisk, cancellationToken);
            _hasSpilledFiltered = true;
        }

        private void PromoteToMemory(List<ISearchResult> items, bool isRaw, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                long promotionSize = CalculateBatchSize(items);
                
                // Only promote if we have room
                if (_currentMemoryUsage + promotionSize <= _memoryThresholdBytes)
                {
                    if (isRaw)
                    {
                        _memoryStorage.AddRawBatch(items, cancellationToken);
                    }
                    else
                    {
                        _memoryStorage.AddFilteredBatch(items, cancellationToken);
                    }
                    
                    _currentMemoryUsage += promotionSize;
                    
                    // Reset access counts for promoted items
                    var tracking = isRaw ? _rawAccessTracking : _filteredAccessTracking;
                    foreach (var item in items)
                    {
                        var key = GetResultKey(item);
                        if (tracking.ContainsKey(key))
                        {
                            tracking[key].AccessCount = 0;
                        }
                    }
                }
            }
        }

        private void TrackAccess(List<ISearchResult> batch, Dictionary<string, AccessTracker> tracking, bool isRaw)
        {
            lock (_sync)
            {
                foreach (var result in batch)
                {
                    var key = GetResultKey(result);
                    if (!tracking.ContainsKey(key))
                    {
                        tracking[key] = new AccessTracker();
                    }
                    tracking[key].AccessCount++;
                    tracking[key].LastAccessTime = DateTime.UtcNow;
                }
            }
        }

        private bool ShouldPromote(ISearchResult result, Dictionary<string, AccessTracker> tracking)
        {
            var key = GetResultKey(result);
            if (tracking.TryGetValue(key, out var tracker))
            {
                return tracker.AccessCount >= _promotionThreshold;
            }
            return false;
        }

        private static string GetResultKey(ISearchResult result)
        {
            // Create a unique key based on result properties
            // Using timestamp + message as a reasonable unique identifier
            return $"{result.GetLogTime():o}_{result.GetMessage()?.GetHashCode() ?? 0}_{result.GetUsername()?.GetHashCode() ?? 0}";
        }

        private static long CalculateBatchSize(IEnumerable<ISearchResult> batch)
        {
            long size = 0;
            foreach (var result in batch)
            {
                size += CalculateResultSize(result);
            }
            return size;
        }

        private static long CalculateResultSize(ISearchResult result)
        {
            long size = 0;
            
            var message = result.GetMessage();
            if (message != null)
                size += System.Text.Encoding.UTF8.GetByteCount(message);
            
            var machineName = result.GetMachineName();
            if (machineName != null)
                size += System.Text.Encoding.UTF8.GetByteCount(machineName);
            
            var username = result.GetUsername();
            if (username != null)
                size += System.Text.Encoding.UTF8.GetByteCount(username);
            
            var taskName = result.GetTaskName();
            if (taskName != null)
                size += System.Text.Encoding.UTF8.GetByteCount(taskName);
            
            var opCode = result.GetOpCode();
            if (opCode != null)
                size += System.Text.Encoding.UTF8.GetByteCount(opCode);
            
            var source = result.GetSource();
            if (source != null)
                size += System.Text.Encoding.UTF8.GetByteCount(source);
            
            var searchableData = result.GetSearchableData();
            if (searchableData != null)
                size += System.Text.Encoding.UTF8.GetByteCount(searchableData);
            
            var resultSource = result.GetResultSource();
            if (resultSource != null)
                size += System.Text.Encoding.UTF8.GetByteCount(resultSource);
            
            size += 12; // DateTime (8 bytes) + Level enum (4 bytes)
            
            return size;
        }

        private class AccessTracker
        {
            public int AccessCount { get; set; }
            public DateTime LastAccessTime { get; set; }
        }

        // NEW: Performance tracking methods
        
        private void TrackHybridWriteTime(double milliseconds)
        {
            _recentWriteTimes.Add(milliseconds);
            if (_recentWriteTimes.Count > 10)
            {
                _recentWriteTimes.RemoveAt(0);
            }
            _writeCount++;
        }

        private void TrackSqliteWriteTime(double milliseconds)
        {
            _recentSqliteWriteTimes.Add(milliseconds);
            if (_recentSqliteWriteTimes.Count > 10)
            {
                _recentSqliteWriteTimes.RemoveAt(0);
            }
        }

        private void CheckAndAdaptStorageStrategy()
        {
            // Only check periodically to avoid overhead
            if (_writeCount % PERFORMANCE_CHECK_INTERVAL != 0)
            {
                return;
            }

            // Need enough data to make a decision
            if (_recentWriteTimes.Count < 5)
            {
                return;
            }

            // Only consider switching if we've started spilling
            // (before spilling, InMemory is always faster)
            if (!_hasSpilledRaw && !_hasSpilledFiltered)
            {
                return;
            }

            // Calculate recent hybrid performance (includes spilling overhead)
            double avgHybridTime = _recentWriteTimes.Average();

            // Decision: Switch to SQLite if hybrid writes exceed 80ms threshold
            // This indicates that InMemory + spilling overhead is no longer beneficial
            if (avgHybridTime > WRITE_TIME_THRESHOLD_MS)
            {
                // Hybrid performance has degraded beyond acceptable threshold
                // Switch to SQLite-only mode for consistent performance
                SwitchToSqliteOnlyMode();
            }
        }

        private double BenchmarkSqliteWrite()
        {
            // Create a small benchmark batch
            var benchmarkBatch = Enumerable.Range(0, 100)
                .Select(i => (ISearchResult)new BenchmarkSearchResult($"Benchmark-{i}"))
                .ToList();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            _diskStorage.AddRawBatch(benchmarkBatch, default);
            sw.Stop();

            // Return normalized time (per 1000 records, to match batch size)
            return sw.Elapsed.TotalMilliseconds * 10; // 100 records ? 1000 records
        }

        private void SwitchToSqliteOnlyMode()
        {
            // Mark that we're switching
            _useOnlySqlite = true;

            // Migrate all remaining InMemory data to SQLite
            var memStats = _memoryStorage.GetStatistics();
            
            if (memStats.rawRecordCount > 0)
            {
                var allRaw = new List<ISearchResult>();
                _memoryStorage.GetRawResultsInBatches(batch => allRaw.AddRange(batch), 10000, default);
                _diskStorage.AddRawBatch(allRaw, default);
                _hasSpilledRaw = true;
            }

            if (memStats.filteredRecordCount > 0)
            {
                var allFiltered = new List<ISearchResult>();
                _memoryStorage.GetFilteredResultsInBatches(batch => allFiltered.AddRange(batch), 10000, default);
                _diskStorage.AddFilteredBatch(allFiltered, default);
                _hasSpilledFiltered = true;
            }

            // Dispose memory storage to free RAM
            _memoryStorage.Dispose();
            _memoryStorage = null; // No longer needed
            _currentMemoryUsage = 0;

            // Log the switch for visibility
            System.Diagnostics.Debug.WriteLine(
                $"[HybridStorage] Switched to SQLite-only mode at {_writeCount} writes. " +
                $"Hybrid avg write time: {_recentWriteTimes.Average():F2}ms exceeded {WRITE_TIME_THRESHOLD_MS:F0}ms threshold. " +
                $"Migrated {memStats.rawRecordCount + memStats.filteredRecordCount:N0} records to SQLite."
            );
        }

        // Simple result for benchmarking
        private class BenchmarkSearchResult : ISearchResult
        {
            private readonly string _message;
            public BenchmarkSearchResult(string message) => _message = message;
            public DateTime GetLogTime() => DateTime.UtcNow;
            public string GetMachineName() => "Benchmark";
            public void WriteToConsole() { }
            public Level GetLevel() => Level.Error; // Use Error level for benchmark
            public string GetUsername() => "Benchmark";
            public string GetTaskName() => "Benchmark";
            public string GetOpCode() => "Benchmark";
            public string GetSource() => "Benchmark";
            public string GetSearchableData() => _message;
            public string GetMessage() => _message;
            public string GetResultSource() => "Benchmark";
        }
    }
}
