namespace FindPluginCore.Searching;

/// <summary>
/// When the full-text (substring) search index is built for a SQLite-backed search. The index is
/// only needed for substring search, and on a large log it can take minutes to build, so deferring
/// it lets the viewer open immediately.
/// </summary>
public enum IndexingMode
{
    /// <summary>Build the index lazily, the first time the user runs a substring search. Sessions
    /// that only open + scroll never pay for it. (Default.)</summary>
    Lazy,

    /// <summary>Build the index in the background right after the viewer opens, so substring search
    /// becomes fast shortly after, while paging works immediately. Search uses the slower scan until
    /// it finishes.</summary>
    Background,

    /// <summary>Build the index during the search, before the viewer opens (substring search is
    /// instant, but a large log's viewer open waits for the whole index). This is the CLI default.</summary>
    Eager
}
