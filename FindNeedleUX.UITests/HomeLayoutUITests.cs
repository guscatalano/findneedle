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
    /// Arranging Home: a section collapses to its header from the chevron, hides from the Customize Home
    /// pencil, and both survive a relaunch of the same profile. The layout file lives beside the isolated
    /// viewer settings, so each test starts from the shipped layout.
    /// </summary>
    [TestClass]
    [TestCategory("UITests")]
    [TestCategory("SkipCI")]
    [TestCategory("UiSmoke")] // self-contained (no data needed): runs in the CI ui-smoke job
    public class HomeLayoutUITests
    {
        private sealed class Session : IDisposable
        {
            public UIA3Automation Automation; public Application App; public AutomationElement Window; public string SettingsPath;
            public void Dispose()
            {
                try { App?.Close(); } catch { }
                try { if (App != null && !App.HasExited) App.Kill(); } catch { }
                try { App?.Dispose(); } catch { }
                try { Automation?.Dispose(); } catch { }
                Thread.Sleep(800);
            }
        }

        private static Session Launch(string settingsPath)
        {
            var s = new Session { Automation = new UIA3Automation(), SettingsPath = settingsPath };
            var psi = new System.Diagnostics.ProcessStartInfo(UiTestHelpers.GetAppExecutablePath(), "") { UseShellExecute = false };
            psi.EnvironmentVariables["FINDNEEDLE_VIEWER_SETTINGS"] = settingsPath;
            s.App = Application.Launch(psi);
            Thread.Sleep(3000);
            s.Window = s.App.GetMainWindow(s.Automation);
            try { s.Window?.Patterns.Window.PatternOrDefault?.SetWindowVisualState(WindowVisualState.Maximized); } catch { }
            Assert.IsNotNull(s.Window, "the app window did not come up.");
            return s;
        }

        private static bool WaitUntil(Func<bool> cond, int timeoutMs)
        {
            var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < deadline) { if (cond()) return true; Thread.Sleep(250); }
            return cond();
        }

        private static AutomationElement ByName(AutomationElement root, string name, int timeoutMs = 10000)
        {
            AutomationElement found = null;
            WaitUntil(() => (found = root.FindFirstDescendant(cf => cf.ByName(name))) != null, timeoutMs);
            return found;
        }

        private static void Invoke(AutomationElement e)
        {
            Assert.IsNotNull(e, "element to invoke was not found");
            var inv = e.Patterns.Invoke.PatternOrDefault;
            if (inv != null) { inv.Invoke(); return; }
            var tog = e.Patterns.Toggle.PatternOrDefault;
            if (tog != null) { tog.Toggle(); return; }
            e.Click();
        }

        [TestMethod]
        [Timeout(180000)]
        public void Collapse_AndHide_SurviveARelaunch()
        {
            var settings = Path.Combine(Path.GetTempPath(), $"fn_home_ui_{Guid.NewGuid():N}.json");
            try
            {
                using (var s = Launch(settings))
                {
                    Assert.IsNotNull(ByName(s.Window, "Open a log", 15000), "Home did not render.");
                    // Shipped layout: the Recent card is expanded (its See all link is visible) and Known logs is shown.
                    Assert.IsNotNull(ByName(s.Window, "Collapse Recent searches"), "the Recent card should have a collapse chevron.");
                    Assert.IsNotNull(ByName(s.Window, "Known logs", 5000), "Known logs should be shown by default.");

                    // Collapse Recent from its chevron: the chevron flips to Expand and the card body goes away.
                    Invoke(ByName(s.Window, "Collapse Recent searches"));
                    Assert.IsTrue(WaitUntil(() => s.Window.FindFirstDescendant(cf => cf.ByName("Expand Recent searches")) != null, 5000),
                        "after collapsing, the chevron should read Expand.");
                    Assert.IsTrue(WaitUntil(() => s.Window.FindFirstDescendant(cf => cf.ByName("See all").And(cf.ByControlType(ControlType.Hyperlink))) == null
                                               || s.Window.FindFirstDescendant(cf => cf.ByName("Collapse Recent searches")) == null, 5000));

                    // Hide Known logs from the pencil: Customize Home ▸ Known logs ▸ Show (untick).
                    Invoke(ByName(s.Window, "Customize Home"));
                    var desktop = s.Automation.GetDesktop();
                    var known = ByName(desktop, "Known logs", 8000);
                    Assert.IsNotNull(known, "the Customize Home menu should list Known logs.");
                    known.Patterns.ExpandCollapse.PatternOrDefault?.Expand();
                    var show = ByName(desktop, "Show", 8000);
                    Assert.IsNotNull(show, "the Known logs submenu should have a Show toggle.");
                    Invoke(show);
                    Assert.IsTrue(WaitUntil(() => s.Window.FindFirstDescendant(cf => cf.ByName("Known logs").And(cf.ByControlType(ControlType.Text))) == null, 8000),
                        "Known logs should disappear from Home once hidden.");
                }

                // Same profile again: still collapsed, still hidden.
                using (var s = Launch(settings))
                {
                    Assert.IsNotNull(ByName(s.Window, "Open a log", 15000), "Home did not render on relaunch.");
                    Assert.IsNotNull(ByName(s.Window, "Expand Recent searches", 8000), "Recent should come back collapsed.");
                    Assert.IsNull(s.Window.FindFirstDescendant(cf => cf.ByName("Known logs").And(cf.ByControlType(ControlType.Text))), "Known logs should stay hidden.");
                    // And the Open card is untouchable: no chevron, no menu entry.
                    Assert.IsNull(s.Window.FindFirstDescendant(cf => cf.ByName("Collapse Open a log")), "the Open card must not be collapsible.");
                }
            }
            finally
            {
                try { File.Delete(settings); } catch { }
                try { File.Delete(settings + ".home-sections.json"); } catch { }
            }
        }
    }
}
