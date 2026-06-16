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
    /// Manual lane (interactive desktop; heavy).
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

        private static string _logPath;
        private static UIA3Automation _automation;

        public TestContext TestContext { get; set; }

        [ClassInitialize]
        public static void Setup(TestContext context)
        {
            _automation = new UIA3Automation();
            _logPath = UiTestHelpers.WriteBracketedLog(RowCount, "findneedle_resp");
            context.WriteLine($"Generated {RowCount:N0}-line log: {_logPath}");
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
            var app = Application.Launch(psi);
            try
            {
                Thread.Sleep(3000);
                var window = app.GetMainWindow(_automation);
                Assert.IsNotNull(window, "Failed to get main window");

                var grid = UiTestHelpers.WaitForPopulatedGrid(window, 420_000);
                Assert.IsNotNull(grid, "grid never populated");

                int total = ReadFilteredCount(window, 30_000);
                TestContext.WriteLine($"Loaded; status shows {total:N0} rows");
                Assert.IsTrue(total >= RowCount - 5, $"expected ~{RowCount:N0} rows loaded, saw {total:N0}");

                var searchBox = UiTestHelpers.FindByIdSkippingGrid(window, "SearchBox", 15_000);
                Assert.IsNotNull(searchBox, "SearchBox not found");

                // Type the term, then press Enter to commit the search.
                searchBox.Focus();
                Keyboard.Type(SearchTerm);
                Keyboard.Type(VirtualKeyShort.RETURN);

                // Probe responsiveness WHILE the search runs: a live property read round-trips to the
                // UI thread, so if the search were synchronous this would block for the whole scan.
                long maxProbeMs = ProbeResponsivenessFor(searchBox, durationMs: 3000);
                TestContext.WriteLine($"Max UI round-trip during search: {maxProbeMs} ms (budget {ResponsiveBudgetMs} ms)");

                // The one matching line must show up.
                int filtered = WaitForFilteredCount(window, expected: 1, timeoutMs: 60_000);
                TestContext.WriteLine($"Filtered result count after search: {filtered}");

                Assert.AreEqual(1, filtered, $"search for '{SearchTerm}' should match exactly the one line {UniqueLine:N0}");
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
        /// Repeatedly take a live (non-cached) property read off the given element for the duration and
        /// return the slowest single round-trip. Each read is served by the app's UI thread, so a busy
        /// (frozen) UI thread shows up as a large max.
        /// </summary>
        private static long ProbeResponsivenessFor(AutomationElement element, int durationMs)
        {
            long max = 0;
            var deadline = DateTime.Now.AddMilliseconds(durationMs);
            while (DateTime.Now < deadline)
            {
                var sw = Stopwatch.StartNew();
                try { _ = element.Patterns.Value.Pattern.Value.Value; } catch { /* transient; ignore */ }
                sw.Stop();
                if (sw.ElapsedMilliseconds > max) max = sw.ElapsedMilliseconds;
                Thread.Sleep(50);
            }
            return max;
        }

        private int WaitForFilteredCount(AutomationElement window, int expected, int timeoutMs)
        {
            var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            int last = -1;
            while (DateTime.Now < deadline)
            {
                last = ReadFilteredCount(window, 2000);
                if (last == expected) return last;
                Thread.Sleep(500);
            }
            return last;
        }

        /// <summary>Parse the "{filtered} / {total} results" status text; returns the filtered count.</summary>
        private static int ReadFilteredCount(AutomationElement window, int timeoutMs)
        {
            var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < deadline)
            {
                var raw = UiTestHelpers.FindAllSkippingGrid(window, ControlType.Text)
                              .Select(UiTestHelpers.SafeName)
                              .FirstOrDefault(n => Regex.IsMatch(n ?? "", @"^[\d,]+\s*/\s*[\d,]+\s*results"));
                if (raw != null)
                {
                    var m = Regex.Match(raw, @"^([\d,]+)\s*/");
                    if (m.Success && int.TryParse(m.Groups[1].Value.Replace(",", ""), out var n)) return n;
                }
                Thread.Sleep(300);
            }
            return -1;
        }
    }
}
