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
        foreach (var d in _dirs)
            try { if (Directory.Exists(d)) Directory.Delete(d, recursive: true); } catch { }
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

    /// <summary>A folder "source" with a couple of log files. The cache signature must aggregate
    /// over its files so a folder gets the warm-cache fast path (the DISM Logs folder case).</summary>
    private string NewSourceFolder(params string[] fileContents)
    {
        var dir = Path.Combine(Path.GetTempPath(), "idxsrcdir_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        for (int i = 0; i < fileContents.Length; i++)
            File.WriteAllText(Path.Combine(dir, $"part{i}.log"), fileContents[i]);
        _dirs.Add(dir);
        _dbPaths.Add(CachedStorage.GetCacheFilePath(dir, ".db"));
        return dir;
    }

    private readonly List<string> _dirs = new();

    [TestMethod]
    public void Cache_FolderSource_ReusesWhenUnchanged_InvalidatesOnChange()
    {
        var dir = NewSourceFolder("alpha log line", "beta log line");
        const int sver = SqliteStorage.CacheSchemaVersion;

        // Build + stamp a cache keyed on the FOLDER path.
        using (var s = new SqliteStorage(dir))
        {
            s.ClearTables();
            s.AddFilteredBatch(MakeRows());
            s.WriteCompletionMetadata(dir, sver);
        }

        // Reopen unchanged → folder signature matches → reuse.
        using (var s = new SqliteStorage(dir))
            Assert.IsTrue(s.EvaluateCacheReuse(dir, sver), "unchanged folder should reuse the cache");

        // Grow one file → total size + mtime change → no reuse.
        File.AppendAllText(Path.Combine(dir, "part0.log"), " more bytes");
        using (var s = new SqliteStorage(dir))
            Assert.IsFalse(s.EvaluateCacheReuse(dir, sver), "a changed file must invalidate the folder cache");
    }

    [TestMethod]
    public void Cache_FolderSource_InvalidatesWhenFileAdded()
    {
        var dir = NewSourceFolder("only file");
        const int sver = SqliteStorage.CacheSchemaVersion;

        using (var s = new SqliteStorage(dir))
        {
            s.ClearTables();
            s.AddFilteredBatch(MakeRows());
            s.WriteCompletionMetadata(dir, sver);
        }
        using (var s = new SqliteStorage(dir))
            Assert.IsTrue(s.EvaluateCacheReuse(dir, sver));

        // A new (empty) file: total size + mtime could coincide, but file count changed → no reuse.
        File.WriteAllText(Path.Combine(dir, "added.log"), "");
        using (var s = new SqliteStorage(dir))
            Assert.IsFalse(s.EvaluateCacheReuse(dir, sver), "adding a file must invalidate the folder cache");
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

    [TestMethod]
    public void ShardedBuild_SearchMatchesAcrossShards()
    {
        var prev = SqliteStorage.FtsShardThreshold;
        SqliteStorage.FtsShardThreshold = 50; // force the parallel-shard path for this small set
        var searched = Path.Combine(Path.GetTempPath(), "shardtest_" + Guid.NewGuid().ToString("N"));
        var dbPath = CachedStorage.GetCacheFilePath(searched, ".db");
        _dbPaths.Add(dbPath);
        for (int k = 0; k < 8; k++) _dbPaths.Add(dbPath + $".fts{k}");
        try
        {
            using var s = new SqliteStorage(searched);
            var list = new List<ISearchResult>(400);
            for (int i = 0; i < 400; i++)
                list.Add(new R(i % 5 == 0 ? $"needle alpha row {i}" : $"hay bravo row {i}",
                               src: $"Provider{i % 3}", rs: $@"C:\logs\file{i % 7}.log"));
            s.AddFilteredBatch(list);
            s.BuildSearchIndex(); // 400 >= 50 → sharded path

            Assert.IsTrue(File.Exists(dbPath + ".fts0"), "sharded build should have created shard DB files");
            Assert.AreEqual(80, SearchCount(s, "needle"), "every 5th row → 80 matches via the sharded union");
            Assert.AreEqual(400, SearchCount(s, "row"), "every row contains 'row'");
            Assert.AreEqual(0, SearchCount(s, "zzznotpresent"), "absent term → 0");
            // Multi-source: Source/ResultSource are indexed in the shards too → path substrings searchable.
            Assert.IsTrue(SearchCount(s, "Provider1") > 0, "Source substring searchable across shards");
            Assert.IsTrue(SearchCount(s, "file3.log") > 0, "ResultSource substring searchable across shards");
        }
        finally { SqliteStorage.FtsShardThreshold = prev; }
    }

    [TestMethod]
    public void BuildSearchIndex_MultiSource_PathColumnsRemainSearchable()
    {
        using var s = NewSqlite();
        // >1 distinct Source AND ResultSource → both path columns ARE trigram-indexed → searchable.
        s.AddFilteredBatch(new List<ISearchResult>
        {
            new R("[2026-01-01 00:00:00] INFO: alpha", src: "ProviderAlpha", rs: @"C:\logs\alpha.log"),
            new R("[2026-01-01 00:00:00] INFO: bravo", src: "ProviderBravo", rs: @"C:\logs\bravo.log"),
        });
        s.BuildSearchIndex();

        Assert.AreEqual(1, SearchCount(s, "ProviderAlpha"), "Source substring must match when >1 distinct source");
        Assert.AreEqual(1, SearchCount(s, "bravo.log"), "ResultSource substring must match when >1 distinct result-source");
        Assert.AreEqual(2, SearchCount(s, "logs"), "shared path fragment matches both rows");
    }

    [TestMethod]
    public void BuildSearchIndex_SingleSource_MessageSearchUnaffected()
    {
        using var s = NewSqlite();
        // One distinct Source/ResultSource (the default) → those columns are blanked in the index to save
        // build cost, but message substring search is unaffected (the point: no useful search is lost).
        s.AddFilteredBatch(MakeRows());
        s.BuildSearchIndex();
        Assert.AreEqual(ExpectedNeedles, SearchCount(s, "needle"), "message search must work despite single-source path blanking");
        Assert.AreEqual(N, SearchCount(s, "row"));
    }

    private sealed class R : ISearchResult
    {
        private readonly string _m, _src, _rs;
        public R(string m, string src = "s", string rs = "rs") { _m = m; _src = src; _rs = rs; }
        public DateTime GetLogTime() => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public string GetMachineName() => "M";
        public void WriteToConsole() { }
        public Level GetLevel() => Level.Info;
        public string GetUsername() => "u";
        public string GetTaskName() => "t";
        public string GetOpCode() => "";
        public string GetSource() => _src;
        public string GetSearchableData() => _m;
        public string GetMessage() => _m;
        public string GetResultSource() => _rs;
    }
}
