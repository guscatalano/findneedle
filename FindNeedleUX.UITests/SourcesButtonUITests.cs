using Microsoft.VisualStudio.TestTools.UnitTesting;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace FindNeedleUX.UITests
{
    /// <summary>
    /// Regression test for the crash hitting the "Sources" button on a large load: it must open the
    /// "Loaded sources" dialog (with the per-source-type counts) and NOT take the app down. Drives the
    /// real app at the big mixed fixture (cache-hit load, so it's fast) and clicks SourcesButton.
    /// [UITests][SkipCI] — needs the generated fixture, the built .exe, and a desktop.
    /// </summary>
    [TestClass]
    [TestCategory("UITests")]
    [TestCategory("SkipCI")]
    public class SourcesButtonUITests
    {
        public TestContext TestContext { get; set; }

        private static string RepoRoot()
            => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

        [TestMethod]
        [Timeout(360000)]
        public void SourcesButton_OnLargeLoad_OpensDialog_DoesNotCrash()
        {
            var zip = Path.Combine(RepoRoot(), "LargeSamples", "mixed-filter-fixture.zip");
            if (!File.Exists(zip)) Assert.Inconclusive($"fixture not found: {zip}");

            var automation = new UIA3Automation();
            Application app = null;
            try
            {
                app = Application.Launch(UiTestHelpers.GetAppExecutablePath(), $"\"{zip}\" --viewer=native");
                Thread.Sleep(3000);
                var window = app.GetMainWindow(automation);
                Assert.IsNotNull(window, "app window did not come up");
                try { window.Patterns.Window.PatternOrDefault?.SetWindowVisualState(WindowVisualState.Maximized); } catch { }

                var grid = UiTestHelpers.WaitForPopulatedGrid(window, 300000);
                Assert.IsNotNull(grid, "viewer never populated");

                var sources = UiTestHelpers.FindByIdSkippingGrid(window, "SourcesButton", 15000);
                Assert.IsNotNull(sources, "SourcesButton not found");

                // Click it TWICE in quick succession — what an impatient user does while the dialog's
                // source-type counts (a multi-second GROUP BY over millions of rows) are still computing
                // and nothing has appeared yet. A second ContentDialog.ShowAsync while the first is
                // pending is what crashed the app ("only one ContentDialog open at a time").
                void Click()
                {
                    var inv = sources.Patterns.Invoke.PatternOrDefault;
                    if (inv != null) inv.Invoke(); else sources.Click();
                }
                // Tight burst: maximize the chance a second handler fires while the first is still mid
                // GROUP BY (no dialog up yet) → two ContentDialog.ShowAsync overlap.
                for (int i = 0; i < 6; i++) { Click(); Thread.Sleep(50); }

                // The "Loaded sources" dialog must appear within a reasonable time…
                AutomationElement dialog = null;
                var deadline = DateTime.Now.AddSeconds(30);
                while (DateTime.Now < deadline && dialog == null)
                {
                    Assert.IsFalse(app.HasExited, "the app CRASHED after clicking Sources (process exited).");
                    UiTestHelpers.WalkSkippingGrid(window, e =>
                    {
                        if (UiTestHelpers.SafeName(e).IndexOf("Loaded sources", StringComparison.OrdinalIgnoreCase) >= 0)
                        { dialog = e; return true; }
                        return false;
                    });
                    if (dialog == null) Thread.Sleep(500);
                }

                Assert.IsFalse(app.HasExited, "the app CRASHED after clicking Sources (process exited).");
                Assert.IsNotNull(dialog, "the 'Loaded sources' dialog did not open after clicking Sources.");
                TestContext.WriteLine("Sources dialog opened; app alive.");
            }
            finally
            {
                try { if (app != null && !app.HasExited) app.Close(); } catch { }
                // Close() may not terminate the process and Dispose() never does — Kill so it can't linger.
                try { if (app != null && !app.HasExited) app.Kill(); } catch { }
                try { app?.Dispose(); } catch { }
                try { automation?.Dispose(); } catch { }
            }
        }
    }
}
