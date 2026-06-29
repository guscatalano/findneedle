using FindNeedleUX.Pages.NativeResultViewer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUX.UnitTests;

/// <summary>
/// Regression guard for the results-grid overlay decision (<see cref="ResultsOverlayPolicy"/>). This exact
/// behavior has broken several times: during a long/streaming/cache-reopen load the viewer would either flash
/// "No results" or sit blank with no spinner, in the window between "load started" and "first page + count
/// bound". The invariant under test: while ANY work is in flight with nothing bound yet, the overlay is the
/// loading SPINNER — never the empty state, never nothing. "No results" appears ONLY when fully settled
/// (not loading, not streaming) with zero rows.
/// </summary>
[TestClass]
public class ResultsOverlayPolicyTests
{
    // --- The regressions: mid-load must show the spinner, NOT empty and NOT blank ---

    [TestMethod]
    public void Loading_NoRowsYet_ShowsSpinner_NotEmpty()
        => Assert.AreEqual(ResultsOverlay.Loading,
            ResultsOverlayPolicy.Decide(isLoading: true, isStreaming: false, isApplyingFilter: false, visibleRowCount: 0));

    [TestMethod]
    public void Streaming_NoRowsYet_ShowsSpinner_NotEmpty()
        => Assert.AreEqual(ResultsOverlay.Loading,
            ResultsOverlayPolicy.Decide(isLoading: false, isStreaming: true, isApplyingFilter: false, visibleRowCount: 0));

    [TestMethod]
    public void Loading_NoVisibleRowsYet_ShowsSpinner_NotBlankGrid()
        // The cache-reopen regression: still loading, the visible page hasn't been bound — must be the
        // spinner, NOT a blank grid (a stale total count must not trick it into showing the empty grid).
        => Assert.AreEqual(ResultsOverlay.Loading,
            ResultsOverlayPolicy.Decide(isLoading: true, isStreaming: false, isApplyingFilter: false, visibleRowCount: 0));

    [TestMethod]
    public void ApplyingFilter_ShowsSpinner()
        => Assert.AreEqual(ResultsOverlay.Loading,
            ResultsOverlayPolicy.Decide(isLoading: false, isStreaming: false, isApplyingFilter: true, visibleRowCount: 123));

    // --- Empty state ONLY when truly settled with zero rows ---

    [TestMethod]
    public void Settled_ZeroRows_ShowsEmpty()
        => Assert.AreEqual(ResultsOverlay.Empty,
            ResultsOverlayPolicy.Decide(isLoading: false, isStreaming: false, isApplyingFilter: false, visibleRowCount: 0));

    // --- Grid (no overlay) when rows are on screen ---

    [TestMethod]
    public void Settled_WithRows_ShowsGrid()
        => Assert.AreEqual(ResultsOverlay.None,
            ResultsOverlayPolicy.Decide(isLoading: false, isStreaming: false, isApplyingFilter: false, visibleRowCount: 500));

    [TestMethod]
    public void Streaming_WithRows_ShowsGrid_BannerHandlesIt()
        // Once rows are visible, the streaming banner (not the full overlay) communicates ongoing load.
        => Assert.AreEqual(ResultsOverlay.None,
            ResultsOverlayPolicy.Decide(isLoading: false, isStreaming: true, isApplyingFilter: false, visibleRowCount: 5000));

    [TestMethod]
    public void Loading_WithVisibleRows_ShowsGrid()
        // Already showing a page while more loads (e.g. paging during a background index build) → grid.
        => Assert.AreEqual(ResultsOverlay.None,
            ResultsOverlayPolicy.Decide(isLoading: true, isStreaming: false, isApplyingFilter: false, visibleRowCount: 5000));
}
