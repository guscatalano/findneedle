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
    /// </summary>
    public class InMemoryStorage : ISearchStorage
    {
        private readonly List<ISearchResult> _rawResults = new();
        private readonly List<ISearchResult> _filteredResults = new();

        public void AddRawBatch(IEnumerable<ISearchResult> batch, CancellationToken cancellationToken = default)
        {
            foreach (var result in batch)
            {
                if (cancellationToken.IsCancellationRequested) break;
                _rawResults.Add(result);
            }
        }

        public void AddFilteredBatch(IEnumerable<ISearchResult> batch, CancellationToken cancellationToken = default)
        {
            foreach (var result in batch)
            {
                if (cancellationToken.IsCancellationRequested) break;
                _filteredResults.Add(result);
            }
        }

        public void GetRawResultsInBatches(Action<List<ISearchResult>> onBatch, int batchSize = 1000, CancellationToken cancellationToken = default)
        {
            var batch = new List<ISearchResult>(batchSize);
            foreach (var result in _rawResults)
            {
                if (cancellationToken.IsCancellationRequested) break;
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
            var batch = new List<ISearchResult>(batchSize);
            foreach (var result in _filteredResults)
            {
                if (cancellationToken.IsCancellationRequested) break;
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
            int rawRecordCount = _rawResults.Count;
            int filteredRecordCount = _filteredResults.Count;
            long sizeInMemory = 0;
            foreach (var result in _rawResults)
            {
                var msg = result.GetMessage();
                if (msg != null)
                    sizeInMemory += System.Text.Encoding.UTF8.GetByteCount(msg);
            }
            foreach (var result in _filteredResults)
            {
                var msg = result.GetMessage();
                if (msg != null)
                    sizeInMemory += System.Text.Encoding.UTF8.GetByteCount(msg);
            }
            long sizeOnDisk = 0;
            return (rawRecordCount, filteredRecordCount, sizeOnDisk, sizeInMemory);
        }
    }
}
