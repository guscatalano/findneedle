namespace FindNeedleUX.Services;

/// <summary>
/// How the result viewer's search box decides when to actually run a search.
///   Live    — search on every keystroke (snappy on small logs; can lag on huge ones)
///   OnEnter — only search when the user presses Enter (no mid-typing searches)
///   Auto    — start live, but switch to Enter-to-search once a search is slow (or the log is
///             large), and switch back to live once searches are fast again
/// </summary>
public enum SearchSubmitMode
{
    Auto,
    Live,
    OnEnter,
}
