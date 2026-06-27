using Microsoft.VisualStudio.TestTools.UnitTesting;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace FindNeedleUX.UITests
{
    /// <summary>
    /// Real WinUI DataGrid pixel-scroll test. Unlike the data-layer scroll benchmark
    /// (RowScrollingPerformanceTests, which times IPagedLogSource.GetPage), this drives the
    /// *actual shipping UI*: it launches FindNeedleUX.exe pointed at a real log file via the
    /// command line, lets the normal load→search pipeline fill the native viewer's
    /// CommunityToolkit DataGrid, then scrolls the grid and asserts the viewport actually moved.
    ///
    /// It uses the command-line load hook (FindNeedleUX.exe &lt;path&gt; --viewer=native) instead of
    /// the file picker on purpose: the WinUI file picker crashes when the app runs elevated, and
    /// automating the OS file dialog is slow and fragile. The CLI hook is the same path the
    /// findneedle.exe console app already exposes.
    ///
    /// Prerequisites (manual lane — excluded from CI):
    ///   1. FindNeedleUX must be built (the test launches the built .exe).
    ///   2. Requires an interactive desktop session. Run it (un-elevated is fine now).
    /// </summary>
    [TestClass]
    [TestCategory("UITests")]
    [TestCategory("SkipCI")]
    public class NativeGridScrollUITests
    {
        private static Application _app;
        private static Window _mainWindow;
        private static UIA3Automation _automation;
        private static string _tempLogPath;

        // Enough rows that the first page (PageSize = 100, RowHeight = 26 → ~2600px) overflows
        // any reasonable viewport, so the grid is genuinely scrollable.
        private const int LineCount = 3000;

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
            {
                if (File.Exists(path) && File.GetLastWriteTime(path) > newestTime)
                {
                    newestTime = File.GetLastWriteTime(path);
                    newestPath = path;
                }
            }
            if (newestPath != null) return newestPath;
            throw new FileNotFoundException(
                $"Could not find FindNeedleUX.exe. Searched: {string.Join(", ", possiblePaths)}");
        }

        private static string WriteTempLog(int lines)
        {
            var path = Path.Combine(Path.GetTempPath(), $"findneedle_scrolltest_{Guid.NewGuid():N}.log");
            var sb = new StringBuilder(lines * 64);
            var start = new DateTime(2026, 1, 1, 0, 0, 0);
            for (int i = 0; i < lines; i++)
            {
                // Use the bracketed "[yyyy-MM-dd HH:mm:ss] LEVEL: message" format the plain-text
                // processor parses a timestamp from. Lines without a parseable [time] get a
                // DateTime.MinValue and are dropped by the default time filter, leaving the grid
                // empty — so the format matters for this test. Each line becomes one result row;
                // the Index column reflects its position, which is what we read to detect scrolling.
                sb.Append('[')
                  .Append(start.AddSeconds(i).ToString("yyyy-MM-dd HH:mm:ss"))
                  .Append("] INFO: scroll test message line number ")
                  .Append(i)
                  .Append('\n');
            }
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        [ClassInitialize]
        public static void Setup(TestContext context)
        {
            try
            {
                _tempLogPath = WriteTempLog(LineCount);
                _automation = new UIA3Automation();
                var appPath = GetAppExecutablePath();

                // Launch straight into the native viewer with the temp log loaded — no picker.
                _app = Application.Launch(appPath, $"\"{_tempLogPath}\" --viewer=native");

                // The app activates the welcome page immediately, then runs the search and
                // navigates to the viewer. Give it a moment, then resolve the main window.
                Thread.Sleep(3000);
                _mainWindow = _app.GetMainWindow(_automation);
                Assert.IsNotNull(_mainWindow, "Failed to get main window");
            }
            catch (Exception ex)
            {
                Assert.Inconclusive(
                    $"Could not launch app for the DataGrid scroll test (needs an interactive desktop). {ex.Message}");
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

        /// <summary>
        /// Wait until the native viewer's DataGrid exists and has realized at least one data row
        /// (i.e. the search completed and rows were bound). Returns the grid element.
        /// </summary>
        private AutomationElement WaitForPopulatedGrid(int timeoutMs)
        {
            var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            AutomationElement grid = null;
            while (DateTime.Now < deadline)
            {
                grid = _mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ResultsGrid"));
                if (grid != null)
                {
                    // A DataGrid with rows exposes DataItem descendants once rows are realized.
                    var anyRow = grid.FindFirstDescendant(cf => cf.ByControlType(ControlType.DataItem));
                    if (anyRow != null) return grid;
                }
                Thread.Sleep(300);
            }
            return grid; // may be null or empty — caller asserts
        }

        /// <summary>
        /// Get an IScrollProvider for the grid: the DataGrid itself usually exposes the Scroll
        /// pattern; if not, fall back to the first scrollable descendant (its inner ScrollViewer).
        /// </summary>
        private static FlaUI.Core.Patterns.IScrollPattern GetScrollPattern(AutomationElement grid)
        {
            var p = grid.Patterns.Scroll.PatternOrDefault;
            if (p != null) return p;
            foreach (var d in grid.FindAllDescendants())
            {
                var dp = d.Patterns.Scroll.PatternOrDefault;
                if (dp != null && dp.VerticallyScrollable.ValueOrDefault) return dp;
            }
            return null;
        }

        /// <summary>
        /// Best-effort read of the Index value shown in the top-most realized row, for logging /
        /// a secondary assertion. Returns null if it can't be parsed.
        /// </summary>
        private static int? TryReadTopRowIndex(AutomationElement grid)
        {
            // The Index column is the only pure-integer cell, so scan every realized row's cells
            // for the first integer value and take the smallest — that's the top-most visible row.
            int? top = null;
            try
            {
                foreach (var row in grid.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem)))
                {
                    foreach (var cell in row.FindAllDescendants(cf => cf.ByControlType(ControlType.Text)))
                    {
                        string name;
                        try { name = cell.Properties.Name.ValueOrDefault ?? ""; }
                        catch { continue; } // property not supported on this element
                        if (int.TryParse(name.Trim(), out var v))
                        {
                            if (top == null || v < top) top = v;
                            break;
                        }
                    }
                }
            }
            catch { /* virtualization race — ignore */ }
            return top;
        }

        [TestMethod]
        [Timeout(120000)]
        public void NativeDataGrid_PixelScroll_MovesViewport()
        {
            if (_app?.HasExited ?? true)
                Assert.Inconclusive("Application is not running.");

            var grid = WaitForPopulatedGrid(60000);
            Assert.IsNotNull(grid, "ResultsGrid did not appear — the CLI load/search may not have completed.");
            Assert.IsNotNull(
                grid.FindFirstDescendant(cf => cf.ByControlType(ControlType.DataItem)),
                "ResultsGrid has no rows — the search returned nothing or rows never realized.");

            var scroll = GetScrollPattern(grid);
            Assert.IsNotNull(scroll, "Could not obtain a vertical scroll pattern from the DataGrid.");
            Assert.IsTrue(scroll.VerticallyScrollable.ValueOrDefault,
                "DataGrid is not vertically scrollable — not enough rows to fill the viewport?");

            double beforePct = scroll.VerticalScrollPercent.ValueOrDefault;
            int? beforeIdx = TryReadTopRowIndex(grid);

            // Scroll the viewport down in several large increments and time it. This exercises the
            // real DataGrid render/virtualization path (the thing that makes scrolling feel fast),
            // not just the data source.
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 12; i++)
            {
                // The synchronous UIA Scroll COM call can time out (0x80131505) if the UI thread is
                // momentarily busy — e.g. an async page still realizing rows. A single skipped
                // increment doesn't change the outcome (we still assert the viewport moved overall),
                // so tolerate a transient timeout with one short-backoff retry instead of failing.
                try { scroll.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeIncrement); }
                catch (Exception ex) when (ex is TimeoutException || ex is System.Runtime.InteropServices.COMException)
                {
                    Thread.Sleep(250);
                    try { scroll.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeIncrement); } catch { /* skip this increment */ }
                }
                Thread.Sleep(40);
            }
            sw.Stop();

            Thread.Sleep(300); // let the final frame settle / rows realize
            double afterPct = scroll.VerticalScrollPercent.ValueOrDefault;
            int? afterIdx = TryReadTopRowIndex(grid);

            TestContext.WriteLine(
                $"Scrolled in {sw.ElapsedMilliseconds} ms over 12 large increments. " +
                $"VerticalScrollPercent {beforePct:F1} -> {afterPct:F1}. " +
                $"Top row Index {beforeIdx?.ToString() ?? "?"} -> {afterIdx?.ToString() ?? "?"}.");

            // Primary assertion: the viewport genuinely moved (real pixel scroll).
            Assert.IsTrue(afterPct > beforePct + 0.5,
                $"Viewport did not scroll: VerticalScrollPercent {beforePct:F1} -> {afterPct:F1}.");

            // Secondary (best-effort) assertion: a row that was off-screen is now the top row,
            // proving virtualized rows were brought in during the scroll.
            if (beforeIdx.HasValue && afterIdx.HasValue)
            {
                Assert.IsTrue(afterIdx.Value > beforeIdx.Value,
                    $"Top realized row index did not advance: {beforeIdx} -> {afterIdx}.");
            }
        }
    }
}
