using System;
using System.Collections.Generic;
using System.IO;
using FindNeedleCoreUtils;
using findneedle.Implementations;
using findneedle.Implementations.FileExtensions;
using FindNeedlePluginLib;
using FindPluginCore.Implementations.Storage;
using FindPluginCore.PluginSubsystem;
using FindPluginCore.Searching;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ETWPluginTests;

/// <summary>
/// Regression guard for <see cref="CacheReuseMode"/> — specifically that <see cref="CacheReuseMode.Never"/>
/// (what the GUI's Settings "don't reuse" / the CLI <c>--cache=off</c> map to) ACTUALLY forces a fresh scan
/// even when a valid warm on-disk cache exists. This is the path that kept regressing: a populated cache .db
/// from a prior run being silently reused despite the caller asking to rescan. The existing scoped/perf tests
/// always delete the cache first, so none of them exercise the "warm cache + Never" decision — this does.
///
/// Sequence on the committed fixture: (1) Always over a cold cache -> scans + writes the cache; (2) Always ->
/// warm-cache HIT (proves the cache is valid + reusable, so step 3 is a real choice, not a miss); (3) Never ->
/// rescans despite that valid warm cache, returning the same row count with no duplication / UNIQUE collision.
/// Note: by design <see cref="CacheReuseMode.Never"/> neither reads NOR writes the cache (it does not leave a
/// reusable cache behind — see <c>TryStampCacheCompletion</c>), so the cache must be warmed with Always.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CacheReuseModeTests
{
    public TestContext TestContext { get; set; } = null!;

    private static string FixtureEtl() => ETWTestUtils.GetSampleETLFile(); // SampleFiles\test.etl (committed)

    private static void DeleteCache(string sourcePath)
    {
        SqliteConnection.ClearAllPools();
        var db = CachedStorage.GetCacheFilePath(sourcePath, ".db");
        foreach (var p in new[] { db, db + "-wal", db + "-shm", db + "-journal" })
            try { if (File.Exists(p)) File.Delete(p); } catch { }
    }

    /// <summary>One full pipeline run at the given cache mode; returns (rows, reusedCache).</summary>
    private static (int rows, bool reused) Run(string etl, CacheReuseMode mode)
    {
        ETWTestUtils.UseTestTraceFmt();
        SqliteStorage.ParallelIngestEnabled = false; // serial — deterministic for a tiny fixture
        DecodeScope.Current = null;
        var loc = new FolderLocation { path = etl };
        loc.SetExtensionProcessorList(new List<IFileExtensionProcessor> { new ETLProcessor() });
        var q = new NuSearchQuery { OverrideStorageType = StorageType.SqlLite, CacheReuseMode = mode };
        q.Locations.Add(loc);
        q.RunThrough();
        int rows = ((SqliteStorage)q.ResultStorage!).GetStatistics().filteredRecordCount;
        bool reused = q.LastSearchReusedCache;
        q.DisposeStorage();
        SqliteConnection.ClearAllPools();
        return (rows, reused);
    }

    [TestMethod]
    [TestCategory("Storage")]
    [Timeout(300_000)]
    public void Never_RescansEvenWithWarmValidCache()
    {
        var etl = FixtureEtl();
        if (!File.Exists(etl)) { Assert.Inconclusive($"fixture missing: {etl}"); return; }
        DeleteCache(etl);
        try
        {
            // (1) cold cache + Always: must scan (nothing to reuse) and write the cache for next time.
            var cold = Run(etl, CacheReuseMode.Always);
            Assert.IsFalse(cold.reused, "first run over a cold cache must scan, not reuse");
            Assert.IsTrue(cold.rows > 0, "fixture should decode to some rows");
            TestContext.WriteLine($"(1) Always cold: {cold.rows} rows, reused={cold.reused}");

            // (2) Always: the cache we just wrote is valid -> warm HIT (confirms caching actually works,
            // so step 3's rescan is a real choice, not a cache miss).
            var warm = Run(etl, CacheReuseMode.Always);
            Assert.IsTrue(warm.reused, "Always over a valid warm cache must reuse (cache hit)");
            Assert.AreEqual(cold.rows, warm.rows, "warm reuse returns the same row count");
            TestContext.WriteLine($"(2) Always warm: {warm.rows} rows, reused={warm.reused}");

            // (3) Never again, cache still warm+valid: must IGNORE it and rescan, with no row
            // duplication and no UNIQUE-Id collision from re-inserting into the populated cache db.
            var rescan = Run(etl, CacheReuseMode.Never);
            Assert.IsFalse(rescan.reused, "Never must rescan even when a valid warm cache exists (--cache=off)");
            Assert.AreEqual(cold.rows, rescan.rows, "rescan returns the same row count (no dup / collision)");
            TestContext.WriteLine($"(3) Never  warm: {rescan.rows} rows, reused={rescan.reused}");
        }
        finally { DeleteCache(etl); DecodeScope.Current = null; }
    }
}
