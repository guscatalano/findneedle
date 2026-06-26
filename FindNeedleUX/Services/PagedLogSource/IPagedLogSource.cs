using System;
using System.Collections.Generic;

namespace FindNeedleUX.Services.PagedLogSource;

/// <summary>
/// Snapshot of the user's current filter inputs. Records have value-equality so consumers can
/// memoize results keyed by (filter, sort) tuples.
/// </summary>
public sealed record FilterSpec(
    string Search,
    string Provider,
    string TaskName,
    string Message,
    string Source,
    string Level,
    DateTime? FromTime,
    DateTime? ToTime)
{
    /// <summary>
    /// Multi-select OR-sets for the "show known" dropdowns. When non-empty, the row's exact value must
    /// be one of these (case-insensitive) — this takes precedence over the matching substring field
    /// above. Null/empty = not used. Declared as init-only properties so existing positional
    /// construction stays valid; records still include them in value-equality (memoization-safe).
    /// </summary>
    public IReadOnlyList<string> ProviderSet { get; init; }
    public IReadOnlyList<string> TaskNameSet { get; init; }
    public IReadOnlyList<string> SourceSet { get; init; }

    /// <summary>Parsed structured query (from the search box's query language), applied as an extra AND
    /// across both backends. Null when the search is a plain substring (the <see cref="Search"/> path).</summary>
    public FindPluginCore.Searching.Query.QueryNode Query { get; init; }

    public static FilterSpec Empty { get; } = new("", "", "", "", "", "", null, null);

    private static bool HasItems(IReadOnlyList<string> s) => s != null && s.Count > 0;

    public bool IsEmpty =>
        string.IsNullOrEmpty(Search) &&
        string.IsNullOrEmpty(Provider) &&
        string.IsNullOrEmpty(TaskName) &&
        string.IsNullOrEmpty(Message) &&
        string.IsNullOrEmpty(Source) &&
        string.IsNullOrEmpty(Level) &&
        FromTime is null && ToTime is null &&
        !HasItems(ProviderSet) && !HasItems(TaskNameSet) && !HasItems(SourceSet) &&
        Query is null;
}

/// <summary>
/// Sort spec. <see cref="Column"/> is one of the LogLine column names (Index/Time/Provider/
/// TaskName/Message/Source/Level). Empty / null Column means "no explicit sort"
/// (insertion / load order).
/// </summary>
public sealed record SortSpec(string Column, bool Descending)
{
    public static SortSpec None { get; } = new(null, false);

    public bool IsSorted => !string.IsNullOrEmpty(Column);
}

/// <summary>
/// Abstraction over the search-result data set so the result viewer doesn't need to materialize
/// every row up-front. Implementations can back the source with an in-memory list, a SQLite
/// connection, or a hybrid storage (RAM + spilled-to-disk).
/// </summary>
public interface IPagedLogSource : IDisposable
{
    /// <summary>Total rows in the underlying store, ignoring filters.</summary>
    int TotalCount { get; }

    /// <summary>Number of rows that match <paramref name="filters"/>.</summary>
    int GetFilteredCount(FilterSpec filters);

    /// <summary>
    /// Return the page <c>[offset, offset + limit)</c> of the filter+sort result set. Offsets past
    /// the end return an empty list. The returned LogLine objects' <c>Index</c> field is the row's
    /// 0-based position in the filter+sort result, which is what the viewer displays.
    /// </summary>
    List<FindNeedleUX.LogLine> GetPage(FilterSpec filters, SortSpec sort, int offset, int limit);

    /// <summary>
    /// Look up a single row by its stable <see cref="FindNeedleUX.LogLine.RowId"/>, independent of
    /// the current filter/sort/paging. Returns null if no row with that id exists. Used for record
    /// lookup (the MCP <c>get_record</c> tool) and tagging by stable id.
    /// </summary>
    FindNeedleUX.LogLine GetByRowId(long rowId);

    /// <summary>Distinct level names present anywhere in the store (for the filter dropdown).</summary>
    List<string> GetDistinctLevels();

    /// <summary>Counts of each level after filters are applied (for the level chips).</summary>
    Dictionary<string, int> GetLevelCounts(FilterSpec filters);

    /// <summary>
    /// Exact distinct values of the viewer's "Source" column (each row's GetResultSource — the file
    /// path) with their row counts, ignoring filters. Cheap by design (a SQL GROUP BY for SQLite, a
    /// tally for in-memory) — distinct sources are bounded by the loaded files, NOT the row count — so
    /// the Sources dialog can list/group them without the O(rows) facet scan.
    /// </summary>
    Dictionary<string, int> GetSourceCounts();

    /// <summary>
    /// Exact distinct values + counts of one "known value" filter field (Provider / TaskName / Source)
    /// over the rows matching <paramref name="filters"/>. A cheap SQL GROUP BY for SQLite (vs the old
    /// O(sample) row scan) so the known-value dropdowns populate fast even on millions of rows.
    /// <paramref name="filters"/> is the cross-filter spec with THIS field's own selection already
    /// cleared (so its list shows what's reachable given the other filters, not just what's picked).
    /// </summary>
    Dictionary<string, int> GetFieldCounts(string field, FilterSpec filters);

    /// <summary>
    /// Stream every row matching <paramref name="filters"/> (in <paramref name="sort"/> order)
    /// through <paramref name="onItem"/>. Used by CSV export — implementations must not
    /// materialize the entire result in memory.
    /// </summary>
    void WalkAllFiltered(FilterSpec filters, SortSpec sort, Action<FindNeedleUX.LogLine> onItem);

    // ----- Streaming surface -----
    // These let the viewer open while a search is still producing results: it shows the rows
    // that have landed so far, refreshes when more arrive, and hides its loading badge when
    // the producer signals completion. Implementations over a stable data set (in-memory) leave
    // IsLoading false forever and never raise RowsAvailable — the viewer's code paths are
    // identical either way.

    /// <summary>
    /// True while a producer is still writing to the underlying store. The viewer uses this to
    /// show a loading badge and a Stop button. Flips to false when
    /// <see cref="MarkLoadingComplete"/> is called.
    /// </summary>
    bool IsLoading { get; }

    /// <summary>
    /// Raised (potentially from a background thread) when new rows are appended to the underlying
    /// store. The viewer should marshal back to the UI thread, debounce, then refresh visible
    /// state. Does not carry the new count — subscribers should re-query <c>GetFilteredCount</c>.
    /// </summary>
    event Action? RowsAvailable;

    /// <summary>
    /// Called by the producer (search task) when it has finished writing. Flips
    /// <see cref="IsLoading"/> to false and raises <see cref="RowsAvailable"/> one last time so
    /// the viewer gets a final refresh. Idempotent.
    /// </summary>
    void MarkLoadingComplete();
}
