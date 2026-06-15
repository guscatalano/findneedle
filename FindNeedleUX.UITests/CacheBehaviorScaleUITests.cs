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
    /// Exercises the on-disk result cache end-to-end against the same file, in order:
    ///   1. FRESH        — first search of a new file: cache miss, full scan, results written to cache.
    ///   2. CACHE HIT    — same file again: cache reused, the read/parse/index scan is skipped (fast).
    ///   3. CACHE OFF    — same file with caching disabled: cache ignored AND not written ("no moving
    ///                     to cache"), so it rescans from scratch.
    ///
    /// Caching only applies to single-file SQLite searches, so every phase forces --storage=sqlite.
    /// State carries between phases via the on-disk cache, so this is one ordered test rather than
    /// three independently-ordered methods. Reads the structured perf report (last-search-report.json)
    /// to assert cache behaviour and compare scan times. Manual lane (interactive desktop; heavy).
    /// </summary>
    [TestClass]
    [TestCategory("UITests")]
    [TestCategory("SkipCI")]
    [TestCategory("Performance")]
    public class CacheBehaviorScaleUITests
    {
        // SQLite scan + FTS index build is slow (~200s/1M), and fresh+disabled each pay it, so keep the
        // dataset modest. Still plenty to make a cache HIT visibly faster than a fresh scan.
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
            // A brand-new file name guarantees phase 1 is genuinely a cache MISS.
            _tempLogPath = UiTestHelpers.WriteBracketedLog(RowCount, "findneedle_cache");
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
            public RunResult(bool hit, bool written, string skip, long search, long viewer, long load)
            { CacheHit = hit; CacheWritten = written; CacheWriteSkipReason = skip; SearchMs = search; ViewerMs = viewer; LoadMs = load; }
            public bool CacheHit { get; }
            public bool CacheWritten { get; }
            public string CacheWriteSkipReason { get; }
            public long SearchMs { get; }
            public long ViewerMs { get; }
            public long LoadMs { get; }
        }

        /// <summary>Launch one search of the shared file (always SQLite) with the given cache mode; return its report.</summary>
        private RunResult RunOnce(string cacheArg)
        {
            try { if (File.Exists(ReportPath)) File.Delete(ReportPath); } catch { }

            var args = $"\"{_tempLogPath}\" --viewer=native --storage=sqlite --cache={cacheArg}";
            var app = Application.Launch(UiTestHelpers.GetAppExecutablePath(), args);
            try
            {
                Thread.Sleep(3000);
                var window = app.GetMainWindow(_automation);
                Assert.IsNotNull(window, "Failed to get main window");

                var loadSw = Stopwatch.StartNew();
                var grid = UiTestHelpers.WaitForPopulatedGrid(window, 300000);
                loadSw.Stop();
                Assert.IsNotNull(grid, $"[cache={cacheArg}] grid never populated.");

                // Sanity: the data is actually there regardless of cache path.
                Assert.IsTrue(UiTestHelpers.ClickPagerButton(window, "Last"), $"[cache={cacheArg}] no Last button.");
                Thread.Sleep(1200);
                var last = UiTestHelpers.ReadPager(window);
                Assert.AreEqual(RowCount, last.end, $"[cache={cacheArg}] last page should end at {RowCount:N0} (pager '{last.raw}').");

                var report = ReadReport(15000);
                Assert.IsNotNull(report, $"[cache={cacheArg}] perf report JSON was not written.");
                var r = report.Value;
                return new RunResult(
                    r.GetProperty("CacheHit").GetBoolean(),
                    r.GetProperty("CacheWritten").GetBoolean(),
                    r.GetProperty("CacheWriteSkipReason").GetString() ?? "",
                    r.GetProperty("SearchMs").GetInt64(),
                    r.GetProperty("ViewerMs").GetInt64(),
                    loadSw.ElapsedMilliseconds);
            }
            finally
            {
                try { if (!app.HasExited) app.Close(); } catch { }
                try { if (!app.HasExited) app.Kill(); } catch { }
                try { app.Dispose(); } catch { }
                Thread.Sleep(1500); // release the file + flush the cache DB before the next launch
            }
        }

        [TestMethod]
        [Timeout(600000)]
        public void Cache_Fresh_Then_Hit_Then_Disabled()
        {
            // 1) FRESH — first time we've seen this file: must be a miss, and it should write the cache.
            var fresh = RunOnce("on");
            TestContext.WriteLine($"FRESH   : hit={fresh.CacheHit} written={fresh.CacheWritten} skip='{fresh.CacheWriteSkipReason}' search={fresh.SearchMs:N0}ms load={fresh.LoadMs:N0}ms");
            Assert.IsFalse(fresh.CacheHit, "First run of a new file must be a cache MISS.");
            Assert.IsTrue(fresh.CacheWritten, "A fresh SQLite scan should write the cache for next time.");

            // 2) CACHE HIT — same file again: must reuse the cache and skip the scan (much faster).
            var hit = RunOnce("on");
            TestContext.WriteLine($"HIT     : hit={hit.CacheHit} written={hit.CacheWritten} skip='{hit.CacheWriteSkipReason}' search={hit.SearchMs:N0}ms load={hit.LoadMs:N0}ms");
            Assert.IsTrue(hit.CacheHit, "Second run of the same unchanged file should be a cache HIT.");
            Assert.IsTrue(hit.SearchMs < fresh.SearchMs,
                $"Cache hit should skip the scan and be faster (hit {hit.SearchMs}ms vs fresh {fresh.SearchMs}ms).");

            // 3) CACHE OFF — caching disabled: ignore the existing cache (rescan) and don't write one.
            var off = RunOnce("off");
            TestContext.WriteLine($"DISABLED: hit={off.CacheHit} written={off.CacheWritten} skip='{off.CacheWriteSkipReason}' search={off.SearchMs:N0}ms load={off.LoadMs:N0}ms");
            Assert.IsFalse(off.CacheHit, "With caching disabled the existing cache must NOT be reused.");
            Assert.IsFalse(off.CacheWritten, "With caching disabled, results must NOT be moved to the cache.");
            Assert.AreEqual("disabled", off.CacheWriteSkipReason, "Cache write should be skipped with reason 'disabled'.");
            Assert.IsTrue(off.SearchMs > hit.SearchMs,
                $"Disabled cache must rescan, so it's slower than a hit (off {off.SearchMs}ms vs hit {hit.SearchMs}ms).");
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
