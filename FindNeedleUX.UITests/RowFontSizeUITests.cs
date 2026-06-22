using Microsoft.VisualStudio.TestTools.UnitTesting;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace FindNeedleUX.UITests
{
    /// <summary>
    /// Drives the real shipping UI to verify the "Row text size" preference actually resizes the
    /// result-grid rows — and that changing it while a file is loaded does NOT freeze the app.
    ///
    /// Flow: launch FindNeedleUX.exe with a temp log via the CLI hook (same as the scroll test) →
    /// measure a realized row's height → Settings ▸ Preferences, bump "Row text size" to the
    /// largest option → back to Results → measure again. The rows must be visibly taller, and the
    /// whole change must complete well within a time budget (a UI-thread hang would blow the budget
    /// or make the post-change grid read time out).
    ///
    /// Manual lane (excluded from CI): needs an interactive desktop and the built .exe.
    /// </summary>
    [TestClass]
    [TestCategory("UITests")]
    [TestCategory("SkipCI")]
    public class RowFontSizeUITests
    {
        private static Application _app;
        private static Window _mainWindow;
        private static UIA3Automation _automation;
        private static string _tempLogPath;

        // Enough rows to fill the viewport so rows are realized and a height is measurable.
        private const int LineCount = 3000;

        public TestContext TestContext { get; set; }

        [ClassInitialize]
        public static void Setup(TestContext context)
        {
            try
            {
                _tempLogPath = UiTestHelpers.WriteBracketedLog(LineCount, "findneedle_fonttest");
                _automation = new UIA3Automation();
                var appPath = UiTestHelpers.GetAppExecutablePath();
                _app = Application.Launch(appPath, $"\"{_tempLogPath}\" --viewer=native");
                Thread.Sleep(3000);
                _mainWindow = _app.GetMainWindow(_automation);
                Assert.IsNotNull(_mainWindow, "Failed to get main window");
            }
            catch (Exception ex)
            {
                Assert.Inconclusive(
                    $"Could not launch app for the row-font-size test (needs an interactive desktop). {ex.Message}");
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

        /// <summary>Median height of the realized data rows. Median (not min/first) so the taller
        /// selected/details row or a virtualization-race partial row doesn't skew it.</summary>
        private static double MedianRealizedRowHeight(AutomationElement grid)
        {
            var heights = grid.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem))
                .Select(r => { try { return (double)r.BoundingRectangle.Height; } catch { return 0.0; } })
                .Where(h => h > 1)
                .OrderBy(h => h)
                .ToList();
            if (heights.Count == 0) return 0;
            return heights[heights.Count / 2];
        }

        /// <summary>Open a top MenuBar item and click one of its flyout entries by AutomationId
        /// (the XAML x:Name). The flyout opens in a popup that isn't always under the main window's
        /// element, so the item is polled for across the whole desktop.</summary>
        private void ClickMenu(string menuName, string itemAutomationId)
        {
            var menu = _mainWindow.FindFirstDescendant(cf =>
                cf.ByControlType(ControlType.MenuItem).And(cf.ByName(menuName)));
            Assert.IsNotNull(menu, $"Menu '{menuName}' should exist");
            // Prefer ExpandCollapse (opens the flyout); fall back to a click.
            var ecp = menu.Patterns.ExpandCollapse.PatternOrDefault;
            if (ecp != null) { try { ecp.Expand(); } catch { menu.Click(); } } else { menu.Click(); }

            var desktop = _automation.GetDesktop();
            AutomationElement item = null;
            var deadline = DateTime.Now.AddSeconds(5);
            while (DateTime.Now < deadline)
            {
                item = _mainWindow.FindFirstDescendant(cf => cf.ByAutomationId(itemAutomationId))
                       ?? desktop.FindFirstDescendant(cf => cf.ByAutomationId(itemAutomationId));
                if (item != null) break;
                Thread.Sleep(200);
            }
            Assert.IsNotNull(item, $"Menu item '{itemAutomationId}' should appear under '{menuName}'");
            // Invoke is more reliable than Click for flyout items that may be partially off-screen.
            var inv = item.Patterns.Invoke.PatternOrDefault;
            if (inv != null) { try { inv.Invoke(); } catch { item.Click(); } } else { item.Click(); }
            Thread.Sleep(900);
        }

        /// <summary>Change "Row text size" to the option whose label contains <paramref name="labelContains"/>
        /// via Settings ▸ Preferences, then return to the results viewer. Returns how long the round trip
        /// took (used as a freeze guard).</summary>
        private long SetRowFont(string labelContains)
        {
            var sw = Stopwatch.StartNew();
            ClickMenu("Settings", "settings_resultviewer");

            var combo = UiTestHelpers.FindByIdSkippingGrid(_mainWindow, "RowFontSizeCombo", 15000);
            Assert.IsNotNull(combo, "RowFontSizeCombo not found on the Preferences page.");
            var box = combo.AsComboBox();
            box.Expand();
            Thread.Sleep(300);
            var item = box.Items.FirstOrDefault(i => UiTestHelpers.SafeName(i).Contains(labelContains));
            Assert.IsNotNull(item, $"Row text size option '{labelContains}' not present in the combo.");
            item.Select();
            Thread.Sleep(400);

            ClickMenu("Run & Results", "results_viewnative");
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }

        [TestMethod]
        [Timeout(120000)]
        public void ChangingRowFontSize_ResizesRows_WithoutFreezing()
        {
            if (_app?.HasExited ?? true)
                Assert.Inconclusive("Application is not running.");

            var grid = UiTestHelpers.WaitForPopulatedGrid(_mainWindow, 60000);
            Assert.IsNotNull(grid, "ResultsGrid did not appear — the CLI load/search may not have completed.");
            Assert.IsNotNull(
                grid.FindFirstDescendant(cf => cf.ByControlType(ControlType.DataItem)),
                "ResultsGrid has no rows — the search returned nothing or rows never realized.");

            // The setting persists across runs (viewer-settings.json), so don't trust the launch
            // state — drive a known small font first, then the largest, and compare the two.
            long smallMs = SetRowFont("Compact");
            var smallGrid = UiTestHelpers.WaitForPopulatedGrid(_mainWindow, 30000);
            Assert.IsNotNull(smallGrid, "ResultsGrid did not return after setting a small font (possible freeze).");
            double smallHeight = MedianRealizedRowHeight(smallGrid);

            long largeMs = SetRowFont("Extra large");
            var largeGrid = UiTestHelpers.WaitForPopulatedGrid(_mainWindow, 30000);
            Assert.IsNotNull(largeGrid, "ResultsGrid did not return after setting a large font (possible freeze).");
            double largeHeight = MedianRealizedRowHeight(largeGrid);

            TestContext.WriteLine(
                $"Row height: Compact(9)={smallHeight:F0}px, Extra large(20)={largeHeight:F0}px. " +
                $"Change round trips took {smallMs} ms / {largeMs} ms.");

            // Rows must be clearly taller at the large font (RowHeight 26 -> ceil(20*1.9)=38).
            Assert.IsTrue(largeHeight > smallHeight + 5,
                $"Rows did not grow with a larger font: Compact={smallHeight:F0}px, Extra large={largeHeight:F0}px.");

            // Freeze guard: each change should be near-instant. A UI-thread hang would blow this budget
            // (or the post-change grid reads above would have timed out).
            Assert.IsTrue(smallMs < 20000 && largeMs < 20000,
                $"A row-font change took too long ({smallMs} ms / {largeMs} ms) — the UI likely froze.");
        }
    }
}
