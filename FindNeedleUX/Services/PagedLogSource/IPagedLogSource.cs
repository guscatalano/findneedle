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
    public static FilterSpec Empty { get; } = new("", "", "", "", "", "", null, null);

    public bool IsEmpty =>
        string.IsNullOrEmpty(Search) &&
        string.IsNullOrEmpty(Provider) &&
        string.IsNullOrEmpty(TaskName) &&
        string.IsNullOrEmpty(Message) &&
        string.IsNullOrEmpty(Source) &&
        string.IsNullOrEmpty(Level) &&
        FromTime is null && ToTime is null;
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

    /// <summary>Distinct level names present anywhere in the store (for the filter dropdown).</summary>
    List<string> GetDistinctLevels();

    /// <summary>Counts of each level after filters are applied (for the level chips).</summary>
    Dictionary<string, int> GetLevelCounts(FilterSpec filters);

    /// <summary>
    /// Stream every row matching <paramref name="filters"/> (in <paramref name="sort"/> order)
    /// through <paramref name="onItem"/>. Used by CSV export — implementations must not
    /// materialize the entire result in memory.
    /// </summary>
    void WalkAllFiltered(FilterSpec filters, SortSpec sort, Action<FindNeedleUX.LogLine> onItem);
}
