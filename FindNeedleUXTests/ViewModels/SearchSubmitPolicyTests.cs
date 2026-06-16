using FindNeedleUX.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Tests for <see cref="SearchSubmitPolicy"/> — the decision logic behind the result viewer's
/// adaptive "live vs Enter-to-search" behavior. The goal these protect: typing in the search box
/// stays smooth — small/fast logs search as you type, and big logs (where a per-keystroke scan would
/// lag) switch to Enter-to-search so typing never blocks.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
public class SearchSubmitPolicyTests
{
    // ── RequireEnter: how each mode maps to "wait for Enter" ────────────────────

    [TestMethod]
    public void RequireEnter_Live_NeverWaitsForEnter()
    {
        Assert.IsFalse(SearchSubmitPolicy.RequireEnter(SearchSubmitMode.Live, autoEnterActive: false));
        Assert.IsFalse(SearchSubmitPolicy.RequireEnter(SearchSubmitMode.Live, autoEnterActive: true));
    }

    [TestMethod]
    public void RequireEnter_OnEnter_AlwaysWaitsForEnter()
    {
        Assert.IsTrue(SearchSubmitPolicy.RequireEnter(SearchSubmitMode.OnEnter, autoEnterActive: false));
        Assert.IsTrue(SearchSubmitPolicy.RequireEnter(SearchSubmitMode.OnEnter, autoEnterActive: true));
    }

    [TestMethod]
    public void RequireEnter_Auto_FollowsAdaptiveState()
    {
        Assert.IsFalse(SearchSubmitPolicy.RequireEnter(SearchSubmitMode.Auto, autoEnterActive: false),
            "Auto with fast searches should search live (smooth typing)");
        Assert.IsTrue(SearchSubmitPolicy.RequireEnter(SearchSubmitMode.Auto, autoEnterActive: true),
            "Auto on a large/slow log should wait for Enter");
    }

    // ── ShouldSeedEnterToSearch: initial state when a result set loads ──────────

    [TestMethod]
    public void Seed_SmallLog_StartsLive_ForSmoothTyping()
    {
        // 250k rows search in ~100ms (measured) — typing should be live, no Enter needed.
        Assert.IsFalse(SearchSubmitPolicy.ShouldSeedEnterToSearch(SearchSubmitMode.Auto, totalRows: 250_000, indexBuilt: false));
    }

    [TestMethod]
    public void Seed_LargeLog_NotIndexed_StartsInEnterToSearch()
    {
        // 5M rows via LIKE is ~2.3s per keystroke (measured) — must not search live.
        Assert.IsTrue(SearchSubmitPolicy.ShouldSeedEnterToSearch(SearchSubmitMode.Auto, totalRows: 5_000_000, indexBuilt: false));
    }

    [TestMethod]
    public void Seed_LargeLog_Indexed_StartsLive()
    {
        // With the FTS index built, even a 5M lookup is ~2ms — live is smooth, so don't force Enter.
        Assert.IsFalse(SearchSubmitPolicy.ShouldSeedEnterToSearch(SearchSubmitMode.Auto, totalRows: 5_000_000, indexBuilt: true));
    }

    [TestMethod]
    public void Seed_AtThreshold_IsBoundaryInclusive()
    {
        Assert.IsFalse(SearchSubmitPolicy.ShouldSeedEnterToSearch(SearchSubmitMode.Auto, SearchSubmitPolicy.LargeRowSeed - 1, indexBuilt: false));
        Assert.IsTrue(SearchSubmitPolicy.ShouldSeedEnterToSearch(SearchSubmitMode.Auto, SearchSubmitPolicy.LargeRowSeed, indexBuilt: false));
    }

    [TestMethod]
    public void Seed_NonAutoModes_NeverSeed()
    {
        // Explicit modes ignore the seed (the user chose; RequireEnter handles them directly).
        Assert.IsFalse(SearchSubmitPolicy.ShouldSeedEnterToSearch(SearchSubmitMode.Live, 5_000_000, indexBuilt: false));
        Assert.IsFalse(SearchSubmitPolicy.ShouldSeedEnterToSearch(SearchSubmitMode.OnEnter, 5_000_000, indexBuilt: false));
    }

