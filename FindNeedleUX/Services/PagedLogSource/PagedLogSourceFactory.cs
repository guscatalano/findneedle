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
}
