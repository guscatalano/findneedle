using Microsoft.VisualStudio.TestTools.UnitTesting;
using FlaUI.Core;
using FlaUI.UIA3;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace FindNeedleUX.UITests
{
    /// <summary>
    /// Measures how much of the SQLite ingest cost is the FTS5 trigram index build. Runs the SAME
    /// data through the SAME SQLite path twice — once normally (index built via the per-row AFTER
    /// INSERT trigger) and once with the index disabled (FINDNEEDLE_DISABLE_FTS=1) — and compares the
    /// search/scan time from the structured perf report. The delta is the FTS build cost.
    /// Manual lane (interactive desktop; heavy).
    /// </summary>
    [TestClass]
    [TestCategory("UITests")]
    [TestCategory("SkipCI")]
    [TestCategory("Performance")]
    public class FtsImpactUITests
    {
        private const int RowCount = 200_000;

        private static string _tempLogPath;
        private static UIA3Automation _automation;

        private static readonly string ReportPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FindNeedle", "last-search-report.json");

        public TestContext TestContext { get; set; }

        [ClassInitialize]
        public static void Setup(TestContext context)
        {
            _automation = new UIA3Automation();
            _tempLogPath = UiTestHelpers.WriteBracketedLog(RowCount, "findneedle_fts");
            context.WriteLine($"Generated {RowCount:N0}-line log: {_tempLogPath}");
        }

        [ClassCleanup]
        public static void TearDown()
        {
            try { _automation?.Dispose(); } catch { }
            try { if (_tempLogPath != null && File.Exists(_tempLogPath)) File.Delete(_tempLogPath); } catch { }
        }

        private readonly struct RunResult
        {
            public RunResult(bool ftsBuilt, long search, long viewer, long load)
            { FtsBuilt = ftsBuilt; SearchMs = search; ViewerMs = viewer; LoadMs = load; }
            public bool FtsBuilt { get; }
            public long SearchMs { get; }
            public long ViewerMs { get; }
            public long LoadMs { get; }
        }

        /// <summary>Run one SQLite search of the shared file, optionally disabling the FTS index build.</summary>
        private RunResult RunOnce(bool disableFts)
        {
            try { if (File.Exists(ReportPath)) File.Delete(ReportPath); } catch { }

            // Use ProcessStartInfo so we can inject the FINDNEEDLE_DISABLE_FTS environment variable
            // for the no-index run. --cache=off keeps both runs as clean, comparable fresh scans.
            var psi = new ProcessStartInfo(UiTestHelpers.GetAppExecutablePath())
            {
                Arguments = $"\"{_tempLogPath}\" --viewer=native --storage=sqlite --cache=off --indexing=eager",
                UseShellExecute = false,
            };
            if (disableFts) psi.Environment["FINDNEEDLE_DISABLE_FTS"] = "1";

            var app = Application.Launch(psi);
            try
            {
                Thread.Sleep(3000);
                var window = app.GetMainWindow(_automation);
                Assert.IsNotNull(window, "Failed to get main window");

                var loadSw = Stopwatch.StartNew();
                var grid = UiTestHelpers.WaitForPopulatedGrid(window, 300000);
                loadSw.Stop();
                Assert.IsNotNull(grid, $"[disableFts={disableFts}] grid never populated.");

                Assert.IsTrue(UiTestHelpers.ClickPagerButton(window, "Last"), $"[disableFts={disableFts}] no Last button.");
                Thread.Sleep(1200);
                var last = UiTestHelpers.ReadPager(window);
                Assert.AreEqual(RowCount, last.end, $"[disableFts={disableFts}] last page should end at {RowCount:N0} (pager '{last.raw}').");

                var report = ReadReport(15000);
                Assert.IsNotNull(report, $"[disableFts={disableFts}] perf report JSON was not written.");
                var r = report.Value;
                Assert.AreEqual("SqliteStorage", r.GetProperty("StorageType").GetString(),
                    $"[disableFts={disableFts}] expected SQLite storage.");
                return new RunResult(
                    r.GetProperty("FtsIndexBuilt").GetBoolean(),
                    r.GetProperty("SearchMs").GetInt64(),
                    r.GetProperty("ViewerMs").GetInt64(),
                    loadSw.ElapsedMilliseconds);
            }
            finally
            {
                try { if (!app.HasExited) app.Close(); } catch { }
                try { if (!app.HasExited) app.Kill(); } catch { }
                try { app.Dispose(); } catch { }
                Thread.Sleep(1500);
            }
        }

        [TestMethod]
        [Timeout(360000)]
        public void FtsIndexBuild_Impact()
        {
            var withFts = RunOnce(disableFts: false);
            Assert.IsTrue(withFts.FtsBuilt, "FTS index should have been built on the normal run.");

            var noFts = RunOnce(disableFts: true);
            Assert.IsFalse(noFts.FtsBuilt, "FTS index should NOT have been built when disabled.");

            long delta = withFts.SearchMs - noFts.SearchMs;
            double pctOfScan = withFts.SearchMs > 0 ? 100.0 * delta / withFts.SearchMs : 0;
            TestContext.WriteLine($"SQLite scan of {RowCount:N0} rows:");
            TestContext.WriteLine($"   with FTS : search {withFts.SearchMs,7:N0} ms   (viewer {withFts.ViewerMs} ms)");
            TestContext.WriteLine($"   no   FTS : search {noFts.SearchMs,7:N0} ms   (viewer {noFts.ViewerMs} ms)");
            TestContext.WriteLine($"   FTS build cost: {delta:N0} ms  ({pctOfScan:F0}% of the scan)");
            TestContext.WriteLine(delta <= 0
                ? "   => FTS is NOT the bottleneck — the cost is in the row-insert path itself."
                : $"   => FTS adds ~{pctOfScan:F0}% to the scan.");

            // This is a measurement, not a hypothesis test: assert only the deterministic facts
            // (the toggle worked and both runs ingested all rows). The timing delta is logged above
            // for the human — run-to-run variance makes a direction assertion flaky, and the whole
            // point of this run was to learn that FTS is roughly free here.
        }

        private static JsonElement? ReadReport(int timeoutMs)
        {
            var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < deadline)
            {
                try
                {
                    if (File.Exists(ReportPath))
                    {
                        var json = File.ReadAllText(ReportPath);
                        if (!string.IsNullOrWhiteSpace(json))
                            return JsonDocument.Parse(json).RootElement.Clone();
                    }
                }
                catch { /* mid-write — retry */ }
                Thread.Sleep(300);
            }
            return null;
        }
    }
}
