using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FindNeedlePluginLib;
using FindNeedlePluginLib.Interfaces;

namespace FindPluginCore.Implementations.Storage
{
    /// <summary>
    /// In-memory implementation of ISearchStorage using separate lists for raw and filtered results.
    /// Thread-safe for concurrent Add and read operations.
    /// </summary>
    public class InMemoryStorage : ISearchStorage
    {
        private readonly List<AccessTrackedResult> _rawResults = new();
        private readonly List<AccessTrackedResult> _filteredResults = new();
        private readonly object _sync = new();

        // Cache statistics to avoid expensive recalculation on every call
        private (int rawRecordCount, int filteredRecordCount, long sizeOnDisk, long sizeInMemory)? _cachedStats = null;
        private bool _statsInvalid = true;

        // Wrapper to track access time for LRU eviction
        private class AccessTrackedResult
        {
            public ISearchResult Result { get; set; }
            public DateTime LastAccessTime { get; set; }

            public AccessTrackedResult(ISearchResult result)
            {
                Result = result;
                LastAccessTime = DateTime.UtcNow;
            }

            public void MarkAccessed()
            {
                LastAccessTime = DateTime.UtcNow;
            }
        }

        public void AddRawBatch(IEnumerable<ISearchResult> batch, CancellationToken cancellationToken = default)
        {
            if (batch == null) throw new ArgumentNullException(nameof(batch));
            var toAdd = new List<AccessTrackedResult>();
            foreach (var result in batch)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                toAdd.Add(new AccessTrackedResult(result));
            }
            if (toAdd.Count == 0) return;
            lock (_sync)
            {
                _rawResults.AddRange(toAdd);
                _statsInvalid = true; // Invalidate cache
            }
        }

