using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FindNeedleUX.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Unit tests for <see cref="SearchOrchestrator"/>. Covers TESTING_PLAN.md U-B6 (cancel promptly)
/// plus the grace-window auto-open behaviour and the cache/scanned status text. No WinUI runtime
/// needed — the search runner is faked.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
public class SearchOrchestratorTests
{
    /// <summary>
    /// Fake runner. The search task either completes after <paramref name="completeAfterMs"/> or,
    /// when that's negative, runs until cancelled — letting tests drive both the fast-finish and
    /// the cancel paths deterministically.
    /// </summary>
    private sealed class FakeRunner : ISearchRunner
    {
        private readonly int _completeAfterMs;
        public FakeRunner(int completeAfterMs) => _completeAfterMs = completeAfterMs;

        public bool LastSearchReusedCache { get; set; }
        public string Report { get; set; } = "5 results";
        public int RunStreamingCount { get; private set; }

        public SearchRunHandle RunStreaming(bool shallowSearch)
        {
            RunStreamingCount++;
            var cts = new CancellationTokenSource();
            var task = _completeAfterMs < 0
                ? Task.Delay(Timeout.Infinite, cts.Token)   // runs until cancelled
                : Task.Delay(_completeAfterMs, cts.Token);
            return new SearchRunHandle { SearchTask = task, Cancellation = cts };
        }

        public string GetSummaryReport() => Report;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // U-B6: Cancel promptly (≤ 200 ms) and report cancellation.
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task RunAsync_CancelsPromptly_WhenCancelInvoked()
    {
        var runner = new FakeRunner(completeAfterMs: -1); // never finishes on its own
        var orch = new SearchOrchestrator(runner);
        string? status = null;
        bool viewerOpened = false;

        var run = orch.RunAsync(false, onOpenViewer: () => viewerOpened = true,
                                onStatus: s => status = s, graceMs: 10);

        // Let it pass the grace window and settle into awaiting the search task.
        await Task.Delay(60);
        Assert.IsTrue(viewerOpened, "viewer should auto-open once past the grace window");

        var sw = Stopwatch.StartNew();
        orch.Cancel();
        await run;
        sw.Stop();

        Assert.IsTrue(sw.ElapsedMilliseconds <= 200, $"cancel should be prompt; took {sw.ElapsedMilliseconds}ms");
        Assert.AreEqual("Search cancelled.", status);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Grace window: fast search must NOT auto-open the viewer; slow one must.
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task RunAsync_FastSearch_DoesNotOpenViewer()
    {
        var runner = new FakeRunner(completeAfterMs: 0); // completes basically immediately
        var orch = new SearchOrchestrator(runner);
        bool viewerOpened = false;

        await orch.RunAsync(false, onOpenViewer: () => viewerOpened = true,
                            onStatus: _ => { }, graceMs: 200);

        Assert.IsFalse(viewerOpened, "a search finishing within the grace window should not open the viewer");
    }

    [TestMethod]
    public async Task RunAsync_SlowSearch_OpensViewer()
    {
        var runner = new FakeRunner(completeAfterMs: 120);
        var orch = new SearchOrchestrator(runner);
        bool viewerOpened = false;

        await orch.RunAsync(false, onOpenViewer: () => viewerOpened = true,
                            onStatus: _ => { }, graceMs: 10);

        Assert.IsTrue(viewerOpened, "a search running past the grace window should open the viewer");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Status text reflects cache vs fresh scan.
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task RunAsync_Completed_ReportsScannedOrCached()
    {
        var scanned = new FakeRunner(completeAfterMs: 0) { LastSearchReusedCache = false, Report = "10 results" };
        string? s1 = null;
        await new SearchOrchestrator(scanned).RunAsync(false, () => { }, s => s1 = s, graceMs: 100);
        Assert.AreEqual("(scanned) 10 results", s1);

        var cached = new FakeRunner(completeAfterMs: 0) { LastSearchReusedCache = true, Report = "10 results" };
        string? s2 = null;
        await new SearchOrchestrator(cached).RunAsync(false, () => { }, s => s2 = s, graceMs: 100);
        Assert.AreEqual("(from cache) 10 results", s2);
    }

    [TestMethod]
    public async Task RunAsync_PassesShallowFlagThrough_AndRunsOnce()
    {
        var runner = new FakeRunner(completeAfterMs: 0);
        await new SearchOrchestrator(runner).RunAsync(true, () => { }, _ => { }, graceMs: 50);
        Assert.AreEqual(1, runner.RunStreamingCount);
    }
}