    // ── NextAutoEnterState: adaptive transition after a timed search ────────────

    [TestMethod]
    public void Transition_SlowSearch_SwitchesToEnter()
    {
        Assert.IsTrue(SearchSubmitPolicy.NextAutoEnterState(current: false, lastSearchMs: SearchSubmitPolicy.SlowSearchMs + 1));
        Assert.IsTrue(SearchSubmitPolicy.NextAutoEnterState(current: false, lastSearchMs: 2_300)); // measured 5M LIKE
    }

    [TestMethod]
    public void Transition_FastSearch_SwitchesBackToLive()
    {
        // e.g. once the FTS index finishes building, searches drop to ~2ms → return to live typing.
        Assert.IsFalse(SearchSubmitPolicy.NextAutoEnterState(current: true, lastSearchMs: SearchSubmitPolicy.FastSearchMs - 1));
        Assert.IsFalse(SearchSubmitPolicy.NextAutoEnterState(current: true, lastSearchMs: 2));
    }

    [TestMethod]
    public void Transition_MidRange_KeepsCurrentState_NoFlapping()
    {
        long mid = (SearchSubmitPolicy.FastSearchMs + SearchSubmitPolicy.SlowSearchMs) / 2;
        Assert.IsTrue(SearchSubmitPolicy.NextAutoEnterState(current: true, lastSearchMs: mid),
            "mid-latency should not yank an Enter-mode user back to live");
        Assert.IsFalse(SearchSubmitPolicy.NextAutoEnterState(current: false, lastSearchMs: mid),
            "mid-latency should not force a live user into Enter mode");
    }

    // ── End-to-end behavioral scenarios ────────────────────────────────────────

    [TestMethod]
    public void Scenario_SmallLog_TypingStaysLiveThroughout()
    {
        // Load a small log: live from the start, and fast searches keep it live every keystroke.
        bool autoEnter = SearchSubmitPolicy.ShouldSeedEnterToSearch(SearchSubmitMode.Auto, 250_000, indexBuilt: false);
        Assert.IsFalse(autoEnter);
        foreach (var keystrokeMs in new long[] { 90, 100, 110, 95 }) // ~measured 250k latency
        {
            Assert.IsFalse(SearchSubmitPolicy.RequireEnter(SearchSubmitMode.Auto, autoEnter), "should keep searching live");
            autoEnter = SearchSubmitPolicy.NextAutoEnterState(autoEnter, keystrokeMs);
        }
        Assert.IsFalse(autoEnter, "small log never needs Enter-to-search");
    }

    [TestMethod]
    public void Scenario_HugeLog_RequiresEnterFromTheStart_NoLaggyKeystrokes()
    {
        // Load a 5M log (no index): seeded into Enter mode, so typing never triggers a live scan.
        bool autoEnter = SearchSubmitPolicy.ShouldSeedEnterToSearch(SearchSubmitMode.Auto, 5_000_000, indexBuilt: false);
        Assert.IsTrue(autoEnter);
        // Typing 'error' — every keystroke is gated behind Enter, so none pays the ~2.3s scan.
        Assert.IsTrue(SearchSubmitPolicy.RequireEnter(SearchSubmitMode.Auto, autoEnter));
    }

    [TestMethod]
    public void Scenario_HugeLog_BecomesLiveAfterIndexBuilds()
    {
        // Start in Enter mode (no index). The user presses Enter; once the index is built the search
        // is ~2ms, which flips the experience back to smooth live typing.
        bool autoEnter = SearchSubmitPolicy.ShouldSeedEnterToSearch(SearchSubmitMode.Auto, 5_000_000, indexBuilt: false);
        Assert.IsTrue(autoEnter);
        autoEnter = SearchSubmitPolicy.NextAutoEnterState(autoEnter, lastSearchMs: 2); // FTS-fast
        Assert.IsFalse(autoEnter);
        Assert.IsFalse(SearchSubmitPolicy.RequireEnter(SearchSubmitMode.Auto, autoEnter), "now live again");
    }
}
