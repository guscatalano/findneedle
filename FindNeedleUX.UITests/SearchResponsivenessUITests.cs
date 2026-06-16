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
    /// particular line, press Enter, and verify (a) the matching row actually shows up and (b) the
    /// window stays responsive while the search runs. The first search is un-indexed (lazy indexing
    /// default), so it's the slow LIKE scan (~2s on 5M) — exactly the case that froze the UI before
    /// search moved off the UI thread. The responsiveness probe is a live UI-thread round-trip taken
    /// repeatedly during the search; a synchronous search would block it for the full scan.
    ///
    /// Reports a per-phase timing breakdown (and a single machine-readable SUMMARY line) so the
    /// numbers can be tracked over time. Manual lane (interactive desktop; heavy).
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

        // A synchronous LIKE scan of 5M rows takes ~2s; a responsive (off-thread) UI answers a
        // round-trip in well under this. The gap is wide enough to be a stable threshold.
        private const long ResponsiveBudgetMs = 1500;

        // Keep probing for at least this long after Enter so the responsiveness max is a stable sample
        // even if the search finishes quickly.
        private const int MinProbeMs = 3000;

        private static string _logPath;
        private static long _genMs;
        private static UIA3Automation _automation;

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
            try { _automation?.Dispose(); } catch { }
            try { if (_logPath != null && File.Exists(_logPath)) File.Delete(_logPath); } catch { }
        }

        [TestMethod]
        [Timeout(600_000)]
        public void Search_OnHugeLog_FindsLine_WithoutFreezing()
        {
            // Lazy indexing (the default) means the first search is the slow LIKE scan — the freeze risk.
            var psi = new ProcessStartInfo(UiTestHelpers.GetAppExecutablePath())
            {
                Arguments = $"\"{_logPath}\" --viewer=native --storage=sqlite --cache=off",
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

                var loadSw = Stopwatch.StartNew();
                var grid = UiTestHelpers.WaitForPopulatedGrid(window, 420_000);
                loadSw.Stop();
                Assert.IsNotNull(grid, "grid never populated");

                int total = ReadFilteredCount(window, 30_000);
                Assert.IsTrue(total >= RowCount - 5, $"expected ~{RowCount:N0} rows loaded, saw {total:N0}");

                var searchBox = UiTestHelpers.FindByIdSkippingGrid(window, "SearchBox", 15_000);
                Assert.IsNotNull(searchBox, "SearchBox not found");

                // Type the term, then press Enter to commit the search.
                searchBox.Focus();
                Keyboard.Type(SearchTerm);
                Keyboard.Type(VirtualKeyShort.RETURN);

                // From here: measure how long until the one row appears, while probing UI
                // responsiveness on every poll (a live read that round-trips to the UI thread).
                var (searchMs, maxProbeMs, filtered) =
                    AwaitResultWhileProbing(window, searchBox, expected: 1, timeoutMs: 90_000);

                // ----- per-phase timing breakdown -----
                TestContext.WriteLine($"=== TIMINGS ({RowCount:N0} rows) ===");
                TestContext.WriteLine($"  log generation               : {_genMs,7:N0} ms");
                TestContext.WriteLine($"  app launch (→ main window)    : {launchSw.ElapsedMilliseconds,7:N0} ms");
                TestContext.WriteLine($"  load (→ populated grid)       : {loadSw.ElapsedMilliseconds,7:N0} ms");
                TestContext.WriteLine($"  search (Enter → 1 row shown)  : {searchMs,7:N0} ms");
                TestContext.WriteLine($"  max UI round-trip in search   : {maxProbeMs,7:N0} ms  (budget {ResponsiveBudgetMs})");
                TestContext.WriteLine(
                    $"SUMMARY rows={RowCount} genMs={_genMs} launchMs={launchSw.ElapsedMilliseconds} " +
                    $"loadMs={loadSw.ElapsedMilliseconds} searchMs={searchMs} maxProbeMs={maxProbeMs}");

                Assert.AreEqual(1, filtered, $"search for '{SearchTerm}' should match exactly the one line {UniqueLine:N0}");
                Assert.IsTrue(searchMs >= 0, "search result never appeared");
                Assert.IsTrue(maxProbeMs < ResponsiveBudgetMs,
                    $"UI blocked for {maxProbeMs} ms during search (budget {ResponsiveBudgetMs} ms) — search is not off the UI thread");
            }
            finally
            {
                try { if (!app.HasExited) app.Close(); } catch { }
                try { if (!app.HasExited) app.Kill(); } catch { }
                try { app.Dispose(); } catch { }
                Thread.Sleep(1000);
            }
        }

        /// <summary>
        /// Poll for the filtered count to reach <paramref name="expected"/> while, on every poll,
        /// timing a live (non-cached) property read that round-trips to the app's UI thread. Returns
        /// the time until the result first appeared (searchMs, -1 if never), the slowest single
        /// round-trip seen during the search (maxProbeMs), and the last filtered count observed.
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

                // Stop once we have the result AND we've probed long enough for a stable max.
                if (searchMs >= 0 && clock.ElapsedMilliseconds >= MinProbeMs) break;
                Thread.Sleep(50);
            }
            if (searchMs >= 0) last = expected;
            return (searchMs, maxProbe, last);
        }

        /// <summary>Retry wrapper around <see cref="ReadFilteredCountOnce"/>.</summary>
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
            var raw = UiTestHelpers.FindAllSkippingGrid(window, ControlType.Text)
                          .Select(UiTestHelpers.SafeName)
                          .FirstOrDefault(n => Regex.IsMatch(n ?? "", @"^[\d,]+\s*/\s*[\d,]+\s*results"));
            if (raw == null) return -1;
            var m = Regex.Match(raw, @"^([\d,]+)\s*/");
            return m.Success && int.TryParse(m.Groups[1].Value.Replace(",", ""), out var n) ? n : -1;
        }
    }
}
