using Microsoft.VisualStudio.TestTools.UnitTesting;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace FindNeedleUX.UITests
{
    /// <summary>
    /// Regenerates the README screenshots (docs/screenshots/) from the committed synthetic demo logs
    /// (Samples/Demo/logs, produced by gen_logs.py). NOT an assertion test — a maintainer tool, run on
    /// demand. Uses FlaUI to drive by automation id/name (resolution-independent, unlike a pixel script)
    /// and a throwaway FINDNEEDLE_VIEWER_SETTINGS file so the theme/state is deterministic regardless of
    /// what's persisted. [UITests][SkipCI] — needs the built .exe + a desktop session.
    ///
    /// Run just these:  vstest.console FindNeedleUX.UITests.dll /TestCaseFilter:"FullyQualifiedName~GenerateReadmeScreenshots"
    /// </summary>
    [TestClass]
    [TestCategory("UITests")]
    [TestCategory("SkipCI")]
    public class GenerateReadmeScreenshots
    {
        public TestContext TestContext { get; set; }

        private static string RepoRoot()
            => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        private static string DemoLogs => Path.Combine(RepoRoot(), "Samples", "Demo", "logs");
        private static string OutDir => Path.Combine(RepoRoot(), "docs", "screenshots");

        /// <summary>Launch the viewer over the demo logs with a given color theme, run an optional
        /// interaction (a query), and write a screenshot of the whole window.</summary>
        private void Capture(string theme, string outName, Action<AutomationElement> interact = null)
        {
            if (!Directory.Exists(DemoLogs))
                Assert.Inconclusive($"demo logs not found: {DemoLogs} — run `python Samples/Demo/gen_logs.py` first.");
            Directory.CreateDirectory(OutDir);

            // Throwaway settings so the theme is deterministic (not whatever the user has persisted).
            var settings = Path.Combine(Path.GetTempPath(), $"fn_shot_{Guid.NewGuid():N}.json");
            File.WriteAllText(settings, "{\"ThemeName\":\"" + theme + "\",\"ColorTaggedRows\":true}");

            var automation = new UIA3Automation();
            Application app = null;
            try
            {
                var psi = new ProcessStartInfo(UiTestHelpers.GetAppExecutablePath())
                {
                    Arguments = $"\"{DemoLogs}\" --viewer=native",
                    UseShellExecute = false,
                };
                psi.EnvironmentVariables["FINDNEEDLE_VIEWER_SETTINGS"] = settings;

                app = Application.Launch(psi);
                Thread.Sleep(3000);
                var window = app.GetMainWindow(automation);
                Assert.IsNotNull(window, "app window did not come up");
                try { window.Patterns.Window.PatternOrDefault?.SetWindowVisualState(WindowVisualState.Maximized); } catch { }

                var grid = UiTestHelpers.WaitForPopulatedGrid(window, 120_000);
                Assert.IsNotNull(grid, "viewer never populated");
                Thread.Sleep(2500); // first page + status strip + histogram settle

                interact?.Invoke(window);
                Thread.Sleep(1500);

                var outPng = Path.Combine(OutDir, outName);
                Capture.Element(window).ToFile(outPng);
                TestContext.WriteLine($"wrote {outPng}");
                Assert.IsTrue(File.Exists(outPng), $"screenshot not written: {outPng}");
            }
            finally
            {
                try { app?.Close(); } catch { }
                try { if (app != null && !app.HasExited) app.Kill(); } catch { }
                try { app?.Dispose(); } catch { }
                try { automation?.Dispose(); } catch { }
                try { File.Delete(settings); } catch { }
            }
        }

        /// <summary>Type a structured query into the search box and submit — same path a user takes.</summary>
        private static void Query(AutomationElement window, string q)
        {
            var box = UiTestHelpers.FindByIdSkippingGrid(window, "SearchBox", 15_000);
            Assert.IsNotNull(box, "SearchBox not found");
            box.Focus();
            Keyboard.Type(q);
            Keyboard.Type(VirtualKeyShort.RETURN);
            Thread.Sleep(3000); // let it filter + the counts/histogram update
        }

        [TestMethod, Timeout(180_000)]
        public void Gen_01_ResultsOverview()
            => Capture("Sunset", "01-results-overview.png");

        [TestMethod, Timeout(180_000)]
        public void Gen_02_QueryLanguage()
            => Capture("Sunset", "02-query-language.png", w => Query(w, "level == Error OR level == Warning"));

        [TestMethod, Timeout(180_000)]
        public void Gen_03_LevelFilter()
            => Capture("Sunset", "03-level-filter.png", w => Query(w, "level == Error"));
    }
}
