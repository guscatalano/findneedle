using System;
using System.Collections.Generic;
using System.Threading;

namespace FindNeedlePluginLib.Interfaces
{
    /// <summary>
    /// Interface for storage of search results, supporting batched input and statistics reporting.
    /// Now inherits IDisposable so implementations can be disposed by callers.
    /// </summary>
    public interface ISearchStorage : IDisposable
    {
        /// <summary>
        /// Add a batch of raw (pre-filtered) search results to the storage.
        /// </summary>
        /// <param name="batch">Batch of raw search results.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        void AddRawBatch(IEnumerable<ISearchResult> batch, CancellationToken cancellationToken = default);

        /// <summary>
        /// Add a batch of post-filtered search results to the storage.
        /// </summary>
        /// <param name="batch">Batch of filtered search results.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        void AddFilteredBatch(IEnumerable<ISearchResult> batch, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get all stored raw search results in batches.
        /// </summary>
        /// <param name="onBatch">Callback for each batch.</param>
        /// <param name="batchSize">Size of each batch.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        void GetRawResultsInBatches(Action<List<ISearchResult>> onBatch, int batchSize = 1000, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get all stored filtered search results in batches.
        /// </summary>
        /// <param name="onBatch">Callback for each batch.</param>
        /// <param name="batchSize">Size of each batch.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        void GetFilteredResultsInBatches(Action<List<ISearchResult>> onBatch, int batchSize = 1000, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get statistics about the storage.
        /// </summary>
        /// <returns>Statistics: number of raw records, number of filtered records, size on disk (bytes), size in memory (bytes).</returns>
        (int rawRecordCount, int filteredRecordCount, long sizeOnDisk, long sizeInMemory) GetStatistics();

        /// <summary>
        /// Remove all stored raw and filtered results, returning the storage to an empty state.
        /// Disk-backed implementations open any existing cache as-is (they do not wipe on
        /// construction), so a consumer about to run a fresh scan must call this first to avoid
        /// surfacing stale or duplicated rows. Cheap no-op when already empty.
        /// </summary>
        void ClearTables();

        /// <summary>
        /// Build (or finish building) any search index over the filtered results, after all batches
        /// have been added. Disk-backed implementations defer the expensive full-text index to this
        /// single bulk step instead of maintaining it per-row during ingest — far faster. Until this
        /// is called, substring search falls back to a scan. No-op for backends without an index.
        /// Cancellable; <paramref name="onProgress"/> receives (rowsIndexed, totalRows) as it runs.
        /// </summary>
        void BuildSearchIndex(CancellationToken cancellationToken = default, Action<long, long> onProgress = null);
    }

    
}
