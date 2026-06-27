using Microsoft.VisualStudio.TestTools.UnitTesting;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace FindNeedleUX.UITests
{
    /// <summary>
    /// End-to-end search experience on a multi-million-row log: load it, type a term for one
    /// particular line, press Enter, and verify (a) the matching row shows up and (b) the window
    /// stays responsive while the search runs (a live UI-thread round-trip, probed during the search,
    /// must stay tiny — a synchronous search would block it for the whole scan).
    ///
    /// Two variants, each emitting a per-phase timing breakdown + a machine-readable SUMMARY line so
    /// the numbers can be tracked over time:
    ///   • lazy  — default; the first search is the slow un-indexed LIKE scan (the freeze risk).
    ///   • eager — the FTS index is built during load, so the search itself is fast (load pays for it).
    /// Manual lane (interactive desktop; heavy — generates a ~240 MB log).
    /// </summary>
    [TestClass]
    [TestCategory("UITests")]
    [TestCategory("SkipCI")]
    [TestCategory("Performance")]
    public class SearchResponsivenessUITests
    {
        private const int RowCount = 5_000_000;
        private const int UniqueLine = 1_234_567;          // only "number 1234567" matches this row
        private const string SearchTerm = "number 1234567";

        // A synchronous LIKE scan of 5M rows takes ~2s+; a responsive (off-thread) UI answers a
        // round-trip in well under this. Wide enough to be a stable threshold.
        private const long ResponsiveBudgetMs = 1500;
        // With the index built, the search itself should be quick (a few hundred ms incl. UI polling).
        private const long FastSearchBudgetMs = 4000;
        // Probe responsiveness for at least this long after Enter so the max is a stable sample.
        private const int MinProbeMs = 3000;

        private static string _logPath;
        private static long _genMs;
        private static UIA3Automation _automation;
        // Each scenario's headline numbers, accumulated across the class's tests and written into a
        // machine-anchored performance report at ClassCleanup.
        private static readonly System.Collections.Generic.List<(string label, string value)> _benchmarks = new();

        public TestContext TestContext { get; set; }

        [ClassInitialize]
        public static void Setup(TestContext context)
        {
            _automation = new UIA3Automation();
            var sw = Stopwatch.StartNew();
            _logPath = UiTestHelpers.WriteBracketedLog(RowCount, "findneedle_resp");
            sw.Stop();
            _genMs = sw.ElapsedMilliseconds;
            context.WriteLine($"Generated {RowCount:N0}-line log in {_genMs} ms: {_logPath}");
        }

        [ClassCleanup]
        public static void TearDown()
        {
            // Write a machine-anchored performance report from the benchmark numbers this class measured
            // (plus the app's persisted search-pipeline run, slow UX interactions, and cache footprint).
            try
            {
                if (_benchmarks.Count > 0)
                {
                    var path = FindPluginCore.Diagnostics.PerformanceReport.Save(benchmarks: _benchmarks);
                    System.Console.WriteLine($"Performance report written: {path}");
                }
            }
            catch { /* reporting must never fail the run */ }

            try { _automation?.Dispose(); } catch { }
            try { if (_logPath != null && File.Exists(_logPath)) File.Delete(_logPath); } catch { }
        }

        private readonly record struct Timings(long LaunchMs, long LoadMs, long SearchMs, long MaxProbeMs, int Filtered);

        /// <summary>Lazy (default): first search is the slow un-indexed scan — must stay responsive.</summary>
        [TestMethod]
        [Timeout(600_000)]
        public void Search_LazyUnindexed_StaysResponsive()
        {
            // Force lazy explicitly: the shipped default is Background, so an empty flag would build
            // the index automatically and this would no longer exercise the un-indexed scan path.
            var t = RunScenario(indexingArgs: "--indexing=lazy");
            Report("lazy", t);

            Assert.AreEqual(1, t.Filtered, $"search for '{SearchTerm}' should match exactly line {UniqueLine:N0}");
            Assert.IsTrue(t.SearchMs >= 0, "search result never appeared");
            Assert.IsTrue(t.MaxProbeMs < ResponsiveBudgetMs,
                $"UI blocked for {t.MaxProbeMs} ms during search (budget {ResponsiveBudgetMs}) — search is not off the UI thread");
        }

        /// <summary>Eager: index built during load, so the search itself is fast.</summary>
        [TestMethod]
        [Timeout(600_000)]
        public void Search_EagerIndex_IsFast()
        {
            var t = RunScenario(indexingArgs: "--indexing=eager");
            Report("eager", t);

            Assert.AreEqual(1, t.Filtered, $"search for '{SearchTerm}' should match exactly line {UniqueLine:N0}");
            Assert.IsTrue(t.SearchMs >= 0, "search result never appeared");
            Assert.IsTrue(t.MaxProbeMs < ResponsiveBudgetMs,
                $"UI blocked for {t.MaxProbeMs} ms during search (budget {ResponsiveBudgetMs})");
            Assert.IsTrue(t.SearchMs < FastSearchBudgetMs,
                $"with the index built the search should be fast, was {t.SearchMs} ms (budget {FastSearchBudgetMs})");
        }

        private Timings RunScenario(string indexingArgs)
        {
            var args = $"\"{_logPath}\" --viewer=native --storage=sqlite --cache=off {indexingArgs}".Trim();
            var psi = new ProcessStartInfo(UiTestHelpers.GetAppExecutablePath())
            {
                Arguments = args,
                UseShellExecute = false,
            };

            var launchSw = Stopwatch.StartNew();
            var app = Application.Launch(psi);
            try
            {
                Thread.Sleep(3000);
                var window = app.GetMainWindow(_automation);
                launchSw.Stop();
                Assert.IsNotNull(window, "Failed to get main window");
                // Maximize so the toolbar/pager don't overflow at a narrow persisted window width.
                try { window.Patterns.Window.PatternOrDefault?.SetWindowVisualState(WindowVisualState.Maximized); } catch { }

                var loadSw = Stopwatch.StartNew();
                var grid = UiTestHelpers.WaitForPopulatedGrid(window, 480_000);
                loadSw.Stop();
                Assert.IsNotNull(grid, "grid never populated");

                int total = ReadFilteredCount(window, 30_000);
                Assert.IsTrue(total >= RowCount - 5, $"expected ~{RowCount:N0} rows loaded, saw {total:N0}");

                var searchBox = UiTestHelpers.FindByIdSkippingGrid(window, "SearchBox", 15_000);
                Assert.IsNotNull(searchBox, "SearchBox not found");

                searchBox.Focus();                       // also kicks off the background index build (lazy mode)
                Keyboard.Type(SearchTerm);
                Keyboard.Type(VirtualKeyShort.RETURN);

                var (searchMs, maxProbeMs, filtered) =
                    AwaitResultWhileProbing(window, searchBox, expected: 1, timeoutMs: 90_000);

                return new Timings(launchSw.ElapsedMilliseconds, loadSw.ElapsedMilliseconds, searchMs, maxProbeMs, filtered);
            }
            finally
            {
                try { if (!app.HasExited) app.Close(); } catch { }
                try { if (!app.HasExited) app.Kill(); } catch { }
                try { app.Dispose(); } catch { }
                Thread.Sleep(1000);
            }
        }

        private void Report(string mode, Timings t)
        {
            TestContext.WriteLine($"=== TIMINGS [{mode}] ({RowCount:N0} rows) ===");
            TestContext.WriteLine($"  log generation               : {_genMs,7:N0} ms");
            TestContext.WriteLine($"  app launch (→ main window)    : {t.LaunchMs,7:N0} ms");
            TestContext.WriteLine($"  load (→ populated grid)       : {t.LoadMs,7:N0} ms");
            TestContext.WriteLine($"  search (Enter → 1 row shown)  : {t.SearchMs,7:N0} ms");
            TestContext.WriteLine($"  max UI round-trip in search   : {t.MaxProbeMs,7:N0} ms  (budget {ResponsiveBudgetMs})");
            TestContext.WriteLine(
                $"SUMMARY mode={mode} rows={RowCount} genMs={_genMs} launchMs={t.LaunchMs} " +
                $"loadMs={t.LoadMs} searchMs={t.SearchMs} maxProbeMs={t.MaxProbeMs}");

            _benchmarks.Add(($"{mode} · load → populated grid ({RowCount:N0} rows)", $"{t.LoadMs:N0} ms"));
            _benchmarks.Add(($"{mode} · search (Enter → 1 row)", $"{t.SearchMs:N0} ms"));
            _benchmarks.Add(($"{mode} · max UI round-trip during search", $"{t.MaxProbeMs:N0} ms (budget {ResponsiveBudgetMs})"));
        }

        /// <summary>
        /// Poll for the filtered count to reach <paramref name="expected"/> while, on every poll,
        /// timing a live property read that round-trips to the app's UI thread. Returns time-until-
        /// result (-1 if never), the slowest single round-trip seen, and the last filtered count.
        /// Keeps probing for at least <see cref="MinProbeMs"/> so the max is a stable sample.
        /// </summary>
        private (long searchMs, long maxProbeMs, int filtered) AwaitResultWhileProbing(
            AutomationElement window, AutomationElement searchBox, int expected, int timeoutMs)
        {
            var clock = Stopwatch.StartNew();
            long maxProbe = 0, searchMs = -1;
            int last = -1;
            var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < deadline)
            {
                var probe = Stopwatch.StartNew();
                try { _ = searchBox.Patterns.Value.Pattern.Value.Value; } catch { /* transient */ }
                probe.Stop();
                if (probe.ElapsedMilliseconds > maxProbe) maxProbe = probe.ElapsedMilliseconds;

                if (searchMs < 0)
                {
                    last = ReadFilteredCountOnce(window);
                    if (last == expected) searchMs = clock.ElapsedMilliseconds;
                }

                if (searchMs >= 0 && clock.ElapsedMilliseconds >= MinProbeMs) break;
                Thread.Sleep(50);
            }
            if (searchMs >= 0) last = expected;
            return (searchMs, maxProbe, last);
        }

        private static int ReadFilteredCount(AutomationElement window, int timeoutMs)
        {
            var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < deadline)
            {
                var n = ReadFilteredCountOnce(window);
                if (n >= 0) return n;
                Thread.Sleep(300);
            }
            return -1;
        }

        /// <summary>One pass: parse the "{filtered} / {total} results" status text's filtered count.</summary>
        private static int ReadFilteredCountOnce(AutomationElement window)
        {
            // Read the row total from the PAGER, not the "{filtered} / {total} results" status text: that
            // status TextBlock sits in a toolbar that overflows into a "More" menu on a narrow window, so
            // it isn't reliably in the visible UIA tree (window width persists across launches). The pager
            // is always present and ends with the filtered total ("… of {N}"). Targeted find by id, then
            // take the last "of N" (the first is the page count). A TextBlock's UIA Name is its full text.
            AutomationElement pager = null;
            UiTestHelpers.WalkSkippingGrid(window, e =>
            {
                if (UiTestHelpers.SafeAutomationId(e) == "PagerStatus") { pager = e; return true; }
                return false;
            });
            var raw = pager == null ? null : UiTestHelpers.SafeName(pager);
            if (string.IsNullOrEmpty(raw)) return -1;
            var ms = Regex.Matches(raw, @"of\s+([\d,]+)");
            if (ms.Count == 0) return -1;
            return int.TryParse(ms[ms.Count - 1].Groups[1].Value.Replace(",", ""), out var n) ? n : -1;
        }
    }
}
