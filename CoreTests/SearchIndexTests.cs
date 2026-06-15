using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using FindPluginCore.Implementations.Storage;
using FindNeedleCoreUtils;
using FindNeedlePluginLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

/// <summary>
/// Correctness tests for the batched, cancellable FTS index build (BuildSearchIndex) and the
/// build-time predictor. Substring search must return the same results whether the index was built,
/// cancelled (LIKE fallback), or never built.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SearchIndexTests
{
    private const int N = 60_000;          // 60k → a couple of 50k batches; fast
    private const int NeedleEvery = 3;     // every 3rd row contains "needle"
    private static readonly int ExpectedNeedles = (N + NeedleEvery - 1) / NeedleEvery;

    private readonly List<string> _dbPaths = new();

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var p in _dbPaths)
            try { if (File.Exists(p)) File.Delete(p); } catch { }
    }

    private SqliteStorage NewSqlite()
    {
        var searchedFile = Path.Combine(Path.GetTempPath(), "idxtest_" + Guid.NewGuid().ToString("N"));
        _dbPaths.Add(CachedStorage.GetCacheFilePath(searchedFile, ".db"));
        return new SqliteStorage(searchedFile);
    }

    private static List<ISearchResult> MakeRows()
    {
        var list = new List<ISearchResult>(N);
        for (int i = 0; i < N; i++)
            list.Add(new R(i % NeedleEvery == 0
                ? $"[2026-01-01 00:00:00] INFO: needle row {i}"
                : $"[2026-01-01 00:00:00] INFO: hay row {i}"));
        return list;
    }

    private static int SearchCount(SqliteStorage s, string term)
        => s.GetFilteredCount(new SqliteStorage.FilterInput { Search = term });

    /// <summary>A real on-disk "source" file (cache reuse stats its size/mtime) + tracked db path.</summary>
    private string NewSourceFile()
    {
        var f = Path.Combine(Path.GetTempPath(), "idxsrc_" + Guid.NewGuid().ToString("N") + ".log");
        File.WriteAllText(f, "source-for-cache-eval");
        _dbPaths.Add(f);
        _dbPaths.Add(CachedStorage.GetCacheFilePath(f, ".db"));
        return f;
    }

    [TestMethod]
    public void Cache_FtsBuiltFlag_RoundTrips_AcrossReopen()
    {
        var src = NewSourceFile();
        const int sver = SqliteStorage.CacheSchemaVersion;

        // Deferred run: ingest rows, do NOT build the index, stamp the cache (fts_built=0).
        using (var s = new SqliteStorage(src))
        {
            s.ClearTables();
            s.AddFilteredBatch(MakeRows());
            s.WriteCompletionMetadata(src, sver);
        }

        // Reopen: cache is reusable (rows complete) but the index isn't built → search via LIKE.
        using (var s = new SqliteStorage(src))
        {
            Assert.IsTrue(s.EvaluateCacheReuse(src, sver), "complete cache should be reusable");
            Assert.IsFalse(s.IsSearchIndexBuilt, "deferred run wrote fts_built=0");
            Assert.AreEqual(ExpectedNeedles, SearchCount(s, "needle"), "search correct via LIKE fallback");
            s.BuildSearchIndex();                 // lazy/background build now
            s.WriteCompletionMetadata(src, sver); // persist fts_built=1
        }

        // Reopen again: the built index is recognized and reused (no rebuild).
        using (var s = new SqliteStorage(src))
        {
            Assert.IsTrue(s.EvaluateCacheReuse(src, sver));
            Assert.IsTrue(s.IsSearchIndexBuilt, "fts_built=1 should round-trip through the cache");
            Assert.AreEqual(ExpectedNeedles, SearchCount(s, "needle"));
        }
    }

    [TestMethod]
    public void BuildSearchIndex_Batched_ReportsProgress_AndSearchIsCorrect()
    {
        using var s = NewSqlite();
        s.AddFilteredBatch(MakeRows());

        var progress = new List<(long indexed, long total)>();
        s.BuildSearchIndex(CancellationToken.None, (i, t) => progress.Add((i, t)));

        Assert.IsTrue(progress.Count >= 1, "progress should be reported at least once");
        Assert.AreEqual((long)N, progress[^1].indexed, "final progress should reach all rows");
        Assert.AreEqual((long)N, progress[^1].total, "total should be the row count");
        // Monotonic non-decreasing progress.
        for (int k = 1; k < progress.Count; k++)
            Assert.IsTrue(progress[k].indexed >= progress[k - 1].indexed, "progress must not go backwards");

        Assert.AreEqual(ExpectedNeedles, SearchCount(s, "needle"), "substring search after build");
        Assert.AreEqual(N, SearchCount(s, "row"), "every row contains 'row'");
    }

    [TestMethod]
    public void BuildSearchIndex_Cancelled_SearchStillCorrectViaFallback()
    {
        using var s = NewSqlite();
        s.AddFilteredBatch(MakeRows());

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // cancel before any batch runs
        s.BuildSearchIndex(cts.Token);

        // Index wasn't built, so search uses the LIKE fallback — still correct.
        Assert.AreEqual(ExpectedNeedles, SearchCount(s, "needle"), "search must work via fallback when index cancelled");
    }

    [TestMethod]
    public void BuildSearchIndex_BeforeBuild_SearchWorksViaFallback()
    {
        using var s = NewSqlite();
        s.AddFilteredBatch(MakeRows());
        // No BuildSearchIndex call at all → LIKE fallback.
        Assert.AreEqual(ExpectedNeedles, SearchCount(s, "needle"));
    }

    [TestMethod]
    public void PredictIndexBuildMs_IsMonotonicAndReasonable()
    {
        Assert.IsTrue(SqliteStorage.PredictIndexBuildMs(200_000) < 30_000, "200k should predict under 30s");
        Assert.IsTrue(SqliteStorage.PredictIndexBuildMs(5_000_000) > 120_000, "5M should predict over 2 minutes");
        Assert.IsTrue(SqliteStorage.PredictIndexBuildMs(2_000_000) > SqliteStorage.PredictIndexBuildMs(1_000_000),
            "prediction must increase with row count");
        Assert.AreEqual(0, SqliteStorage.PredictIndexBuildMs(0));
    }

    private sealed class R : ISearchResult
    {
        private readonly string _m;
        public R(string m) { _m = m; }
        public DateTime GetLogTime() => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public string GetMachineName() => "M";
        public void WriteToConsole() { }
        public Level GetLevel() => Level.Info;
        public string GetUsername() => "u";
        public string GetTaskName() => "t";
        public string GetOpCode() => "";
        public string GetSource() => "s";
        public string GetSearchableData() => _m;
        public string GetMessage() => _m;
        public string GetResultSource() => "rs";
    }
}
