using Microsoft.VisualStudio.TestTools.UnitTesting;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace FindNeedleUX.UITests
{
    /// <summary>
    /// End-to-end guard for "un-searching resets the viewer": load a log, search a term whose only
    /// match sits several pages in, then clear the search — the viewer must stay on the page that
    /// contains the match (so the user can read the lines around it), not reset to page 1. Drives the
    /// real search box + pager through UI Automation; the ViewModel-level variants live in
    /// FindNeedleUXTests (UnsearchKeepsPositionTests).
    ///
    /// Manual lane (UITests + SkipCI): needs an interactive desktop and the built .exe.
    /// </summary>
    [TestClass]
    [TestCategory("UITests")]
    [TestCategory("SkipCI")]
    public class UnsearchKeepsPositionUITests
    {
        private const int RowCount = 1000;
        private const int MatchLine = 450;                  // 0-based → the 451st row in load order
        private const string SearchTerm = "number 450";     // matches exactly one of the 1000 lines

        public TestContext TestContext { get; set; }

        [TestMethod]
        [Timeout(300_000)]
        public void ClearingSearch_StaysOnThePageContainingTheMatch()
        {
            var logPath = UiTestHelpers.WriteBracketedLog(RowCount, "findneedle_unsearch");
            UIA3Automation automation = null;
            Application app = null;
            try
            {
                automation = new UIA3Automation();
                // SQLite storage = the backend a genuinely large file would use (exercises the SQL
                // row-position path); cache=off so every run scans fresh.
                app = Application.Launch(UiTestHelpers.GetAppExecutablePath(),
                    $"\"{logPath}\" --viewer=native --storage=sqlite --cache=off");
                Thread.Sleep(3000);
                var window = app.GetMainWindow(automation);
                Assert.IsNotNull(window, "Failed to get main window");
                try { window.Patterns.Window.PatternOrDefault?.SetWindowVisualState(WindowVisualState.Maximized); } catch { }

                Assert.IsNotNull(UiTestHelpers.WaitForPopulatedGrid(window, 120_000), "grid never populated");
                var loaded = WaitForPager(window, p => p.total == RowCount, 60_000);
                Assert.AreEqual(RowCount, loaded.total, $"expected all {RowCount} rows loaded (pager: '{loaded.raw}')");

                // Derive the expected page from the observed page size (persisted setting — don't assume 100).
                int pageSize = (int)(loaded.end - loaded.start + 1);
                Assert.IsTrue(pageSize > 0, $"couldn't read the page size from the pager ('{loaded.raw}')");
                int expectedPage = (MatchLine + 1 + pageSize - 1) / pageSize;

                var searchBox = UiTestHelpers.FindByIdSkippingGrid(window, "SearchBox", 15_000);
                Assert.IsNotNull(searchBox, "SearchBox not found");

                // Search for the word: exactly one row matches.
                searchBox.Focus();
                Keyboard.Type(SearchTerm);
                Keyboard.Type(VirtualKeyShort.RETURN);
                var filtered = WaitForPager(window, p => p.total == 1, 60_000);
                Assert.AreEqual(1, filtered.total, $"search should match exactly one row (pager: '{filtered.raw}')");

                // "Unsearch": clear the box and commit, the way a user does.
                searchBox.Focus();
                Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
                Keyboard.Type(VirtualKeyShort.DELETE);
                Keyboard.Type(VirtualKeyShort.RETURN);
                var restored = WaitForPager(window, p => p.total == RowCount, 60_000);
                Assert.AreEqual(RowCount, restored.total, $"clearing the search must restore all rows (pager: '{restored.raw}')");

                TestContext.WriteLine($"pager after unsearch: '{restored.raw}' (pageSize={pageSize}, expected page {expectedPage})");
                Assert.AreEqual(expectedPage, restored.page,
                    $"after clearing the search the viewer must stay on the match's page (pager: '{restored.raw}')");
                Assert.IsTrue(restored.start <= MatchLine + 1 && MatchLine + 1 <= restored.end,
                    $"the visible row range must include the match (row {MatchLine + 1}; pager: '{restored.raw}')");
            }
            finally
            {
                try { app?.Close(); } catch { }
                // Close() may not terminate the process and Dispose() never does — Kill so it can't linger.
                try { if (app != null && !app.HasExited) app.Kill(); } catch { }
                try { app?.Dispose(); } catch { }
                try { automation?.Dispose(); } catch { }
                try { if (File.Exists(logPath)) File.Delete(logPath); } catch { }
                Thread.Sleep(800);
            }
        }

        private readonly record struct Pager(int page, int pages, long start, long end, int total, string raw);

        /// <summary>Poll the PagerStatus text ("Page X of Y · a–b of N") until <paramref name="ready"/>
        /// accepts it (or timeout — the last parse is returned either way).</summary>
        private static Pager WaitForPager(AutomationElement window, Func<Pager, bool> ready, int timeoutMs)
        {
            Pager last = default;
            var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < deadline)
            {
                last = ReadPagerOnce(window);
                if (last.total >= 0 && ready(last)) return last;
                Thread.Sleep(300);
            }
            return last;
        }

        private static Pager ReadPagerOnce(AutomationElement window)
        {
            AutomationElement pager = null;
            UiTestHelpers.WalkSkippingGrid(window, e =>
            {
                if (UiTestHelpers.SafeAutomationId(e) == "PagerStatus") { pager = e; return true; }
                return false;
            });
            var raw = pager == null ? "" : UiTestHelpers.SafeName(pager);
            if (string.IsNullOrEmpty(raw)) return new Pager(-1, -1, -1, -1, -1, raw);

            int page = -1, pages = -1, total = -1;
            long start = -1, end = -1;
            var pm = Regex.Match(raw, @"Page\s+([\d,]+)\s+of\s+([\d,]+)");
            if (pm.Success)
            {
                int.TryParse(pm.Groups[1].Value.Replace(",", ""), out page);
                int.TryParse(pm.Groups[2].Value.Replace(",", ""), out pages);
            }
            var rm = Regex.Match(raw, @"([\d,]+)\s*[–-]\s*([\d,]+)");
            if (rm.Success)
            {
                long.TryParse(rm.Groups[1].Value.Replace(",", ""), out start);
                long.TryParse(rm.Groups[2].Value.Replace(",", ""), out end);
            }
            // The row total is the LAST "of N" (the first is the page count).
            var tms = Regex.Matches(raw, @"of\s+([\d,]+)");
            if (tms.Count > 0)
                int.TryParse(tms[tms.Count - 1].Groups[1].Value.Replace(",", ""), out total);
            // A single-row result renders "0" total pages edge cases as-is; callers match on total.
            return new Pager(page, pages, start, end, total, raw);
        }
    }
}
