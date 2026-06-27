using Microsoft.VisualStudio.TestTools.UnitTesting;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace FindNeedleUX.UITests
{
    /// <summary>
    /// Large-scale end-to-end UI test: load FIVE MILLION rows into the real native viewer and prove
    /// the app can (a) pixel-scroll a page with the WinUI DataGrid and (b) paginate to the very end
    /// at every page size the viewer offers. At 5M rows the search picks the SQLite storage tier and
    /// the viewer only ever materializes one page (max 5,000 rows) via IPagedLogSource — the rest
    /// stays on disk — so this exercises the paging path, not an in-memory blowup.
    ///
    /// IMPORTANT UI-Automation constraint: a CommunityToolkit DataGrid bound to thousands of rows
    /// exposes thousands of automation peers, and ANY window-wide UIA query (FindAllDescendants) —
    /// or asking the grid for a cell — times out or hangs the UI thread. So this test never walks
    /// the grid's subtree: it reaches the pagination bar by walking children only (pruning the grid
    /// node) and reads the deterministic pager text ("1–50 of 5000000") to verify pagination, and it
    /// scrolls via the grid's O(1) scroll pattern. No per-row introspection anywhere.
    ///
    /// Manual lane only (UITests + SkipCI + Performance): needs an interactive desktop, generates a
    /// ~300 MB log, and runs a full 5M-row search. Leave the desktop alone while it runs.
    /// </summary>
    [TestClass]
    [TestCategory("UITests")]
    [TestCategory("SkipCI")]
    [TestCategory("Performance")]
    public class NativeGridLargeScaleUITests
    {
        private static Application _app;
        private static Window _mainWindow;
        private static UIA3Automation _automation;
        private static string _tempLogPath;

        private const int RowCount = 5_000_000;

        // Every option offered by the viewer's page-size selector (NativeResultsPage.xaml).
        private static readonly int[] PageSizes = { 50, 100, 250, 500, 1000, 5000 };

        public TestContext TestContext { get; set; }

        private static string GetAppExecutablePath()
        {
            var testDir = AppContext.BaseDirectory;
            var solutionDir = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", ".."));
            string[] possiblePaths =
            {
                Path.Combine(solutionDir, "FindNeedleUX", "bin", "Debug", "net8.0-windows10.0.19041.0", "win-x64", "FindNeedleUX.exe"),
                Path.Combine(solutionDir, "FindNeedleUX", "bin", "Release", "net8.0-windows10.0.19041.0", "win-x64", "FindNeedleUX.exe"),
                Path.Combine(solutionDir, "FindNeedleUX", "bin", "Debug", "net8.0-windows10.0.19041.0", "FindNeedleUX.exe"),
                Path.Combine(solutionDir, "FindNeedleUX", "bin", "Release", "net8.0-windows10.0.19041.0", "FindNeedleUX.exe"),
            };
            string newestPath = null;
            DateTime newestTime = DateTime.MinValue;
            foreach (var path in possiblePaths)
                if (File.Exists(path) && File.GetLastWriteTime(path) > newestTime)
                { newestTime = File.GetLastWriteTime(path); newestPath = path; }
            if (newestPath != null) return newestPath;
            throw new FileNotFoundException($"Could not find FindNeedleUX.exe. Searched: {string.Join(", ", possiblePaths)}");
        }

        private static string WriteTempLog(int lines)
        {
            var path = Path.Combine(Path.GetTempPath(), $"findneedle_5m_{Guid.NewGuid():N}.log");
            var start = new DateTime(2026, 1, 1, 0, 0, 0);
            using var sw = new StreamWriter(path, append: false, System.Text.Encoding.ASCII, 1 << 20);
            for (int i = 0; i < lines; i++)
            {
                var t = start.AddSeconds(i);
                sw.Write('['); sw.Write(t.ToString("yyyy-MM-dd HH:mm:ss"));
                sw.Write("] INFO: scroll test message line number "); sw.Write(i); sw.Write('\n');
            }
            return path;
        }

        [ClassInitialize]
        public static void Setup(TestContext context)
        {
            try
            {
                var genSw = Stopwatch.StartNew();
                _tempLogPath = WriteTempLog(RowCount);
                genSw.Stop();
                context.WriteLine($"Generated {RowCount:N0}-line log ({new FileInfo(_tempLogPath).Length / (1024 * 1024)} MB) in {genSw.Elapsed.TotalSeconds:F1}s");

                _automation = new UIA3Automation();
                var appPath = GetAppExecutablePath();
                _app = Application.Launch(appPath, $"\"{_tempLogPath}\" --viewer=native");
                Thread.Sleep(3000);
                _mainWindow = _app.GetMainWindow(_automation);
                Assert.IsNotNull(_mainWindow, "Failed to get main window");
            }
            catch (Exception ex)
            {
                Assert.Inconclusive($"Could not launch the 5M-row scenario (needs an interactive desktop). {ex.Message}");
            }
        }

        [ClassCleanup]
        public static void TearDown()
        {
            try { _mainWindow?.Close(); } catch { }
            try { _app?.Dispose(); } catch { }
            try { _automation?.Dispose(); } catch { }
            try { if (_tempLogPath != null && File.Exists(_tempLogPath)) File.Delete(_tempLogPath); } catch { }
        }

        // ---- UIA helpers that never descend into the (huge) ResultsGrid subtree ----

        private static string SafeName(AutomationElement e)
        { try { return e.Properties.Name.ValueOrDefault ?? ""; } catch { return ""; } }

        private static string SafeAutomationId(AutomationElement e)
        { try { return e.Properties.AutomationId.ValueOrDefault ?? ""; } catch { return ""; } }

        private static ControlType SafeControlType(AutomationElement e)
        { try { return e.Properties.ControlType.ValueOrDefault; } catch { return ControlType.Unknown; } }

        /// <summary>
        /// Walk the window CHILDREN-first, pruning the ResultsGrid node so we never enumerate its
        /// thousands of row peers. Invokes <paramref name="visit"/> on every element reached; visit
        /// returns true to stop early. FindAllChildren at each node is cheap (a handful of elements).
        /// </summary>
        private void WalkSkippingGrid(Func<AutomationElement, bool> visit)
        {
            var queue = new Queue<AutomationElement>();
            queue.Enqueue(_mainWindow);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                AutomationElement[] children;
                try { children = node.FindAllChildren(); } catch { continue; }
                foreach (var child in children)
                {
                    if (visit(child)) return;
                    if (SafeAutomationId(child) == "ResultsGrid") continue; // never walk the 5,000-row grid
                    queue.Enqueue(child);
                }
            }
        }

        /// <summary>Find an element by AutomationId without descending into the grid (with retry).</summary>
        private AutomationElement FindByIdSkippingGrid(string automationId, int timeoutMs = 15000)
        {
            var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < deadline)
            {
                AutomationElement found = null;
                WalkSkippingGrid(e => { if (SafeAutomationId(e) == automationId) { found = e; return true; } return false; });
                if (found != null) return found;
                Thread.Sleep(300);
            }
            return null;
        }

        /// <summary>Collect every element of a control type, never walking the grid's row subtree.</summary>
        private List<AutomationElement> FindAllSkippingGrid(ControlType type)
        {
            var results = new List<AutomationElement>();
            WalkSkippingGrid(e => { if (SafeControlType(e) == type) results.Add(e); return false; });
            return results;
        }

        /// <summary>Wait until the grid exists and has realized its first row (search done, page bound).</summary>
        private AutomationElement WaitForPopulatedGrid(int timeoutMs)
        {
            var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            AutomationElement grid = null;
            while (DateTime.Now < deadline)
            {
                grid = FindByIdSkippingGrid("ResultsGrid", 2000);
                // FindFirstDescendant short-circuits at the first row — cheap while the page is small
                // (default size 100 during the wait).
                if (grid?.FindFirstDescendant(cf => cf.ByControlType(ControlType.DataItem)) != null) return grid;
                Thread.Sleep(1000);
            }
            return grid;
        }

        /// <summary>Read the pager's "start–end of total" line (collected without walking the grid).</summary>
        private (long start, long end, string raw) ReadPager()
        {
            var raw = FindAllSkippingGrid(ControlType.Text)
                         .Select(SafeName)
                         .FirstOrDefault(n => n.Contains("Page ") && n.Contains(" of ")) ?? "";
            var m = Regex.Match(raw, @"([\d,]+)\s*[–-]\s*([\d,]+)"); // en-dash or hyphen
            long s = -1, e = -1;
            if (m.Success)
            {
                long.TryParse(m.Groups[1].Value.Replace(",", ""), out s);
                long.TryParse(m.Groups[2].Value.Replace(",", ""), out e);
            }
            return (s, e, raw);
        }

        private void ClickPagerButton(string text)
        {
            var btn = FindAllSkippingGrid(ControlType.Button)
                         .FirstOrDefault(b => SafeName(b).IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.IsNotNull(btn, $"Pagination button containing '{text}' not found.");
            btn.Click();
        }

        [TestMethod]
        [Timeout(420000)] // 7 min: generous for gen + 5M search, but fails fast if wedged
        public void FiveMillionRows_AllPageSizes_PaginateToEnd_AndScroll()
        {
            if (_app?.HasExited ?? true) Assert.Inconclusive("Application is not running.");

            // 1) Wait for the 5M-row search to finish and the grid to bind its first page.
            var loadSw = Stopwatch.StartNew();
            var grid = WaitForPopulatedGrid(300000); // 5 min
            loadSw.Stop();
            Assert.IsNotNull(grid, "ResultsGrid never appeared — the 5M-row search did not complete in time.");
            Assert.IsNotNull(grid.FindFirstDescendant(cf => cf.ByControlType(ControlType.DataItem)),
                "ResultsGrid has no rows after the 5M search.");
            TestContext.WriteLine($"5M-row search + first page bound in {loadSw.Elapsed.TotalSeconds:F1}s");

            // 2) Exercise EVERY page size on the same 5M-row data set. For each: reset to page 1
            //    (pager must read "1–<size> of 5000000"), pixel-scroll the page (viewport must move),
            //    then jump to the last page (pager must read "<5,000,000-size+1>–5,000,000").
            TestContext.WriteLine("page size |   pages | first page    | scroll %         | last page");
            foreach (var pageSize in PageSizes)
            {
                var combo = FindByIdSkippingGrid("PageSizeCombo")?.AsComboBox();
                Assert.IsNotNull(combo, $"[size {pageSize}] PageSizeCombo not found.");
                try { combo.Select(pageSize.ToString()); }
                catch { combo.Expand(); _mainWindow.FindFirstDescendant(cf => cf.ByName(pageSize.ToString()))?.Click(); }
                Thread.Sleep(1000);

                // Changing the page size re-renders the grid, which stales the cached element + its
                // scroll-pattern handle (Scroll() then throws "operation not valid"). Re-acquire it.
                grid = WaitForPopulatedGrid(30000) ?? grid;
                Assert.IsNotNull(grid, $"[size {pageSize}] grid disappeared after page-size change.");

                // --- First page ---
                ClickPagerButton("First");
                Thread.Sleep(900);
                var first = ReadPager();
                StringAssert.Contains(first.raw, RowCount.ToString(), $"[size {pageSize}] total should be {RowCount:N0}.");
                Assert.AreEqual(1, first.start, $"[size {pageSize}] first page should start at row 1 (pager: '{first.raw}').");
                Assert.AreEqual(pageSize, first.end, $"[size {pageSize}] first page should end at row {pageSize} (pager: '{first.raw}').");

                // --- Pixel-scroll within the page (O(1) scroll pattern; never touches rows) ---
                var scroll = grid.Patterns.Scroll.PatternOrDefault;
                Assert.IsNotNull(scroll, $"[size {pageSize}] DataGrid exposes no scroll pattern.");
                bool scrollable = scroll.VerticallyScrollable.ValueOrDefault;
                // Paging is async now, so the initial scroll offset is timing-dependent — after the
                // previous size's jump-to-last the grid is parked at the bottom, and a "First" click when
                // already on page 1 is a no-op (no reload → no auto scroll-to-top). Pin to the top here so
                // "did scrolling move the viewport?" is deterministic regardless of load timing.
                if (scrollable) { scroll.SetScrollPercent(-1, 0); Thread.Sleep(300); }
                double before = scroll.VerticalScrollPercent.ValueOrDefault;
                var scrollSw = Stopwatch.StartNew();
                // Only drive Scroll when the grid reports it's scrollable — calling Scroll on a
                // non-scrollable / mid-render grid throws "operation not valid due to current state".
                if (scrollable)
                    for (int i = 0; i < 12; i++) { scroll.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeIncrement); Thread.Sleep(35); }
                scrollSw.Stop();
                Thread.Sleep(250);
                double after = scroll.VerticalScrollPercent.ValueOrDefault;
                if (scrollable)
                    Assert.IsTrue(after > before + 0.5,
                        $"[size {pageSize}] viewport did not scroll within the page ({before:F1}% -> {after:F1}%).");

                // --- Last page ---
                var jumpSw = Stopwatch.StartNew();
                ClickPagerButton("Last");
                Thread.Sleep(1500);
                jumpSw.Stop();
                var last = ReadPager();
                Assert.AreEqual(RowCount, last.end,
                    $"[size {pageSize}] last page should end at row {RowCount:N0} (pager: '{last.raw}').");
                Assert.AreEqual(RowCount - pageSize + 1, last.start,
                    $"[size {pageSize}] last page should start at row {RowCount - pageSize + 1:N0} (pager: '{last.raw}').");

                int totalPages = (RowCount + pageSize - 1) / pageSize;
                TestContext.WriteLine(
                    $"{pageSize,9} | {totalPages,7:N0} | {first.start}-{first.end,-7} | " +
                    $"{before,3:F0}->{after,3:F0}%{(scrollable ? "    " : "(fit)")} {scrollSw.ElapsedMilliseconds,4}ms | " +
                    $"{last.start:N0}-{last.end:N0} ({jumpSw.ElapsedMilliseconds,4}ms)");
            }
        }
    }
}
