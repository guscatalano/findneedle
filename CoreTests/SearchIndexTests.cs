using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>
    /// The sharded build must report progress WHILE it works, not once per shard at commit time. It
    /// used to do the latter, which left the viewer's "Building search index…" indicator sitting on
    /// "starting…" for the whole 89-second build of a 7.4M-row log - indistinguishable from a hang.
    /// </summary>
    [TestMethod]
    public void ShardedBuild_ReportsProgressWhileItRuns()
    {
        var prevThreshold = SqliteStorage.FtsShardThreshold;
        var prevReport = SqliteStorage.IndexProgressReportRows;
        SqliteStorage.FtsShardThreshold = 50;   // force the sharded path
        SqliteStorage.IndexProgressReportRows = 10; // report often enough to see it move on a small set
        var src = NewSourceFile();
        var dbPath = CachedStorage.GetCacheFilePath(src, ".db");
        for (int k = 0; k < 8; k++) _dbPaths.Add(dbPath + $".fts{k}");
        try
        {
            using var s = new SqliteStorage(src);
            s.ClearTables();
            var list = new List<ISearchResult>(400);
            for (int i = 0; i < 400; i++) list.Add(new R($"row {i}", src: $"Prov{i % 3}", rs: $@"C:\l{i % 4}.log"));
            s.AddFilteredBatch(list);

            var reports = new System.Collections.Concurrent.ConcurrentBag<(long done, long total)>();
            s.BuildSearchIndex(default, (done, total) => reports.Add((done, total)));

            var all = reports.ToList();
            Assert.IsTrue(all.Count > 8, $"progress should be reported as rows are indexed, not once per shard (got {all.Count} for 8 shards)");
            Assert.IsTrue(all.All(r => r.total == 400), "every report carries the row total");
            Assert.IsTrue(all.Any(r => r.done > 0 && r.done < 400), "at least one report lands mid-build, so the indicator moves");
            Assert.AreEqual(400, all.Max(r => r.done), "the last report accounts for every row exactly once");
        }
        finally
        {
            SqliteStorage.FtsShardThreshold = prevThreshold;
            SqliteStorage.IndexProgressReportRows = prevReport;
        }
    }

    /// <summary>
    /// The checkpoint window has to adapt to the shard, not just sit at its cap: a shard smaller than
    /// the cap would commit only at the very end, which is how an 800k-row log (100k per shard against
    /// a 250k cap) still lost everything to a kill.
    /// </summary>
    [TestMethod]
    public void CheckpointWindow_AdaptsToTheShardSize()
    {
        var cap = SqliteStorage.IndexShardCheckpointRows;
        Assert.AreEqual(cap, SqliteStorage.CheckpointRowsFor(40_000_000 / 8), "a huge shard stays at the cap");
        Assert.IsTrue(SqliteStorage.CheckpointRowsFor(100_000) < 100_000,
            "a shard smaller than the cap must still checkpoint on the way through");
        Assert.AreEqual(25_000, SqliteStorage.CheckpointRowsFor(100_000), "roughly four checkpoints per shard");
        Assert.IsTrue(SqliteStorage.CheckpointRowsFor(30_000) >= 20_000, "with a floor, so a small shard is not all commits");
        Assert.AreEqual(cap, SqliteStorage.CheckpointRowsFor(5_000), "and a tiny shard just commits once at the end");
    }

    /// <summary>
    /// A cancelled build keeps what it finished. Before this, all eight shards committed only at the
    /// very end, so closing the app during an 89-second build on a 7.4M-row log threw the lot away and
    /// the next open started from zero. Now each shard checkpoints as it goes and the next build
    /// resumes - and, the part that actually matters, the resumed index finds every row.
    /// </summary>
    [TestMethod]
    public void ShardedBuild_Cancelled_ResumesAndStillFindsEverything()
    {
        var prevThreshold = SqliteStorage.FtsShardThreshold;
        var prevCheckpoint = SqliteStorage.IndexShardCheckpointRows;
        SqliteStorage.FtsShardThreshold = 50;        // force the sharded path
        SqliteStorage.IndexShardCheckpointRows = 5;  // checkpoint often so a cancel lands mid-shard
        var src = NewSourceFile();
        var dbPath = CachedStorage.GetCacheFilePath(src, ".db");
        for (int k = 0; k < 8; k++) _dbPaths.Add(dbPath + $".fts{k}");
        try
        {
            using var s = new SqliteStorage(src);
            s.ClearTables();
            var list = new List<ISearchResult>(600);
            for (int i = 0; i < 600; i++)
                list.Add(new R(i % 5 == 0 ? $"needle row {i}" : $"hay row {i}", src: $"Prov{i % 3}"));
            s.AddFilteredBatch(list);

            // Cancel partway: stop once some rows are in, mid-shard.
            var cts = new CancellationTokenSource();
            long seen = 0;
            s.BuildSearchIndex(cts.Token, (done, _) => { seen = done; if (done >= 40) cts.Cancel(); });
            Assert.IsFalse(s.IsSearchIndexBuilt, "a cancelled build must not claim to have an index");
            Assert.IsTrue(seen > 0, "the cancelled build indexed something");
            Assert.IsTrue(File.Exists(dbPath + ".fts0"), "the shard files it finished are kept, not deleted");

            // Resume: the second build picks up the checkpoints and completes.
            long firstReport = -1;
            s.BuildSearchIndex(default, (done, _) => { if (firstReport < 0) firstReport = done; });
            Assert.IsTrue(s.IsSearchIndexBuilt, "the resumed build completes the index");
            Assert.IsTrue(firstReport > 0, $"the resumed build starts from the rows already indexed, not from 0 (first report: {firstReport})");

            // The point of all of it: search is correct over the resumed index.
            Assert.AreEqual(120, SearchCount(s, "needle"), "every needle row is findable after resuming");
            Assert.AreEqual(480, SearchCount(s, "hay"), "and every hay row too - no rows dropped at the seam");
            Assert.AreEqual(1, SearchCount(s, "needle row 355".Replace("355", "5")) > 0 ? 1 : 0, "a specific row still matches");
        }
        finally
        {
            SqliteStorage.FtsShardThreshold = prevThreshold;
            SqliteStorage.IndexShardCheckpointRows = prevCheckpoint;
        }
    }

    /// <summary>
    /// The real shape of a resume: a session indexes part of a log and EXITS (its storage disposed,
    /// its connections gone), then a new session opens the same cache and carries on. This is what
    /// happens when someone closes the app during a long build, and it is where the first cut fell
    /// over with "database is locked".
    /// </summary>
    [TestMethod]
    public void Resume_AcrossSessions_CompletesAndSearchesCorrectly()
    {
        var prevThreshold = SqliteStorage.FtsShardThreshold;
        var prevCheckpoint = SqliteStorage.IndexShardCheckpointRows;
        SqliteStorage.FtsShardThreshold = 50;
        SqliteStorage.IndexShardCheckpointRows = 5;
        var src = NewSourceFile();
        var dbPath = CachedStorage.GetCacheFilePath(src, ".db");
        for (int k = 0; k < 8; k++) _dbPaths.Add(dbPath + $".fts{k}");
        const int sver = SqliteStorage.CacheSchemaVersion;
        try
        {
            // Session one: ingest, start indexing, get interrupted, close.
            using (var s1 = new SqliteStorage(src))
            {
                s1.ClearTables();
                var list = new List<ISearchResult>(600);
                for (int i = 0; i < 600; i++)
                    list.Add(new R(i % 5 == 0 ? $"needle row {i}" : $"hay row {i}", src: $"Prov{i % 3}"));
                s1.AddFilteredBatch(list);
                s1.WriteCompletionMetadata(src, sver);
                var cts = new CancellationTokenSource();
                s1.BuildSearchIndex(cts.Token, (done, _) => { if (done >= 40) cts.Cancel(); });
                Assert.IsFalse(s1.IsSearchIndexBuilt, "interrupted, so no index yet");
            }

            // Session two: same cache, finish the job.
            using (var s2 = new SqliteStorage(src))
            {
                Assert.IsTrue(s2.EvaluateCacheReuse(src, sver), "the rows are still cached");
                Assert.IsFalse(s2.IsSearchIndexBuilt, "the interrupted index is not claimed as built");
                s2.BuildSearchIndex();
                Assert.IsTrue(s2.IsSearchIndexBuilt, "the second session completes the index");
                Assert.AreEqual(120, SearchCount(s2, "needle"), "every needle row is findable");
                Assert.AreEqual(480, SearchCount(s2, "hay"), "and every hay row");
                s2.WriteCompletionMetadata(src, sver);
            }

            // Session three: nothing left to build.
            using (var s3 = new SqliteStorage(src))
            {
                Assert.IsTrue(s3.EvaluateCacheReuse(src, sver));
                Assert.IsTrue(s3.IsSearchIndexBuilt, "a completed index is reused, not rebuilt");
                Assert.AreEqual(120, SearchCount(s3, "needle"));
            }
        }
        finally
        {
            SqliteStorage.FtsShardThreshold = prevThreshold;
            SqliteStorage.IndexShardCheckpointRows = prevCheckpoint;
        }
    }

    /// <summary>A wiped cache must not leave checkpoints behind that point at deleted shard files.</summary>
    [TestMethod]
    public void ClearingTheCache_ForgetsShardCheckpoints()
    {
        var prevThreshold = SqliteStorage.FtsShardThreshold;
        var prevCheckpoint = SqliteStorage.IndexShardCheckpointRows;
        SqliteStorage.FtsShardThreshold = 50;
        SqliteStorage.IndexShardCheckpointRows = 5;
        var src = NewSourceFile();
        var dbPath = CachedStorage.GetCacheFilePath(src, ".db");
        for (int k = 0; k < 8; k++) _dbPaths.Add(dbPath + $".fts{k}");
        try
        {
            using var s = new SqliteStorage(src);
            s.ClearTables();
            var list = new List<ISearchResult>(300);
            for (int i = 0; i < 300; i++) list.Add(new R($"alpha row {i}"));
            s.AddFilteredBatch(list);
            var cts = new CancellationTokenSource();
            s.BuildSearchIndex(cts.Token, (done, _) => { if (done >= 20) cts.Cancel(); });

            // Wipe and refill with different content: the stale checkpoints must not be trusted.
            s.ClearTables();
            var list2 = new List<ISearchResult>(300);
            for (int i = 0; i < 300; i++) list2.Add(new R($"bravo row {i}"));
            s.AddFilteredBatch(list2);
            s.BuildSearchIndex();

            Assert.IsTrue(s.IsSearchIndexBuilt);
            Assert.AreEqual(300, SearchCount(s, "bravo"), "the rebuilt index covers the new rows");
            Assert.AreEqual(0, SearchCount(s, "alpha"), "and none of the wiped ones");
        }
        finally
        {
            SqliteStorage.FtsShardThreshold = prevThreshold;
            SqliteStorage.IndexShardCheckpointRows = prevCheckpoint;
        }
    }

    [TestMethod]
    public void Cache_ShardedFts_WarmReuses_AcrossReopen()
    {
        var prev = SqliteStorage.FtsShardThreshold;
        SqliteStorage.FtsShardThreshold = 50;
        var src = NewSourceFile();
        var dbPath = CachedStorage.GetCacheFilePath(src, ".db");
        for (int k = 0; k < 8; k++) _dbPaths.Add(dbPath + $".fts{k}");
        const int sver = SqliteStorage.CacheSchemaVersion;
        try
        {
            using (var s = new SqliteStorage(src))
            {
                s.ClearTables();
                var list = new List<ISearchResult>(300);
                for (int i = 0; i < 300; i++)
                    list.Add(new R(i % 5 == 0 ? $"needle row {i}" : $"hay row {i}", src: $"Prov{i % 3}", rs: $@"C:\l\f{i % 4}.log"));
                s.AddFilteredBatch(list);
                s.BuildSearchIndex(); // sharded (300 >= 50)
                Assert.IsTrue(s.IsSearchIndexBuilt);
                Assert.IsTrue(File.Exists(dbPath + ".fts0"), "sharded build created shard files");
                s.WriteCompletionMetadata(src, sver);
            }
            // Shard files must survive Dispose so the next session can reuse them.
            Assert.IsTrue(File.Exists(dbPath + ".fts0"), "shard files must survive Dispose for warm reuse");

            // Reopen: the sharded index is re-attached and queried WITHOUT a rebuild. (If re-attach failed,
            // the empty single index would return 0 — so a correct count here proves the shards are live.)
            using (var s = new SqliteStorage(src))
            {
                Assert.IsTrue(s.EvaluateCacheReuse(src, sver), "sharded cache should be reusable");
                Assert.IsTrue(s.IsSearchIndexBuilt, "sharded index should be recognized + re-attached on reopen");
                Assert.AreEqual(60, SearchCount(s, "needle"), "search works on the reused sharded cache (no rebuild)");
                Assert.IsTrue(SearchCount(s, "Prov1") > 0, "Source path searchable via re-attached shards");
            }
        }
        finally { SqliteStorage.FtsShardThreshold = prev; }
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
