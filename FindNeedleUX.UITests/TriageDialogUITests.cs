using Microsoft.VisualStudio.TestTools.UnitTesting;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace FindNeedleUX.UITests
{
    /// <summary>
    /// Verifies the large-file triage dialog actually works in the running app (the one piece that compiles
    /// + is unit-tested but needs a real run): opening a large .etl shows the provider-selection dialog, and
    /// "Load everything" closes it and loads the log. Guards against the ContentDialog re-entrancy failfast
    /// and the open-flow integration. Needs LargeSamples/large-5M.etl (881 MB ≥ the 500 MB triage threshold).
    /// </summary>
    [TestClass]
    [TestCategory("UITests")]
    [TestCategory("SkipCI")]
    [TestCategory("Performance")]
    public class TriageDialogUITests
    {
        private static UIA3Automation _automation;
        public TestContext TestContext { get; set; }

        [ClassInitialize]
        public static void Setup(TestContext c) => _automation = new UIA3Automation();

        [ClassCleanup]
        public static void TearDown() { try { _automation?.Dispose(); } catch { } }

        private static string LargeEtl()
        {
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && dir != null; i++)
            {
                var c = Path.Combine(dir, "LargeSamples", "large-5M.etl");
                if (File.Exists(c)) return c;
                dir = Directory.GetParent(dir)?.FullName;
            }
            return null;
        }

        [TestMethod]
        [Timeout(360_000)]
        public void TriageDialog_Appears_AndLoadEverything_Loads()
        {
            var etl = LargeEtl();
            if (etl == null) { Assert.Inconclusive("need LargeSamples/large-5M.etl"); return; }

            // Clear any cache from a prior run so the open actually re-scans and the grid repopulates (a
            // killed prior run can leave a cache that a later launch reuses → grid would read a stale/empty set).
            try
            {
                var cacheDb = FindNeedleCoreUtils.CachedStorage.GetCacheFilePath(etl, ".db");
                foreach (var p in new[] { cacheDb, cacheDb + "-wal", cacheDb + "-shm", cacheDb + "-journal" })
                    if (File.Exists(p)) File.Delete(p);
            }
            catch { }

            var psi = new ProcessStartInfo(UiTestHelpers.GetAppExecutablePath())
            {
                Arguments = $"\"{etl}\" --viewer=native --cache=off",
                UseShellExecute = false,
            };
            var app = Application.Launch(psi);
            try
            {
                Thread.Sleep(3000);
                var window = app.GetMainWindow(_automation);
                Assert.IsNotNull(window, "main window");
                try { window.Patterns.Window.PatternOrDefault?.SetWindowVisualState(WindowVisualState.Maximized); } catch { }

                // The triage dialog should appear (the bounded provider scan is sub-second). Find the actual
                // BUTTON (not the text label) so the click dismisses the dialog.
                var loadAll = FindButtonWithTimeout(window, "Load everything", 45_000);
                Assert.IsNotNull(loadAll, "triage dialog did not appear for a large .etl");

                var checks = window.FindAllDescendants(cf => cf.ByControlType(ControlType.CheckBox));
                TestContext.WriteLine($"triage dialog shown with {checks.Length} provider checkbox(es)");
                Assert.IsTrue(checks.Length >= 1, "dialog should list providers as checkboxes");

                // Choose "Load everything" → dialog closes, the full load runs.
                loadAll.AsButton().Invoke();
                // Confirm the dialog actually dismissed before waiting on the load.
                var dismissDeadline = DateTime.Now.AddSeconds(15);
                while (DateTime.Now < dismissDeadline && FindButtonWithTimeout(window, "Load everything", 300) != null)
                    Thread.Sleep(250);
                Assert.IsNull(FindButtonWithTimeout(window, "Load everything", 300), "triage dialog did not dismiss after Load everything");

                var grid = UiTestHelpers.WaitForPopulatedGrid(window, 150_000);
                Assert.IsNotNull(grid, "grid never populated after Load everything (dialog→load path broke)");
                // The pager reads "Page 1 of N · a–b of TOTAL" — the row total is the LAST "of N".
                var (_, _, raw) = UiTestHelpers.ReadPager(window);
                var ofMatches = System.Text.RegularExpressions.Regex.Matches(raw ?? "", @"of\s+([\d,]+)");
                long total = ofMatches.Count > 0 && long.TryParse(ofMatches[ofMatches.Count - 1].Groups[1].Value.Replace(",", ""), out var t) ? t : -1;
                TestContext.WriteLine($"loaded after triage: pager='{raw}' (total {total:N0})");
                Assert.IsTrue(total > 1_000_000, $"expected the full ~5M-row load, pager total was {total}");
            }
            finally
            {
                try { if (!app.HasExited) app.Close(); } catch { }
                try { if (!app.HasExited) app.Kill(); } catch { }
                try { app.Dispose(); } catch { }
                Thread.Sleep(1000);
            }
        }

        private static AutomationElement FindButtonWithTimeout(AutomationElement root, string name, int timeoutMs)
        {
            var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < deadline)
            {
                try
                {
                    var e = root.FindFirstDescendant(cf => cf.ByName(name).And(cf.ByControlType(ControlType.Button)));
                    if (e != null) return e;
                }
                catch { }
                Thread.Sleep(250);
            }
            return null;
        }
    }
}
