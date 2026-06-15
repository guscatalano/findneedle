using Microsoft.VisualStudio.TestTools.UnitTesting;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace FindNeedleUX.UITests
{
    /// <summary>
    /// Characterizes the four storage paths end-to-end at scale, all on the same large data set:
    ///   1. Auto with a GOOD prediction  → should choose SQLite (lazy, low memory)
    ///   2. Auto with a BAD prediction   → chooses in-memory for a large set (works, but heavy)
    ///   3. Hybrid (forced)
    ///   4. SQLite (forced)
    ///
    /// Each scenario launches FindNeedleUX with a log via the command line plus a --storage / --estimate
    /// override, lets the real pipeline run, verifies it paginates to the end, and reads the structured
    /// performance report (last-search-report.json) to assert which storage tier was actually chosen and
    /// to surface the load-time breakdown. Manual lane only (interactive desktop; heavy).
    /// </summary>
    [TestClass]
    [TestCategory("UITests")]
    [TestCategory("SkipCI")]
    [TestCategory("Performance")]
    public class StorageBackendScaleUITests
    {
        // Above Auto's 50k SQLite threshold, but small enough that the SQLite/Hybrid FTS-index build
        // (which costs ~200s for 1M rows!) finishes quickly across four launches. In-memory scans this
        // in ~0.3s; SQLite/Hybrid take tens of seconds — the contrast is the whole point.
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
            _tempLogPath = UiTestHelpers.WriteBracketedLog(RowCount, "findneedle_storage");
            context.WriteLine($"Generated {RowCount:N0}-line log: {_tempLogPath}");
        }

        [ClassCleanup]
        public static void TearDown()
        {
            try { _automation?.Dispose(); } catch { }
            try { if (_tempLogPath != null && File.Exists(_tempLogPath)) File.Delete(_tempLogPath); } catch { }
        }

        /// <summary>
        /// Launch the app with the given storage override, verify the data paginates to the end, read the
        /// JSON perf report, assert the tier matches, and log the breakdown.
        /// </summary>
        private void RunScenario(string storageArg, int? estimate, string expectedStorageType)
        {
            // Start from a clean report so we read this run's, not a stale one.
            try { if (File.Exists(ReportPath)) File.Delete(ReportPath); } catch { }

            // --cache=off so each scenario is a clean scan (no cache-hit skew between the two SQLite runs).
            // --indexing=eager so the FTS index is built during the search (deterministic for the assertions).
            var args = $"\"{_tempLogPath}\" --viewer=native --storage={storageArg} --cache=off --indexing=eager";
            if (estimate.HasValue) args += $" --estimate={estimate.Value}";

            var app = Application.Launch(UiTestHelpers.GetAppExecutablePath(), args);
            try
            {
                Thread.Sleep(3000);
                var window = app.GetMainWindow(_automation);
                Assert.IsNotNull(window, "Failed to get main window");

                var loadSw = Stopwatch.StartNew();
                var grid = UiTestHelpers.WaitForPopulatedGrid(window, 300000); // 5 min (SQLite FTS build is slow)
                loadSw.Stop();
                Assert.IsNotNull(grid, $"[{storageArg}] grid never populated.");

                // Pagination still works on this backend: first page starts at row 1, last page ends at RowCount.
                Assert.IsTrue(UiTestHelpers.ClickPagerButton(window, "First"), $"[{storageArg}] no First button.");
                Thread.Sleep(800);
                var first = UiTestHelpers.ReadPager(window);
                Assert.AreEqual(1, first.start, $"[{storageArg}] first page should start at row 1 (pager '{first.raw}').");

                Assert.IsTrue(UiTestHelpers.ClickPagerButton(window, "Last"), $"[{storageArg}] no Last button.");
                Thread.Sleep(1500);
                var last = UiTestHelpers.ReadPager(window);
                Assert.AreEqual(RowCount, last.end, $"[{storageArg}] last page should end at row {RowCount:N0} (pager '{last.raw}').");

                // Read the structured perf report and assert the storage tier actually chosen.
                var report = ReadReport(15000);
                Assert.IsNotNull(report, $"[{storageArg}] perf report JSON was not written.");
                var actualType = report.Value.GetProperty("StorageType").GetString();
                Assert.AreEqual(expectedStorageType, actualType,
                    $"[{storageArg} est={estimate}] expected storage {expectedStorageType} but report says {actualType}.");

                long searchMs = report.Value.GetProperty("SearchMs").GetInt64();
                long viewerMs = report.Value.GetProperty("ViewerMs").GetInt64();
                long stored = report.Value.GetProperty("StoredRows").GetInt64();
                long fallback = GetPhaseMs(report.Value, "viewer.native.getloglines_fallback");
                TestContext.WriteLine(
                    $"{storageArg,-8} est={(estimate?.ToString() ?? "-"),-8} -> {actualType,-16} | " +
                    $"search {searchMs,6:N0}ms · viewer {viewerMs,6:N0}ms (materialize {fallback,6:N0}ms) | " +
                    $"stored {stored:N0} | load {loadSw.ElapsedMilliseconds:N0}ms");
            }
            finally
            {
                try { if (!app.HasExited) app.Close(); } catch { }
                try { if (!app.HasExited) app.Kill(); } catch { }
                try { app.Dispose(); } catch { }
                Thread.Sleep(1000); // let the process release the log file before the next launch
            }
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

        private static long GetPhaseMs(JsonElement report, string phaseName)
        {
            if (!report.TryGetProperty("Phases", out var phases) || phases.ValueKind != JsonValueKind.Array) return 0;
            long max = 0;
            foreach (var p in phases.EnumerateArray())
                if (p.GetProperty("Name").GetString() == phaseName)
                    max = Math.Max(max, p.GetProperty("ElapsedMs").GetInt64());
            return max;
        }

        [TestMethod]
        [Timeout(360000)]
        public void Auto_GoodPrediction_ChoosesSqlite()
            => RunScenario("auto", estimate: RowCount, expectedStorageType: "SqliteStorage");

        [TestMethod]
        [Timeout(360000)]
        public void Auto_BadPrediction_FallsBackToInMemory()
            => RunScenario("auto", estimate: 1000, expectedStorageType: "InMemoryStorage");

        [TestMethod]
        [Timeout(360000)]
        public void Hybrid_Forced_Works()
            => RunScenario("hybrid", estimate: null, expectedStorageType: "HybridStorage");

        [TestMethod]
        [Timeout(360000)]
        public void Sqlite_Forced_Works()
            => RunScenario("sqlite", estimate: null, expectedStorageType: "SqliteStorage");
    }
}
