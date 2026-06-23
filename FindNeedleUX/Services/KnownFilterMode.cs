namespace FindNeedleUX.Services;

/// <summary>
/// How a "show known" filter dropdown (Provider / TaskName / Source) lets the user pick values:
/// pick exactly one (Single) or pick several at once (Multi, matched as an OR-set). Configured
/// per-field in preferences.
/// </summary>
public enum KnownFilterMode
{
    /// <summary>Pick one known value; sets that column's filter to it.</summary>
    Single,

    /// <summary>Pick multiple known values; rows matching ANY of them are kept (exact-match OR-set).</summary>
    Multi,
}
