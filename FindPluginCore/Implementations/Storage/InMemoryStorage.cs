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
        private readonly List<ISearchResult> _rawResults = new();
        private readonly List<ISearchResult> _filteredResults = new();
        private readonly object _sync = new();

        public void AddRawBatch(IEnumerable<ISearchResult> batch, CancellationToken cancellationToken = default)
        {
            if (batch == null) throw new ArgumentNullException(nameof(batch));
            var toAdd = new List<ISearchResult>();
            foreach (var result in batch)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                toAdd.Add(result);
            }
            if (toAdd.Count == 0) return;
            lock (_sync)
            {
                _rawResults.AddRange(toAdd);
            }
        }

        public void AddFilteredBatch(IEnumerable<ISearchResult> batch, CancellationToken cancellationToken = default)
        {
            if (batch == null) throw new ArgumentNullException(nameof(batch));
            var toAdd = new List<ISearchResult>();
            foreach (var result in batch)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                toAdd.Add(result);
            }
            if (toAdd.Count == 0) return;
            lock (_sync)
            {
                _filteredResults.AddRange(toAdd);
            }
        }

        public void GetRawResultsInBatches(Action<List<ISearchResult>> onBatch, int batchSize = 1000, CancellationToken cancellationToken = default)
        {
            if (onBatch == null) throw new ArgumentNullException(nameof(onBatch));
            List<ISearchResult> snapshot;
            lock (_sync)
            {
                snapshot = new List<ISearchResult>(_rawResults);
            }

            var batch = new List<ISearchResult>(batchSize);
            foreach (var result in snapshot)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                batch.Add(result);
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
            List<ISearchResult> snapshot;
            lock (_sync)
            {
                snapshot = new List<ISearchResult>(_filteredResults);
            }

            var batch = new List<ISearchResult>(batchSize);
            foreach (var result in snapshot)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                batch.Add(result);
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
            List<ISearchResult> rawSnapshot, filteredSnapshot;
            lock (_sync)
            {
                rawSnapshot = new List<ISearchResult>(_rawResults);
                filteredSnapshot = new List<ISearchResult>(_filteredResults);
            }

            int rawRecordCount = rawSnapshot.Count;
            int filteredRecordCount = filteredSnapshot.Count;
            long sizeInMemory = 0;
            
            foreach (var result in rawSnapshot)
            {
                sizeInMemory += CalculateResultSize(result);
            }
            foreach (var result in filteredSnapshot)
            {
                sizeInMemory += CalculateResultSize(result);
            }
            
            long sizeOnDisk = 0;
            return (rawRecordCount, filteredRecordCount, sizeOnDisk, sizeInMemory);
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
    }
}
