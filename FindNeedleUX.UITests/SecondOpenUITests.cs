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
    /// "I have an .evtx open and I open another one": the second launch (Explorer double-click,
    /// "Open with", a command line) is handed to the running window by the single-instance owner, and
    /// the Open-into-workspace setting decides what happens: Add (default) puts both in one workspace,
    /// Replace starts over, Ask shows the Add / Replace / Cancel dialog. Each test owns the
    /// single-instance key for its duration, so it refuses to run if a FindNeedle it did not start is
    /// already up (it would receive the activation instead).
    /// </summary>
    [TestClass]
    [TestCategory("UITests")]
    [TestCategory("SkipCI")]
    [TestCategory("UiSmoke")] // self-contained (generated data, small): runs in the CI ui-smoke job
    public class SecondOpenUITests
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
                try { if (SettingsPath != null && File.Exists(SettingsPath)) File.Delete(SettingsPath); } catch { }
                foreach (var p in Process.GetProcessesByName("FindNeedleUX")) { try { p.Kill(); } catch { } } // stray second instances
                Thread.Sleep(800);
            }
        }

        /// <summary>The first instance: single-instancing ON (it registers the key), isolated settings
        /// pre-seeded with the open policy under test.</summary>
        private static Session LaunchOwner(string log, string policy)
        {
            if (Process.GetProcessesByName("FindNeedleUX").Length > 0)
                Assert.Inconclusive("a FindNeedle instance this test did not start is running; it would receive the activation.");
            var s = new Session { Automation = new UIA3Automation() };
            var psi = UiTestHelpers.IsolatedLaunch(UiTestHelpers.GetAppExecutablePath(), $"\"{log}\" --viewer=native", out s.SettingsPath);
            psi.EnvironmentVariables["FINDNEEDLE_NO_SINGLE_INSTANCE"] = "0"; // the suite sets 1 for isolation; this test needs the real behaviour
            File.WriteAllText(s.SettingsPath, $"{{\"OpenIntoWorkspace\":\"{policy}\"}}");
            s.App = Application.Launch(psi);
            Thread.Sleep(3000);
            s.Window = s.App.GetMainWindow(s.Automation);
            try { s.Window?.Patterns.Window.PatternOrDefault?.SetWindowVisualState(WindowVisualState.Maximized); } catch { }
            Assert.IsNotNull(s.Window, "the app window did not come up.");
            return s;
        }

        /// <summary>The second launch: what Explorer does. It must hand off to the owner and exit.</summary>
        private static void OpenAgain(Session owner, string log)
        {
            var psi = new ProcessStartInfo(UiTestHelpers.GetAppExecutablePath(), $"\"{log}\"") { UseShellExecute = false };
            psi.EnvironmentVariables["FINDNEEDLE_NO_SINGLE_INSTANCE"] = "0";
            psi.EnvironmentVariables["FINDNEEDLE_VIEWER_SETTINGS"] = owner.SettingsPath;
            using var second = Process.Start(psi);
            Assert.IsNotNull(second, "the second launch did not start");
            Assert.IsTrue(second.WaitForExit(30000), "the second launch should hand its file to the running instance and exit, not open a second window.");
        }

        private static string WriteLog(string prefix, int lines)
        {
            var path = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}.log");
            var start = new DateTime(2026, 1, 1, 0, 0, 0);
            using var sw = new StreamWriter(path, false, Encoding.ASCII);
            for (int i = 0; i < lines; i++)
            {
                sw.Write('['); sw.Write(start.AddSeconds(i).ToString("yyyy-MM-dd HH:mm:ss")); sw.Write("] INFO: line "); sw.Write(i); sw.Write('\n');
            }
            return path;
        }

        private static string TextOf(AutomationElement e) { try { return e?.Properties.Name.ValueOrDefault ?? ""; } catch { return ""; } }
        private static string Chip(AutomationElement w) => TextOf(UiTestHelpers.FindByIdSkippingGrid(w, "WorkspaceChipSummary", 5000));
        private static bool WaitUntil(Func<bool> cond, int timeoutMs)
        {
            var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < deadline) { if (cond()) return true; Thread.Sleep(300); }
            return cond();
        }
        private static long PagerTotal(AutomationElement window)
        {
            var (_, _, raw) = UiTestHelpers.ReadPager(window);
            var ms = System.Text.RegularExpressions.Regex.Matches(raw ?? "", @"of\s+([\d,]+)");
            return ms.Count > 0 ? long.Parse(ms[ms.Count - 1].Groups[1].Value.Replace(",", "")) : -1;
        }
        private static void Invoke(AutomationElement e)
        {
            Assert.IsNotNull(e, "element to invoke was not found");
            var inv = e.Patterns.Invoke.PatternOrDefault;
            if (inv != null) inv.Invoke(); else e.Click();
        }
        private static void TryDelete(params string[] paths) { foreach (var p in paths) try { File.Delete(p); } catch { } }

        [TestMethod]
        [Timeout(240000)]
        public void SecondFile_DefaultAdd_JoinsTheWorkspace_NoSecondWindow_SameFileTwiceIsOne()
        {
            var a = WriteLog("fn_second_a", 150);
            var b = WriteLog("fn_second_b", 250);
            try
            {
                using var s = LaunchOwner(a, "Add");
                Assert.IsNotNull(UiTestHelpers.WaitForPopulatedGrid(s.Window, 45000), "the first file did not load.");
                Assert.IsTrue(WaitUntil(() => PagerTotal(s.Window) == 150, 30000));

                OpenAgain(s, b);
                Assert.IsTrue(WaitUntil(() => Chip(s.Window).Contains("2 sources"), 30000), $"the second file should be ADDED to the workspace (chip: '{Chip(s.Window)}').");
                Assert.IsTrue(WaitUntil(() => PagerTotal(s.Window) == 400, 60000), $"both files' rows should show (pager: {PagerTotal(s.Window)}).");
                Assert.AreEqual(1, Process.GetProcessesByName("FindNeedleUX").Length, "exactly one FindNeedle window: the second launch handed off and exited.");

                // The same file again is not a third source.
                OpenAgain(s, b);
                Thread.Sleep(4000);
                StringAssert.Contains(Chip(s.Window), "2 sources", "opening a file that is already loaded must not add it again.");
            }
            finally { TryDelete(a, b); }
        }

        [TestMethod]
        [Timeout(240000)]
        public void SecondFile_ReplacePolicy_StartsOver()
        {
            var a = WriteLog("fn_second_a", 150);
            var b = WriteLog("fn_second_b", 250);
            try
            {
                using var s = LaunchOwner(a, "Replace");
                Assert.IsNotNull(UiTestHelpers.WaitForPopulatedGrid(s.Window, 45000), "the first file did not load.");
                Assert.IsTrue(WaitUntil(() => PagerTotal(s.Window) == 150, 30000));

                OpenAgain(s, b);
                Assert.IsTrue(WaitUntil(() => PagerTotal(s.Window) == 250, 60000), $"Replace: only the second file's rows (pager: {PagerTotal(s.Window)}).");
                StringAssert.Contains(Chip(s.Window), "1 source", "Replace: the workspace holds just the new file");
                Assert.IsNull(UiTestHelpers.FindByIdSkippingGrid(s.Window, "OpenIntoWorkspaceDialog", 1000), "Replace never asks");
            }
            finally { TryDelete(a, b); }
        }

        [TestMethod]
        [Timeout(240000)]
        public void SecondFile_AskPolicy_ShowsTheChoice_AndReplaceIsHonoured()
        {
            var a = WriteLog("fn_second_a", 150);
            var b = WriteLog("fn_second_b", 250);
            try
            {
                using var s = LaunchOwner(a, "Ask");
                Assert.IsNotNull(UiTestHelpers.WaitForPopulatedGrid(s.Window, 45000), "the first file did not load.");
                Assert.IsTrue(WaitUntil(() => PagerTotal(s.Window) == 150, 30000));

                OpenAgain(s, b);
                // The dialog: "Open into the current workspace?" with Add to workspace / Replace workspace / Cancel.
                var desktop = s.Automation.GetDesktop();
                AutomationElement replace = null;
                Assert.IsTrue(WaitUntil(() => (replace = desktop.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Replace workspace")))) != null, 20000),
                    "Ask: the Add / Replace / Cancel dialog should appear for the second file.");
                Assert.IsNotNull(desktop.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Add to workspace"))), "the dialog should offer Add");
                Assert.IsNotNull(desktop.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Cancel"))), "the dialog should offer Cancel");
                Invoke(replace);

                Assert.IsTrue(WaitUntil(() => PagerTotal(s.Window) == 250, 60000), $"choosing Replace should leave only the second file (pager: {PagerTotal(s.Window)}).");
                StringAssert.Contains(Chip(s.Window), "1 source");
            }
            finally { TryDelete(a, b); }
        }

        [TestMethod]
        [Timeout(240000)]
        public void SecondFile_AskPolicy_Cancel_LeavesTheWorkspaceUntouched()
        {
            var a = WriteLog("fn_second_a", 150);
            var b = WriteLog("fn_second_b", 250);
            try
            {
                using var s = LaunchOwner(a, "Ask");
                Assert.IsNotNull(UiTestHelpers.WaitForPopulatedGrid(s.Window, 45000), "the first file did not load.");
                Assert.IsTrue(WaitUntil(() => PagerTotal(s.Window) == 150, 30000));

                OpenAgain(s, b);
                var desktop = s.Automation.GetDesktop();
                AutomationElement cancel = null;
                Assert.IsTrue(WaitUntil(() => desktop.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Replace workspace"))) != null
                                             && (cancel = desktop.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Cancel")))) != null, 20000),
                    "the Add / Replace / Cancel dialog should appear.");
                Invoke(cancel);
                Thread.Sleep(3000);
                Assert.AreEqual(150, PagerTotal(s.Window), "Cancel: nothing changes");
                StringAssert.Contains(Chip(s.Window), "1 source");
            }
            finally { TryDelete(a, b); }
        }
    }
}
