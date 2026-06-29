namespace FindNeedleUX.Pages.NativeResultViewer;

/// <summary>Which overlay (if any) covers the results grid.</summary>
public enum ResultsOverlay
{
    /// <summary>No overlay — show the grid (rows are present, or streaming with rows is in progress).</summary>
    None,
    /// <summary>The loading spinner — work is in flight and there's nothing to show yet.</summary>
    Loading,
    /// <summary>The "No results" empty state — the load has fully settled with zero rows.</summary>
    Empty,
}

/// <summary>
/// Single source of truth for the results viewer's grid overlay. Extracted as a pure function because this
/// exact decision has regressed repeatedly: a long/streaming load would show "No results" (or, after over-
/// correcting, a blank grid with no spinner) during the window between "load started" and "first page +
/// count bound". The invariant: while ANY work is in flight with nothing yet to show, the user sees the
/// loading spinner — never "No results", never a blank grid. "No results" appears ONLY once the load has
/// fully settled (not loading, not streaming) and the count is genuinely zero.
/// </summary>
public static class ResultsOverlayPolicy
{
    /// <param name="visibleRowCount">Rows actually bound to the grid's current page (NOT the total/stale
    /// count). This is the signal that distinguishes "blank grid" from "grid showing rows" — a cache-reopen
    /// can carry a non-zero total count while the visible page hasn't been bound yet, which must still show
    /// the spinner, not a blank grid.</param>
    public static ResultsOverlay Decide(bool isLoading, bool isStreaming, bool isApplyingFilter, int visibleRowCount)
    {
        // Re-filtering an already-loaded set: cover the grid with the spinner while it recomputes.
        if (isApplyingFilter) return ResultsOverlay.Loading;

        // Work in flight (initial load or a streaming producer) and no rows on screen yet → spinner.
        // This is the case that kept regressing: the empty-state must NOT show here, and the grid must
        // NOT be left blank — even if a stale total count is still hanging around.
        if ((isLoading || isStreaming) && visibleRowCount <= 0) return ResultsOverlay.Loading;

        // Fully settled with nothing on screen → the explanatory empty state (the caller decides between
        // "no rows at all" and "filters hid everything" using the total count).
        if (!isLoading && !isStreaming && visibleRowCount <= 0) return ResultsOverlay.Empty;

        // Rows are on screen (or streaming with rows — the streaming banner handles that case) → grid.
        return ResultsOverlay.None;
    }
}
