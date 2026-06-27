using Microsoft.VisualStudio.TestTools.UnitTesting;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace FindNeedleUX.UITests
{
    /// <summary>
    /// Utility (not an assertion test): launches the real app at the big mixed fixture zip, waits for the
    /// viewer to populate, and captures a screenshot so we can SEE what loading the bundle looks like.
    /// [UITests][SkipCI] — needs the generated fixture, the built .exe, and a desktop.
    /// </summary>
    [TestClass]
    [TestCategory("UITests")]
    [TestCategory("SkipCI")]
    public class CaptureFixtureScreenshot
    {
        public TestContext TestContext { get; set; }

        private static string RepoRoot()
            => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

        [TestMethod]
        [Timeout(360000)]
        public void Capture_MixedFixture_Viewer()
        {
            var zip = Path.Combine(RepoRoot(), "LargeSamples", "mixed-filter-fixture.zip");
            if (!File.Exists(zip)) Assert.Inconclusive($"fixture not found: {zip}");

            var outPng = Path.Combine(RepoRoot(), "LargeSamples", "fixture-viewer.png");

            var automation = new UIA3Automation();
            Application app = null;
            try
            {
                app = Application.Launch(UiTestHelpers.GetAppExecutablePath(), $"\"{zip}\" --viewer=native");
                Thread.Sleep(3000);
                var window = app.GetMainWindow(automation);
                Assert.IsNotNull(window, "app window did not come up");
                try { window.Patterns.Window.PatternOrDefault?.SetWindowVisualState(WindowVisualState.Maximized); } catch { }

                // Big bundle: extraction + ETL decode + parse + ingest before the first page shows.
                var grid = UiTestHelpers.WaitForPopulatedGrid(window, 300000);
                Assert.IsNotNull(grid, "viewer never populated");
                Thread.Sleep(2500); // let the first page + status strip settle

                // Log the row-count / pager text so we can report what's on screen.
                var (s, e, raw) = UiTestHelpers.ReadPager(window);
                TestContext.WriteLine($"pager: '{raw}' (rows {s}-{e})");
                foreach (var t in UiTestHelpers.FindAllSkippingGrid(window, ControlType.Text)
                                               .Select(UiTestHelpers.SafeName)
                                               .Where(n => n.Length > 0 &&
                                                      (n.Contains("row", StringComparison.OrdinalIgnoreCase)
                                                    || n.Contains("result", StringComparison.OrdinalIgnoreCase)
                                                    || n.Contains("of "))).Distinct().Take(12))
                    TestContext.WriteLine($"status: {t}");

                Capture.Element(window).ToFile(outPng);
                TestContext.WriteLine($"screenshot: {outPng}");
                Assert.IsTrue(File.Exists(outPng), "screenshot was not written");
            }
            finally
            {
                try { app?.Close(); } catch { }
                // Close() may not terminate the process and Dispose() never does — Kill so it can't linger.
                try { if (app != null && !app.HasExited) app.Kill(); } catch { }
                try { app?.Dispose(); } catch { }
                try { automation?.Dispose(); } catch { }
            }
        }
    }
}
