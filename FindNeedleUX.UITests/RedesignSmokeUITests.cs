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
    /// Smoke tests for the ux-redesign surfaces, driven through UI Automation on the real app: the Home
    /// Tools row, the breadcrumb's Home root, the workspace chip, the filter dock (Left / Top), the in-row
    /// details with their action bar, and cancelling a run from the loading screen. Each test launches
    /// its own isolated instance (throwaway viewer settings) so a developer's persisted layout cannot
    /// change what is asserted. Presence + one interaction each; the logic behind these surfaces is
    /// covered by the WinUI unit tests.
    /// </summary>
    [TestClass]
    [TestCategory("UITests")]
    [TestCategory("SkipCI")]
    [TestCategory("UiSmoke")] // self-contained (generated data, small): runs in the CI ui-smoke job
    public class RedesignSmokeUITests
    {
        public TestContext TestContext { get; set; }

        private sealed class Session : IDisposable
        {
            public UIA3Automation Automation;
            public Application App;
            public AutomationElement Window;
            public string SettingsPath;

            public void Dispose()
            {
                try { App?.Close(); } catch { }
                try { if (App != null && !App.HasExited) App.Kill(); } catch { }
                try { App?.Dispose(); } catch { }
                try { Automation?.Dispose(); } catch { }
                try { if (SettingsPath != null && File.Exists(SettingsPath)) File.Delete(SettingsPath); } catch { }
                Thread.Sleep(800);
            }
        }

        /// <summary>Launch with isolated settings. <paramref name="args"/> is the raw command line ("" = Home).</summary>
        private static Session Launch(string args, int settleMs = 3000, string presetSettings = null)
        {
            var s = new Session { Automation = new UIA3Automation() };
            var psi = UiTestHelpers.IsolatedLaunch(UiTestHelpers.GetAppExecutablePath(), args, out s.SettingsPath);
            if (presetSettings != null) File.WriteAllText(s.SettingsPath, presetSettings); // start in a chosen layout
            s.App = Application.Launch(psi);
            Thread.Sleep(settleMs);
            s.Window = s.App.GetMainWindow(s.Automation);
            try { s.Window?.Patterns.Window.PatternOrDefault?.SetWindowVisualState(WindowVisualState.Maximized); } catch { }
            Assert.IsNotNull(s.Window, "the app window did not come up.");
            return s;
        }

        private static AutomationElement FindByName(AutomationElement root, string nameContains, int timeoutMs = 10000, ControlType? type = null, bool exact = false)
        {
            var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            do
            {
                AutomationElement found = null;
                UiTestHelpers.WalkSkippingGrid(root, e =>
                {
                    if (type.HasValue && UiTestHelpers.SafeControlType(e) != type.Value) return false;
                    var name = UiTestHelpers.SafeName(e);
                    bool hit = exact ? string.Equals(name, nameContains, StringComparison.OrdinalIgnoreCase)
                                     : name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (hit) { found = e; return true; }
                    return false;
                });
                if (found != null) return found;
                Thread.Sleep(300);
            } while (DateTime.Now < deadline);
            return null;
        }

        private static string TextOf(AutomationElement e)
        {
            if (e == null) return "";
            try { var n = e.Properties.Name.ValueOrDefault; if (!string.IsNullOrEmpty(n)) return n; } catch { }
            try { return e.Patterns.Text.PatternOrDefault?.DocumentRange.GetText(-1) ?? ""; } catch { return ""; }
        }

        private static void Invoke(AutomationElement e)
        {
            Assert.IsNotNull(e, "element to invoke was not found");
            var inv = e.Patterns.Invoke.PatternOrDefault;
            if (inv != null) inv.Invoke(); else e.Click();
        }

        private static string Breadcrumb(AutomationElement window)
            => TextOf(UiTestHelpers.FindByIdSkippingGrid(window, "BreadcrumbTitle", 5000));

        private static bool WaitUntil(Func<bool> cond, int timeoutMs)
        {
            var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < deadline) { if (cond()) return true; Thread.Sleep(250); }
            return cond();
        }

        // ---- Home ----

        [TestMethod]
        [Timeout(120000)]
        public void Home_HasToolsRow_NotShortcuts_AndBreadcrumbRoot()
        {
            using var s = Launch("");
            Assert.IsNotNull(FindByName(s.Window, "Open a log", 15000), "Home did not render its Open card.");
            Assert.AreEqual("Home", Breadcrumb(s.Window), "the breadcrumb on Home should read just 'Home'.");

            // The tile row is "Tools" now, and never "Shortcuts".
            Assert.IsNotNull(FindByName(s.Window, "Tools", 5000, ControlType.Text, exact: true), "the Home 'Tools' row title is missing.");
            Assert.IsNull(FindByName(s.Window, "Shortcuts", 1500, ControlType.Text, exact: true), "the old 'Shortcuts' title is still on Home.");
            Assert.IsNotNull(FindByName(s.Window, "Customize tools", 5000), "the Tools row has no Customize… affordance.");

            // Whatever tiles this profile has pinned (the tile selection is machine state, not part of the
            // isolated viewer settings), none may repeat a card action.
            foreach (var retired in new[] { "Open results", "Run search", "Open log file…", "Open with rules…" })
            {
                // These exist as card buttons; they must not ALSO be tiles. A tile is a Button whose name is
                // exactly the label; the card buttons carry the same label, so count them: exactly one.
                var n = UiTestHelpers.FindAllSkippingGrid(s.Window, ControlType.Button)
                    .Count(b => string.Equals(UiTestHelpers.SafeName(b), retired, StringComparison.OrdinalIgnoreCase));
                Assert.IsTrue(n <= 1, $"'{retired}' appears {n} times on Home — the card action is being repeated as a tile.");
            }
        }

        // ---- Breadcrumb + workspace chip ----

        [TestMethod]
        [Timeout(120000)]
        public void Viewer_BreadcrumbHomeRoot_ReturnsHome_AndChipShowsCounts()
        {
            var log = UiTestHelpers.WriteBracketedLog(150, "findneedle_crumb");
            try
            {
                using var s = Launch($"\"{log}\" --viewer=native");
                UiTestHelpers.WaitForPopulatedGrid(s.Window, 45000);
                StringAssert.Contains(Breadcrumb(s.Window), "Results", "opening a log should land on Results.");

                // The workspace chip is THE summary of composition; the window title carries only the name.
                var chip = TextOf(UiTestHelpers.FindByIdSkippingGrid(s.Window, "WorkspaceChipSummary", 5000));
                StringAssert.Contains(chip, "1 source", "the workspace chip should count the one opened source.");
                StringAssert.Contains(chip, "0 rule files", "the workspace chip should count rule files.");
                var title = UiTestHelpers.SafeName(s.Window);
                Assert.IsFalse(title.Contains("source", StringComparison.OrdinalIgnoreCase),
                    $"the window title should be the workspace name only, not counts: '{title}'");

                // The breadcrumb's Home root is the way back.
                Invoke(UiTestHelpers.FindByIdSkippingGrid(s.Window, "BreadcrumbHome", 5000));
                Assert.IsTrue(WaitUntil(() => Breadcrumb(s.Window) == "Home", 10000),
                    $"clicking the breadcrumb Home root did not navigate Home (breadcrumb: '{Breadcrumb(s.Window)}').");
                Assert.IsNotNull(FindByName(s.Window, "Open a log", 5000), "Home did not render after the breadcrumb click.");
            }
            finally { try { File.Delete(log); } catch { } }
        }

        // ---- Filter dock ----

        [TestMethod]
        [Timeout(180000)]
        public void FilterDock_TopIsCompact_WithQuickRulesOverflow_AndLeftHasTheRail()
        {
            // Start IN top mode. Switching Left -> Top at runtime re-parents the sections, and UI Automation
            // then loses the moved subtree (the tree enumerates up to the first Time chip and stops) even
            // though the screen is right - tracked in docs/ux-followups.md section 7. Top -> Left is fine.
            var log = UiTestHelpers.WriteBracketedLog(150, "findneedle_dock");
            try
            {
                using var s = Launch($"\"{log}\" --viewer=native", presetSettings: "{\"FilterDock\":\"Top\"}");
                var grid = UiTestHelpers.WaitForPopulatedGrid(s.Window, 45000);
                Assert.IsNotNull(grid, "the results grid did not populate.");

                // Top: the quick rules live in the overflow button, the rail's "+ New quick rule" is gone.
                var overflow = UiTestHelpers.FindByIdSkippingGrid(s.Window, "TopOverflowButton", 10000);
                Assert.IsNotNull(overflow, "top dock: the 'Quick rules ▾' overflow button did not appear.");
                StringAssert.StartsWith(UiTestHelpers.SafeName(overflow), "Quick rules", "the overflow button caption");
                Assert.IsNull(FindByName(s.Window, "+ New quick rule", 1500, ControlType.Button),
                    "top dock: the rail's '+ New quick rule' should not be present (it lives in the overflow).");

                // The band between the search box and the grid stays short: two rows, not the old stacked block.
                var search = UiTestHelpers.FindByIdSkippingGrid(s.Window, "SearchBox", 5000);
                Assert.IsNotNull(search, "SearchBox not found");
                var band = grid.BoundingRectangle.Top - search.BoundingRectangle.Bottom;
                // ~80px at 100% DPI for two rows; allow for the banner row and DPI, but the old ~190px block fails.
                // The band wraps on a narrow desktop (the CI runner is 1024x768), so only judge height when
                // the window is wide enough for the two-row layout.
                if (s.Window.BoundingRectangle.Width >= 1400)
                    Assert.IsTrue(band < 150, $"top dock band is {band}px tall between the search box and the grid; expected a compact band (< 150px).");
                else
                    Assert.IsTrue(band < 320, $"top dock band is {band}px tall on a {s.Window.BoundingRectangle.Width}px-wide window; even wrapped it should stay under 320px.");

                // Left restores the rail with the Quick rules section, and the overflow button goes away.
                Invoke(FindByName(s.Window, "Filters: Left", 5000, ControlType.Button));
                Assert.IsNotNull(FindByName(s.Window, "+ New quick rule", 10000, ControlType.Button),
                    "switching to Left did not show the Quick rules section in the rail.");
                Assert.IsTrue(WaitUntil(() => UiTestHelpers.FindByIdSkippingGrid(s.Window, "TopOverflowButton", 500) == null, 5000),
                    "left dock: the top-band overflow button should be gone.");
            }
            finally { try { File.Delete(log); } catch { } }
        }

        // ---- In-row details ----

        [TestMethod]
        [Timeout(180000)]
        public void RowDetails_InRow_ShowsFieldsAndActionBar()
        {
            var log = UiTestHelpers.WriteBracketedLog(150, "findneedle_details");
            try
            {
                using var s = Launch($"\"{log}\" --viewer=native");
                var grid = UiTestHelpers.WaitForPopulatedGrid(s.Window, 45000);
                Assert.IsNotNull(grid, "the results grid did not populate.");

                // In row is the default details mode: selecting a row expands the detail under it.
                var row = grid.FindFirstChild(cf => cf.ByControlType(ControlType.DataItem));
                Assert.IsNotNull(row, "no data row to select.");
                var sel = row.Patterns.SelectionItem.PatternOrDefault;
                Assert.IsNotNull(sel, "the row has no SelectionItem pattern.");
                sel.Select();

                Assert.IsNotNull(grid.FindFirstDescendant(cf => cf.ByName("Log entry details")),
                    "selecting a row did not expand the in-row details.");
                foreach (var action in new[] { "Filter in", "Filter out", "Follow", "Tag", "Copy" })
                    Assert.IsNotNull(grid.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName(action + " ▾"))),
                        $"the in-row action bar is missing '{action} ▾'.");

                // Level is shown; RawLevel is not — a plain-text log has no source level to show.
                Assert.IsNotNull(grid.FindFirstDescendant(cf => cf.ByName("Level")), "the details lack the Level row.");
                Assert.IsNull(grid.FindFirstDescendant(cf => cf.ByName("RawLevel")),
                    "a text log has no raw level; the RawLevel row should be hidden.");
            }
            finally { try { File.Delete(log); } catch { } }
        }

        // ---- Cancel ----

        [TestMethod]
        [Timeout(300000)]
        public void CancelFromLoadingScreen_StaysHome_AndStripReportsCancelled()
        {
            // Big enough that the load outlasts the click, small enough to write quickly (~50 MB).
            var log = UiTestHelpers.WriteBracketedLog(900_000, "findneedle_cancel");
            try
            {
                using var s = Launch($"\"{log}\" --viewer=native", settleMs: 800);

                // The loading screen's Cancel, and the status bar's Run-turned-Stop, both appear while running.
                var cancel = FindByName(s.Window, "Cancel current operation", 20000, ControlType.Button);
                if (cancel == null)
                {
                    // The run finished before we could look: nothing to cancel on this machine. Not a failure.
                    Assert.Inconclusive("the load finished before the Cancel button was seen; increase the log size for this machine.");
                }
                var stop = FindByName(s.Window, "Stop", 5000, ControlType.Button, exact: true);
                Assert.IsNotNull(stop, "while a search runs the status-bar Run should read Stop.");

                Invoke(cancel);

                // After cancelling: still Home (no empty viewer), Run is back, and the chip still has the source.
                Assert.IsTrue(WaitUntil(() => FindByName(s.Window, "Cancel current operation", 300, ControlType.Button) == null, 60000),
                    "the loading screen did not go away after Cancel.");
                Assert.AreEqual("Home", Breadcrumb(s.Window), "a cancelled open should stay on Home, not open an empty viewer.");
                Assert.IsNull(UiTestHelpers.FindByIdSkippingGrid(s.Window, "ResultsGrid", 1500), "an (empty) viewer was opened after Cancel.");
                Assert.IsTrue(WaitUntil(() => FindByName(s.Window, "Run", 500, ControlType.Button, exact: true) != null, 10000),
                    "the status bar did not go back to Run after the cancelled search ended.");
                Assert.IsNotNull(FindByName(s.Window, "cancelled", 5000, ControlType.Text),
                    "the Last run summary should say the run was cancelled.");
            }
            finally { try { File.Delete(log); } catch { } }
        }
    }
}
