namespace FindNeedleUX.Services;

/// <summary>
/// Pure decision logic for the result viewer's adaptive search-submit behavior, factored out of the
/// (UI) result page so it can be unit-tested without a XAML host. Searching runs synchronously on the
/// UI thread, so on a multi-million-row log a per-keystroke search blocks typing; this policy decides
/// when to require Enter instead of searching live.
///
/// Thresholds are tuned to measured latency (see CoreTests SearchLatencyBenchmark): an un-indexed
/// (LIKE) "find one line" scan crosses ~1s at roughly 2M rows, while an FTS-indexed lookup is ~1-2ms
/// regardless of size.
/// </summary>
public static class SearchSubmitPolicy
{
    /// <summary>A search slower than this (Auto) switches to Enter-to-search.</summary>
    public const long SlowSearchMs = 1000;

    /// <summary>A search faster than this (Auto) switches back to live search.</summary>
    public const long FastSearchMs = 400;

    /// <summary>
    /// Row count at/above which Auto seeds Enter-to-search: a LIKE scan per keystroke crosses ~1s
    /// here, matching the "search per keystroke &gt; 1s → Enter" rule.
    /// </summary>
    public const int LargeRowSeed = 2_000_000;

    /// <summary>Whether, given the mode and current adaptive state, typing should NOT search live.</summary>
    public static bool RequireEnter(SearchSubmitMode mode, bool autoEnterActive) => mode switch
    {
        SearchSubmitMode.Live    => false,
        SearchSubmitMode.OnEnter => true,
        _                        => autoEnterActive, // Auto
    };

    /// <summary>
    /// Initial Auto state when (re)loading a result set: start in Enter-to-search when the log is
    /// large enough that an un-indexed per-keystroke search would be slow, and the FTS index isn't
    /// built yet (a built index makes live search instant). Only Auto uses the seed.
    /// </summary>
    public static bool ShouldSeedEnterToSearch(SearchSubmitMode mode, int totalRows, bool indexBuilt)
        => mode == SearchSubmitMode.Auto && totalRows >= LargeRowSeed && !indexBuilt;

    /// <summary>
    /// Adaptive transition for Auto after a timed search: a slow search switches to Enter-to-search;
    /// a fast one switches back to live; in between keep the current state (hysteresis avoids flapping
    /// while latency hovers near the threshold, e.g. as the FTS index finishes building).
    /// </summary>
    public static bool NextAutoEnterState(bool current, long lastSearchMs)
    {
        if (lastSearchMs > SlowSearchMs) return true;
        if (lastSearchMs < FastSearchMs) return false;
        return current;
    }
}
