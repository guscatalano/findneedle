using Microsoft.VisualStudio.TestTools.UnitTesting;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace FindNeedleUX.UITests
{
    /// <summary>
    /// Opening something big must not freeze the window, and the loading screen must say something
    /// true while it happens.
    ///
    /// Every incident this suite exists for came from a real report: a 20-second dead window right
    /// after 7.4M rows landed (a GROUP BY on the UI thread), and a progress line stuck on
    /// "Building search index… starting…" for 89 seconds while the grid was already usable. Both
    /// looked identical to a hang, and neither was caught by a test that only asserted the final
    /// state - so these tests watch the app all the way THROUGH the load.
    ///
    /// Responsiveness is measured from outside with the Win32 "is this window pumping" check rather
    /// than through UIA: it is what Windows itself uses to decide an app has stopped responding, it
    /// costs nothing, and it cannot be satisfied by a UI thread that is busy but not painting.
    ///
    /// These need the big fixtures in LargeSamples\ and take minutes each, so they are opt-in:
    ///   dotnet test FindNeedleUX.UITests\FindNeedleUX.UITests.csproj -c Debug -p:Platform=x64 ^
    ///     --no-build --filter "TestCategory=Stress"
    /// A fixture that is not present reports Inconclusive rather than failing. Nothing here
    /// synthesises keyboard or mouse input, so it is safe to run on a machine in use - though the
    /// app window does come to the foreground while each case runs.
    /// </summary>
    [TestClass]
    [TestCategory("UITests")]
    [TestCategory("SkipCI")]   // needs LargeSamples; minutes per case
    [TestCategory("Stress")]
    public class LargeLoadStressUITests
    {
        public TestContext TestContext { get; set; }

        // ---- how much unresponsiveness is a failure ----

        /// <summary>A window that stops pumping for longer than this is "frozen" as far as a person is
        /// concerned. Windows itself draws the ghost "(Not Responding)" frame at 5s; half that is a
        /// budget we can actually hold, and the freeze that prompted this suite was 20s.</summary>
        private const int FreezeBudgetMs = 2500;

        /// <summary>How often the window is probed while a load runs.</summary>
        private const int ProbeIntervalMs = 250;

        /// <summary>Longest a single fixture may take to land its first page.</summary>
        private const int LoadTimeoutMs = 15 * 60 * 1000;

        // ---- Win32: the same question Task Manager asks ----

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
                                                        uint flags, uint timeoutMs, out IntPtr result);
        private const uint WM_NULL = 0x0000;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        /// <summary>Milliseconds the window took to answer a trivial message; the timeout when it did not.</summary>
        private static long PumpLatencyMs(IntPtr hwnd, uint timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            var ok = SendMessageTimeout(hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, timeoutMs, out _);
            sw.Stop();
            return ok == IntPtr.Zero ? timeoutMs : sw.ElapsedMilliseconds;
        }

        // ---- what a run observed ----

        private sealed class LoadObservations
        {
            public long WorstPumpMs;
            public string WorstPumpAt = "";
            public int SamplesOverBudget;
            public int Samples;
            public readonly List<string> BannerTexts = new();   // distinct, in order
            public bool SawStop;
            public bool SawBanner;
            public string LastBanner = "";
        }

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
                Thread.Sleep(1000);
            }
        }

        private static Session Launch(string filePath)
        {
            var s = new Session { Automation = new UIA3Automation() };
            var psi = UiTestHelpers.IsolatedLaunch(UiTestHelpers.GetAppExecutablePath(), $"\"{filePath}\" --viewer=native", out s.SettingsPath);
            s.App = Application.Launch(psi);
            Thread.Sleep(2500);
            s.Window = s.App.GetMainWindow(s.Automation);
            Assert.IsNotNull(s.Window, "the app window did not come up.");
            try { s.Window.Patterns.Window.PatternOrDefault?.SetWindowVisualState(WindowVisualState.Maximized); } catch { }
            return s;
        }

        private static string TextOf(AutomationElement e)
        { try { return e?.Properties.Name.ValueOrDefault ?? ""; } catch { return ""; } }

        /// <summary>
        /// Watch a load from start to finish: probe the window's responsiveness on a fixed cadence and
        /// record what the loading screen said, until the grid has rows (or the timeout runs out).
        /// </summary>
        private LoadObservations WatchLoad(Session s, out bool gridPopulated)
        {
            var obs = new LoadObservations();
            var hwnd = s.App.MainWindowHandle;
            var deadline = DateTime.Now.AddMilliseconds(LoadTimeoutMs);
            var started = DateTime.Now;
            gridPopulated = false;

            while (DateTime.Now < deadline)
            {
                // 1. Is the window still answering? This is the freeze check.
                var latency = PumpLatencyMs(hwnd, (uint)(FreezeBudgetMs * 4));
                obs.Samples++;
                if (latency > obs.WorstPumpMs)
                {
                    obs.WorstPumpMs = latency;
                    obs.WorstPumpAt = $"{(DateTime.Now - started).TotalSeconds:0.0}s into the load";
                }
                if (latency > FreezeBudgetMs) obs.SamplesOverBudget++;

                // 2. What is the loading screen saying? (Cheap reads; the grid is pruned from the walk.)
                var banner = UiTestHelpers.FindByIdSkippingGrid(s.Window, "ProgressBannerText", 300);
                var text = TextOf(banner);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    obs.SawBanner = true;
                    obs.LastBanner = text;
                    if (obs.BannerTexts.Count == 0 || obs.BannerTexts[^1] != text) obs.BannerTexts.Add(text);
                }
                if (!obs.SawStop)
                {
                    var stop = UiTestHelpers.FindAllSkippingGrid(s.Window, ControlType.Button)
                                            .Any(b => UiTestHelpers.SafeName(b).IndexOf("Stop", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (stop) obs.SawStop = true;
                }

                // 3. Done yet?
                var grid = UiTestHelpers.FindByIdSkippingGrid(s.Window, "ResultsGrid", 300);
                if (grid?.FindFirstDescendant(cf => cf.ByControlType(ControlType.DataItem)) != null)
                {
                    gridPopulated = true;
                    break;
                }
                Thread.Sleep(ProbeIntervalMs);
            }
            return obs;
        }

        /// <summary>The shared shape of every case: open it, watch it, then say what was wrong.</summary>
        private void AssertLoadsWithoutFreezing(string path, string what, bool expectProgressText = true)
        {
            if (!File.Exists(path)) Assert.Inconclusive($"fixture not present: {path}");
            var sizeMb = new FileInfo(path).Length / 1024.0 / 1024.0;
            TestContext.WriteLine($"{what}: {Path.GetFileName(path)} ({sizeMb:0.#} MB)");

            using var s = Launch(path);
            var started = DateTime.Now;
            var obs = WatchLoad(s, out var populated);
            var elapsed = (DateTime.Now - started).TotalSeconds;

            TestContext.WriteLine($"  loaded={populated} in {elapsed:0.#}s, {obs.Samples} probes, " +
                                  $"worst pump {obs.WorstPumpMs} ms ({obs.WorstPumpAt}), {obs.SamplesOverBudget} over budget");
            foreach (var t in obs.BannerTexts.Take(8)) TestContext.WriteLine($"  banner: {t}");

            // The window stayed alive throughout - the point of the whole exercise.
            Assert.IsTrue(obs.WorstPumpMs <= FreezeBudgetMs,
                $"the window stopped responding for {obs.WorstPumpMs} ms {obs.WorstPumpAt} while loading {what} " +
                $"(budget {FreezeBudgetMs} ms, {obs.SamplesOverBudget} of {obs.Samples} probes over it).");

            Assert.IsTrue(populated, $"{what} did not finish loading within {LoadTimeoutMs / 1000}s.");

            // The loading screen said something, and said it honestly.
            if (expectProgressText)
            {
                Assert.IsTrue(obs.SawBanner, $"no progress text appeared while loading {what} - the window just sat there.");
                Assert.IsTrue(obs.BannerTexts.Count >= 2,
                    $"the progress line never changed while loading {what} (stuck on '{obs.BannerTexts.FirstOrDefault()}') - " +
                    "a frozen message is indistinguishable from a frozen app.");
                Assert.IsFalse(obs.BannerTexts.Any(t => t.Contains("starting…", StringComparison.OrdinalIgnoreCase)
                                                        && obs.BannerTexts.Count(x => x == t) > 1),
                    "the progress line repeated a 'starting…' placeholder instead of advancing.");
                Assert.IsTrue(obs.SawStop, $"no Stop was offered while loading {what} - a long load must be abandonable.");
            }

            AssertViewerRenderedCorrectly(s, what);
        }

        /// <summary>After the load: the things a person looks at must all say something sensible.</summary>
        private void AssertViewerRenderedCorrectly(Session s, string what)
        {
            // Rows are actually on screen.
            var grid = UiTestHelpers.FindByIdSkippingGrid(s.Window, "ResultsGrid", 10000);
            Assert.IsNotNull(grid?.FindFirstDescendant(cf => cf.ByControlType(ControlType.DataItem)), $"no rows rendered for {what}.");

            // The pager knows how many there are.
            var (_, _, pager) = UiTestHelpers.ReadPager(s.Window);
            StringAssert.Contains(pager ?? "", "of", $"the pager did not render a total for {what} (got '{pager}').");
            var total = System.Text.RegularExpressions.Regex.Matches(pager ?? "", @"of\s+([\d,]+)");
            Assert.IsTrue(total.Count > 0 && long.Parse(total[^1].Groups[1].Value.Replace(",", "")) > 0,
                $"the pager total is not a number of rows for {what}: '{pager}'");

            // The workspace chip counts the source.
            var chip = TextOf(UiTestHelpers.FindByIdSkippingGrid(s.Window, "WorkspaceChipSummary", 5000));
            StringAssert.Contains(chip, "source", $"the workspace chip did not render for {what} (got '{chip}').");

            // The status strip's Last run segment is populated - it read blank in an earlier report.
            var strip = UiTestHelpers.FindAllSkippingGrid(s.Window, ControlType.Text).Select(UiTestHelpers.SafeName).ToList();
            Assert.IsTrue(strip.Any(t => t.Contains("result", StringComparison.OrdinalIgnoreCase)
                                      || t.Contains("cancelled", StringComparison.OrdinalIgnoreCase)),
                $"the status strip has no run summary after loading {what}.");

            // And the loading screen got out of the way.
            var banner = TextOf(UiTestHelpers.FindByIdSkippingGrid(s.Window, "ProgressBannerText", 1000));
            if (!string.IsNullOrWhiteSpace(banner))
                Assert.IsTrue(banner.Contains("index", StringComparison.OrdinalIgnoreCase),
                    $"the progress banner is still up after {what} finished, and it is not the index build: '{banner}'");
        }

        // ---- fixtures ----

        private static string LargeSample(string name)
            => Path.Combine(UiTestHelpers.RepoRoot(), "LargeSamples", name);

        /// <summary>A big plain-text log, generated once and kept in temp between runs (two minutes to
        /// write, so re-using it matters). Deliberately not in LargeSamples: it is derived, not data.</summary>
        private static string GeneratedTextLog(int lines)
        {
            var path = Path.Combine(Path.GetTempPath(), $"fn_stress_{lines}.log");
            if (File.Exists(path) && new FileInfo(path).Length > 0) return path;
            var start = new DateTime(2026, 1, 1, 0, 0, 0);
            using var sw = new StreamWriter(path, false, Encoding.ASCII, 1 << 20);
            for (int i = 0; i < lines; i++)
            {
                sw.Write('['); sw.Write(start.AddMilliseconds(i * 37).ToString("yyyy-MM-dd HH:mm:ss.fff")); sw.Write("] ");
                sw.Write(i % 23 == 0 ? "ERROR: subsystem alpha failed to answer, retrying request "
                                     : "INFO: subsystem bravo handled request ");
                sw.Write(i);
                sw.Write(" tid=");  sw.Write(i % 64);
                sw.Write(" ctx=");  sw.Write((i * 2654435761L) & 0xFFFFFF);
                sw.Write('\n');
            }
            return path;
        }

        // ---- the cases ----

        [TestMethod]
        [Timeout(20 * 60 * 1000)]
        public void LargeTextLog_LoadsWithoutFreezing()
            => AssertLoadsWithoutFreezing(GeneratedTextLog(2_000_000), "a 2M-line text log");

        [TestMethod]
        [Timeout(20 * 60 * 1000)]
        public void LargeEtl_LoadsWithoutFreezing()
            => AssertLoadsWithoutFreezing(LargeSample("large-5M.etl"), "a 5M-event ETL");

        [TestMethod]
        [Timeout(20 * 60 * 1000)]
        public void WppEtl_LoadsWithoutFreezing()
            => AssertLoadsWithoutFreezing(LargeSample("cats-wpp.etl"), "a WPP ETL");

        [TestMethod]
        [Timeout(20 * 60 * 1000)]
        public void LargeEvtx_LoadsWithoutFreezing()
            => AssertLoadsWithoutFreezing(LargeSample("large-app.evtx"), "a 21 MB event log");

        /// <summary>The archive case: one location, eighteen logs inside it, 7.4M rows - the load that
        /// produced both original reports.</summary>
        [TestMethod]
        [Timeout(30 * 60 * 1000)]
        public void MixedArchive_LoadsWithoutFreezing()
            => AssertLoadsWithoutFreezing(LargeSample("mixed-filter-fixture.zip"), "a mixed 7.4M-row archive");

        /// <summary>
        /// The state the viewer is actually left in after a big load is the state the NEXT thing
        /// happens in, so check the window survives a second heavy open on top of the first.
        /// </summary>
        [TestMethod]
        [Timeout(30 * 60 * 1000)]
        public void ASecondLargeOpen_OnTopOfTheFirst_StaysResponsive()
        {
            var first = GeneratedTextLog(2_000_000);
            var second = LargeSample("mixed-filter-fixture.zip");
            if (!File.Exists(second)) Assert.Inconclusive($"fixture not present: {second}");

            using var s = Launch(first);
            var obs1 = WatchLoad(s, out var populated1);
            Assert.IsTrue(populated1, "the first log did not load.");
            Assert.IsTrue(obs1.WorstPumpMs <= FreezeBudgetMs,
                $"the window froze for {obs1.WorstPumpMs} ms {obs1.WorstPumpAt} on the first load.");

            // Open the archive into the same window, the way Explorer would (single-instancing hands
            // the file to the running app), and watch the second load the same way.
            var psi = new ProcessStartInfo(UiTestHelpers.GetAppExecutablePath(), $"\"{second}\"") { UseShellExecute = false };
            psi.EnvironmentVariables["FINDNEEDLE_VIEWER_SETTINGS"] = s.SettingsPath;
            using (var forwarder = Process.Start(psi)) { forwarder?.WaitForExit(60_000); }

            var obs2 = WatchLoad(s, out var populated2);
            TestContext.WriteLine($"  second load: worst pump {obs2.WorstPumpMs} ms ({obs2.WorstPumpAt})");
            Assert.IsTrue(obs2.WorstPumpMs <= FreezeBudgetMs,
                $"the window stopped responding for {obs2.WorstPumpMs} ms {obs2.WorstPumpAt} during the second open.");
            Assert.IsTrue(populated2, "the second open never produced rows.");
            AssertViewerRenderedCorrectly(s, "the second open");
        }
    }
}
