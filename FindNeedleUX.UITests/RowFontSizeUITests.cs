using Microsoft.VisualStudio.TestTools.UnitTesting;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace FindNeedleUX.UITests
{
    /// <summary>
    /// Verifies the "Row text size" preference actually resizes the result-grid TEXT (not just the row
    /// height, which moves independently via RowHeight). Rather than drive the flaky WinUI MenuBar to
    /// reach Preferences, this persists the font to viewer-settings.json (the same store the app reads)
    /// and launches the app twice — once small, once large — measuring the realized cell-text height
    /// each time. No menu automation, so it's deterministic. The launch itself exercises the on-load
    /// ApplyRowFontSize path (per-column FontSize), and a healthy grid render proves there's no freeze.
    ///
    /// Manual lane (excluded from CI): needs an interactive desktop and the built .exe.
    /// </summary>
    [TestClass]
    [TestCategory("UITests")]
    [TestCategory("SkipCI")]
    public class RowFontSizeUITests
    {
        private const int LineCount = 3000;
        private static string _tempLogPath;
        private static string _settingsPath;
        private static string _settingsBackup; // original file contents, restored on cleanup

        public TestContext TestContext { get; set; }

        [ClassInitialize]
        public static void Setup(TestContext context)
        {
            _tempLogPath = UiTestHelpers.WriteBracketedLog(LineCount, "findneedle_fonttest");
            _settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FindNeedle", "viewer-settings.json");
            // Preserve the user's real settings — this test rewrites the file.
            try { if (File.Exists(_settingsPath)) _settingsBackup = File.ReadAllText(_settingsPath); }
            catch { _settingsBackup = null; }
        }

        [ClassCleanup]
        public static void TearDown()
        {
            try
            {
                if (_settingsBackup != null) File.WriteAllText(_settingsPath, _settingsBackup);
                else if (_settingsPath != null && File.Exists(_settingsPath)) File.Delete(_settingsPath);
            }
            catch { /* best effort */ }
            try { if (_tempLogPath != null && File.Exists(_tempLogPath)) File.Delete(_tempLogPath); } catch { }
        }

        /// <summary>Write the persisted row text size, then launch the app pointed at the temp log, wait
        /// for the grid, and return the median realized cell-text height (the app applies the font on
        /// load via ApplyRowFontSize). Returns -1 if the app/grid couldn't be brought up.</summary>
        private double MeasureWithFont(int fontPx)
        {
            File.WriteAllText(_settingsPath, $"{{\"RowFontSize\":{fontPx},\"RowHeightRatio\":1.9}}");

            UIA3Automation automation = null;
            Application app = null;
            try
            {
                automation = new UIA3Automation();
                app = Application.Launch(UiTestHelpers.GetAppExecutablePath(), $"\"{_tempLogPath}\" --viewer=native");
                Thread.Sleep(3000);
                var window = app.GetMainWindow(automation);
                if (window == null) return -1;
                try { window.Patterns.Window.PatternOrDefault?.SetWindowVisualState(WindowVisualState.Maximized); } catch { }

                var grid = UiTestHelpers.WaitForPopulatedGrid(window, 45000);
                if (grid?.FindFirstDescendant(cf => cf.ByControlType(ControlType.DataItem)) == null) return -1;
                Thread.Sleep(500); // let the first page settle at the applied size
                return MedianCellTextHeight(grid);
            }
            finally
            {
                try { app?.Close(); } catch { }
                try { app?.Dispose(); } catch { }
                try { automation?.Dispose(); } catch { }
                Thread.Sleep(800);
            }
        }

        /// <summary>Median height of the realized cell TEXT elements — tracks the actual font size,
        /// unlike row height (which the viewer sets independently via RowHeight).</summary>
        private static double MedianCellTextHeight(AutomationElement grid)
        {
            var heights = new System.Collections.Generic.List<double>();
            foreach (var row in grid.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem)))
            {
                foreach (var cell in row.FindAllDescendants(cf => cf.ByControlType(ControlType.Text)))
                {
                    try
                    {
                        var h = (double)cell.BoundingRectangle.Height;
                        if (h > 1 && !string.IsNullOrWhiteSpace(UiTestHelpers.SafeName(cell))) heights.Add(h);
                    }
                    catch { /* virtualization race — skip */ }
                }
            }
            if (heights.Count == 0) return 0;
            heights.Sort();
            return heights[heights.Count / 2];
        }

        [TestMethod]
        [Timeout(180000)]
        public void RowFontSize_ResizesCellText()
        {
            double smallText = MeasureWithFont(9);
            Assert.IsTrue(smallText > 1, "Could not measure cell text at the small font (app/grid didn't come up).");

            double largeText = MeasureWithFont(20);
            Assert.IsTrue(largeText > 1, "Could not measure cell text at the large font (app/grid didn't come up).");

            TestContext.WriteLine($"Cell text height: font 9 = {smallText:F0}px, font 20 = {largeText:F0}px.");

            // The TEXT must be clearly larger at the bigger font — this is what proves the per-column
            // FontSize is applied (row height alone would grow even if the text didn't).
            Assert.IsTrue(largeText > smallText + 4,
                $"Cell text did not grow with a larger font: font 9 = {smallText:F0}px, font 20 = {largeText:F0}px.");
        }
    }
}
