namespace FindNeedleUX.Services;

/// <summary>
/// How the result viewer sorts rows when a log first opens, before the user clicks any column header.
/// </summary>
public enum DefaultSortMode
{
    /// <summary>
    /// Load order — the exact order rows were read from the source (SQLite <c>Id ASC</c> / insertion
    /// order). This is the original/default behavior. For a single chronological file it looks
    /// time-ordered, but it is NOT guaranteed to be (e.g. ETW or multiple merged sources).
    /// </summary>
    LoadOrder,

    /// <summary>Sort by the Time column ascending (oldest first) on open.</summary>
    TimeAscending,

    /// <summary>Sort by the Time column descending (newest first) on open.</summary>
    TimeDescending,
}
