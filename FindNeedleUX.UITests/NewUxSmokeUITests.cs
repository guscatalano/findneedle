using Microsoft.VisualStudio.TestTools.UnitTesting;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using System;
using System.IO;
using System.Threading;

namespace FindNeedleUX.UITests
{
    /// <summary>
    /// Functional smoke tests for UX surfaces added recently — each launches the real app at a file and
    /// asserts the new surface renders and responds: the CSV "Remap columns…" button (and that it no
    /// longer auto-pops the mapping dialog on open), and the result-viewer filter row. These are
    /// intentionally shallow (presence / one interaction), not perf — the heavier logic (source-type
    /// classification, settings defaults, level presets, …) is covered by the WinUI unit tests.
    ///
    /// NOTE on reach: only controls that surface as real UI Automation nodes are findable here — a
    /// Button/Edit/ComboBox with an explicit AutomationProperties.AutomationId. Bare panels are pruned
    /// from the UIA tree, and flyout/dialog popups aren't reliably reachable from the window root, so
    /// surfaces behind those (Settings nav, the Sources dialog) are left to the unit tests.
    ///
    /// Manual lane (UITests + SkipCI): needs an interactive desktop and the built .exe; these launch
    /// the app window and drive it via UI Automation.
    /// </summary>
    [TestClass]
    [TestCategory("UITests")]
    [TestCategory("SkipCI")]
    public class NewUxSmokeUITests
    {
        public TestContext TestContext { get; set; }

        // ---- launch / find helpers (mirror the grid-pruning approach in UiTestHelpers) ----

        private static (UIA3Automation automation, Application app, AutomationElement window) Launch(string pathArg)
            => LaunchArgs($"\"{pathArg}\" --viewer=native");

        private static (UIA3Automation automation, Application app, AutomationElement window) LaunchArgs(string rawArgs)
        {
            var automation = new UIA3Automation();
            var app = Application.Launch(UiTestHelpers.GetAppExecutablePath(), rawArgs);
            Thread.Sleep(3000);
            var window = app.GetMainWindow(automation);
            try { window?.Patterns.Window.PatternOrDefault?.SetWindowVisualState(WindowVisualState.Maximized); } catch { }
            return (automation, app, window);
        }

        private static void Shutdown(Application app, UIA3Automation automation)
        {
            try { app?.Close(); } catch { }
            try { app?.Dispose(); } catch { }
            try { automation?.Dispose(); } catch { }
            Thread.Sleep(800);
        }

        /// <summary>Find the first element whose Name contains <paramref name="nameContains"/>, pruning
        /// the ResultsGrid subtree (which would otherwise expose thousands of peers).</summary>
        private static AutomationElement FindByName(AutomationElement root, string nameContains, int timeoutMs = 10000)
        {
            var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < deadline)
            {
                AutomationElement found = null;
                UiTestHelpers.WalkSkippingGrid(root, e =>
                {
                    if (UiTestHelpers.SafeName(e).IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                    { found = e; return true; }
                    return false;
                });
                if (found != null) return found;
                Thread.Sleep(300);
            }
            return null;
        }

        private static void Invoke(AutomationElement e)
        {
            var inv = e.Patterns.Invoke.PatternOrDefault;
            if (inv != null) inv.Invoke(); else e.Click();
        }

        private static string WriteTempCsv()
        {
            var p = Path.Combine(Path.GetTempPath(), $"findneedle_csvui_{Guid.NewGuid():N}.csv");
            File.WriteAllText(p, "time,level,message,provider,region\n"
                + "2026-01-01 00:00:00,INFO,hello world,Auth,US\n"
                + "2026-01-01 00:00:01,ERROR,boom,Auth,EU\n");
            return p;
        }

        // ---- tests ----

        [TestMethod]
        [Timeout(120000)]
        public void Csv_ShowsRemapButton_AndDoesNotAutoPopup()
        {
            var csv = WriteTempCsv();
            UIA3Automation automation = null; Application app = null;
            try
            {
                (automation, app, var window) = Launch(csv);
                Assert.IsNotNull(window, "the app window did not come up.");
                UiTestHelpers.WaitForPopulatedGrid(window, 45000);

                // The mapping dialog must NOT auto-open on load (the jarring popup we removed).
                Assert.IsNull(FindByName(window, "Map CSV columns", 2500),
                    "the CSV mapping dialog auto-popped on open — it should be opt-in via the button.");

                // The "Remap columns…" button appears in the status strip when a CSV is loaded.
                var remap = UiTestHelpers.FindByIdSkippingGrid(window, "CsvRemapButton", 10000);
                Assert.IsNotNull(remap, "the CSV 'Remap columns…' button did not appear for a loaded CSV.");

                // Clicking it opens the mapping dialog on demand.
                Invoke(remap);
                Assert.IsNotNull(FindByName(window, "Map CSV columns", 8000),
                    "clicking Remap did not open the column-mapping dialog.");
            }
            finally { Shutdown(app, automation); try { File.Delete(csv); } catch { } }
        }

        [TestMethod]
        [Timeout(120000)]
        public void FilterPane_RendersFilterRow()
        {
            var log = UiTestHelpers.WriteBracketedLog(200, "findneedle_filterui");
            UIA3Automation automation = null; Application app = null;
            try
            {
                (automation, app, var window) = Launch(log);
                Assert.IsNotNull(window, "the app window did not come up.");
                UiTestHelpers.WaitForPopulatedGrid(window, 45000);

                // The "Known ▾" toggle is always present in the filter row regardless of whether the
                // per-value combos or the substring boxes are showing — so it proves the row rendered.
                Assert.IsNotNull(UiTestHelpers.FindByIdSkippingGrid(window, "ShowKnownToggle", 10000),
                    "the filter row (Provider / TaskName / Source filters) did not render.");
            }
            finally { Shutdown(app, automation); try { File.Delete(log); } catch { } }
        }
    }
}
