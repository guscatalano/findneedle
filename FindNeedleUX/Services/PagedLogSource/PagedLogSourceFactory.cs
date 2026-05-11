using System;
using System.Collections.Generic;
using FindNeedlePluginLib.Interfaces;
using FindPluginCore.Implementations.Storage;

namespace FindNeedleUX.Services.PagedLogSource;

/// <summary>
/// Builds the right <see cref="IPagedLogSource"/> for the current search's storage backend.
///
/// Mapping:
///   <see cref="InMemoryStorage"/>  ->  <see cref="InMemoryPagedSource"/> over the materialized list
///   <see cref="SqliteStorage"/>    ->  <see cref="SqlitePagedSource"/> over the connection
///   <see cref="HybridStorage"/>    ->  call <c>SettleToDisk</c>, then <see cref="SqlitePagedSource"/>
///   anything else / null         ->  <see cref="InMemoryPagedSource"/> over the fallback list
/// </summary>
public static class PagedLogSourceFactory
{
    /// <summary>
    /// Picks the appropriate paged source for the current search.
    /// </summary>
    /// <param name="storage">The search's underlying storage (or null).</param>
    /// <param name="fallbackInMemory">
    /// Used when <paramref name="storage"/> is in-memory or null. The viewer can pass the result
    /// of <c>MiddleLayerService.GetLogLines()</c> here to preserve current behavior.
    /// </param>
    public static IPagedLogSource Create(ISearchStorage storage, IList<FindNeedleUX.LogLine> fallbackInMemory)
    {
        switch (storage)
        {
            case SqliteStorage sql:
                // Source doesn't own the storage — search query / NuSearchQuery owns its lifecycle.
                return new SqlitePagedSource(sql, ownsStorage: false);

            case HybridStorage hybrid:
                hybrid.SettleToDisk();
                return new SqlitePagedSource(hybrid.InnerSqliteStorage, ownsStorage: false);

            // InMemoryStorage and unknown types fall back to the materialized LogLine list.
            default:
                return new InMemoryPagedSource(fallbackInMemory ?? new List<FindNeedleUX.LogLine>());
        }
    }

    /// <summary>
    /// Build a paged source that's already wired for live streaming: starts in
    /// <c>IsLoading=true</c> and forwards <c>SqliteStorage.FilteredRowsAdded</c> as the source's
    /// <c>RowsAvailable</c> event. The producer (search task) must call
    /// <see cref="IPagedLogSource.MarkLoadingComplete"/> when done.
    ///
    /// Only valid for SqliteStorage — other storage backends are not safe for concurrent
    /// read+write from a live viewer, so this throws if the storage type doesn't qualify. The
    /// streaming entry point in MiddleLayerService is responsible for forcing the right storage.
    /// </summary>
    public static IPagedLogSource CreateStreaming(SqliteStorage storage)
    {
        if (storage == null) throw new ArgumentNullException(nameof(storage));
        return new SqlitePagedSource(storage, ownsStorage: false, startInLoadingState: true);
    }
}