        public void AddFilteredBatch(IEnumerable<ISearchResult> batch, CancellationToken cancellationToken = default)
        {
            if (batch == null) throw new ArgumentNullException(nameof(batch));
            var toAdd = new List<AccessTrackedResult>();
            foreach (var result in batch)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                toAdd.Add(new AccessTrackedResult(result));
            }
            if (toAdd.Count == 0) return;
            lock (_sync)
            {
                _filteredResults.AddRange(toAdd);
                _statsInvalid = true; // Invalidate cache
            }
        }

        public void GetRawResultsInBatches(Action<List<ISearchResult>> onBatch, int batchSize = 1000, CancellationToken cancellationToken = default)
        {
            if (onBatch == null) throw new ArgumentNullException(nameof(onBatch));
            List<AccessTrackedResult> snapshot;
            lock (_sync)
            {
                snapshot = new List<AccessTrackedResult>(_rawResults);
            }

            var batch = new List<ISearchResult>(batchSize);
            foreach (var trackedResult in snapshot)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                
                // Mark as accessed (important for LRU tracking)
                lock (_sync)
                {
                    trackedResult.MarkAccessed();
                }
                
                batch.Add(trackedResult.Result);
                if (batch.Count == batchSize)
                {
                    onBatch(batch);
                    batch = new List<ISearchResult>(batchSize);
                }
            }
            if (batch.Count > 0)
            {
                onBatch(batch);
            }
        }

        public void GetFilteredResultsInBatches(Action<List<ISearchResult>> onBatch, int batchSize = 1000, CancellationToken cancellationToken = default)
        {
            if (onBatch == null) throw new ArgumentNullException(nameof(onBatch));
            List<AccessTrackedResult> snapshot;
            lock (_sync)
            {
                snapshot = new List<AccessTrackedResult>(_filteredResults);
            }

            var batch = new List<ISearchResult>(batchSize);
            foreach (var trackedResult in snapshot)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                
                // Mark as accessed (important for LRU tracking)
                lock (_sync)
                {
                    trackedResult.MarkAccessed();
                }
                
                batch.Add(trackedResult.Result);
                if (batch.Count == batchSize)
                {
                    onBatch(batch);
                    batch = new List<ISearchResult>(batchSize);
                }
            }
            if (batch.Count > 0)
            {
                onBatch(batch);
            }
        }

        public (int rawRecordCount, int filteredRecordCount, long sizeOnDisk, long sizeInMemory) GetStatistics()
        {
            lock (_sync)
            {
                // Return cached statistics if still valid
                if (!_statsInvalid && _cachedStats.HasValue)
                {
                    return _cachedStats.Value;
                }

                // Recalculate statistics
                int rawRecordCount = _rawResults.Count; // O(1) - just get Count
                int filteredRecordCount = _filteredResults.Count; // O(1)
                long sizeInMemory = 0;
                
                // Only calculate size if we actually have records
                if (rawRecordCount > 0 || filteredRecordCount > 0)
                {
                    foreach (var trackedResult in _rawResults)
                    {
                        sizeInMemory += CalculateResultSize(trackedResult.Result);
                    }
                    foreach (var trackedResult in _filteredResults)
                    {
                        sizeInMemory += CalculateResultSize(trackedResult.Result);
                    }
                }
                
                long sizeOnDisk = 0;
                var stats = (rawRecordCount, filteredRecordCount, sizeOnDisk, sizeInMemory);
                
                // Cache the result
                _cachedStats = stats;
                _statsInvalid = false;
                
                return stats;
            }
        }

        private static long CalculateResultSize(ISearchResult result)
        {
            long size = 0;
            
            // Calculate size for all string fields that are stored
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
            
            // Add overhead for DateTime (8 bytes) and Level enum (4 bytes)
            size += 12;
            
            return size;
        }

        // Implement IDisposable because ISearchStorage now inherits IDisposable.
        public void Dispose()
        {
            // No unmanaged resources to release for in-memory storage.
            // Method provided so callers can use 'using' with ISearchStorage.
        }

        /// <summary>
        /// Efficiently removes the least recently accessed (oldest) raw results for spilling to disk.
        /// Returns the removed items sorted by last access time (oldest first).
        /// </summary>
        public List<ISearchResult> RemoveOldestRaw(int count)
        {
            lock (_sync)
            {
                if (_rawResults.Count == 0) return new List<ISearchResult>();

                // Sort by LastAccessTime (oldest first) and take the requested count
                var toRemove = _rawResults
                    .OrderBy(r => r.LastAccessTime)
                    .Take(count)
                    .ToList();

                if (toRemove.Count == 0) return new List<ISearchResult>();

                // Create a HashSet for O(1) lookup during removal
                var toRemoveSet = new HashSet<AccessTrackedResult>(toRemove);

                // Use RemoveAll for efficient O(n) bulk removal instead of O(n²)
                _rawResults.RemoveAll(item => toRemoveSet.Contains(item));

                _statsInvalid = true; // Invalidate cache after removing

                // Return the actual ISearchResult objects
                return toRemove.Select(r => r.Result).ToList();
            }
        }

        /// <summary>
        /// Efficiently removes the least recently accessed (oldest) filtered results for spilling to disk.
        /// Returns the removed items sorted by last access time (oldest first).
        /// </summary>
        public List<ISearchResult> RemoveOldestFiltered(int count)
        {
            lock (_sync)
            {
                if (_filteredResults.Count == 0) return new List<ISearchResult>();

                // Sort by LastAccessTime (oldest first) and take the requested count
                var toRemove = _filteredResults
                    .OrderBy(r => r.LastAccessTime)
                    .Take(count)
                    .ToList();

                if (toRemove.Count == 0) return new List<ISearchResult>();

                // Create a HashSet for O(1) lookup during removal
                var toRemoveSet = new HashSet<AccessTrackedResult>(toRemove);

                // Use RemoveAll for efficient O(n) bulk removal instead of O(n²)
                _filteredResults.RemoveAll(item => toRemoveSet.Contains(item));

                _statsInvalid = true; // Invalidate cache after removing

                // Return the actual ISearchResult objects
                return toRemove.Select(r => r.Result).ToList();
            }
        }
    }
}
