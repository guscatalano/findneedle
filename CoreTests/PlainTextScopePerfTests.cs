using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BasicTextPlugin;
using FindNeedleCoreUtils;
using findneedle.Implementations;
using FindNeedlePluginLib;
using FindPluginCore.Implementations.Storage;
using FindPluginCore.PluginSubsystem;
using FindPluginCore.Searching;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

/// <summary>
/// Performance: decode-time scope on a large plain-text log. Loads LargeSamples\large-5M.log full, then
/// scoped to a leading time window, through the shipped rule -> NuSearchQuery -> decode-filter path. Text has
/// no provider/level, so the scope is a TIME WINDOW; the saving is downstream (fewer rows stored), since the
/// reader still streams every line. Reports both wall-clock times + row counts for the whitepaper (§9). FTS
/// disabled + serial ingest to isolate ingest. SkipCI / local-only (needs the multi-million-line fixture).
/// </summary>
[TestClass]
[TestCategory("Performance")]
[TestCategory("SkipCI")]
[DoNotParallelize]
public sealed class PlainTextScopePerfTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestCleanup]
    public void Cleanup() { DecodeScope.Current = null; SqliteConnection.ClearAllPools(); }

    private static string? FindLog()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            var c = Path.Combine(dir, "LargeSamples", "large-5M.log");
            if (File.Exists(c)) return c;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

    // large-5M.log spans [2026-01-01 00:00:00] .. [2026-02-27 ...], 1 line/sec. A window ending mid-January
    // keeps the leading ~2 weeks (~25% of the 5M lines) — the exact fraction is measured + reported.
    private static string TimeWindowJson(string toUtcIso)
        => "{ \"sections\": [ { \"name\": \"scope\", \"purpose\": \"scope\", \"rules\": [ { \"name\": \"scope\", " +
           "\"action\": { \"type\": \"scope\", \"timeTo\": \"" + toUtcIso + "\" } } ] } ] }";

    private (long ms, long rows) Load(string log, string? rulesJson)
    {
        SqliteStorage.ParallelIngestEnabled = false;
        SqliteStorage.DisableFtsForMeasurement = true;
        DecodeScope.Current = null; // the rule path sets this; don't pre-set
        var loc = new FolderLocation { path = log };
        loc.SetExtensionProcessorList(new List<IFileExtensionProcessor> { new PlainTextProcessor() });
        var cacheDb = CachedStorage.GetCacheFilePath(loc.GetName(), ".db");
        foreach (var p in new[] { cacheDb, cacheDb + "-wal", cacheDb + "-shm", cacheDb + "-journal" })
            try { if (File.Exists(p)) File.Delete(p); } catch { }
        var q = new NuSearchQuery { OverrideStorageType = StorageType.SqlLite, CacheReuseMode = CacheReuseMode.Never };
        q.Locations.Add(loc);
        if (rulesJson != null)
        {
            var path = Path.Combine(Path.GetDirectoryName(log)!, $"scope_{Guid.NewGuid():N}.rules.json");
            File.WriteAllText(path, rulesJson);
            q.RulesConfigPaths.Add(path);
        }
        var sw = Stopwatch.StartNew();
        q.RunThrough();
        sw.Stop();
        long rows = ((SqliteStorage)q.ResultStorage!).GetStatistics().filteredRecordCount;
        q.DisposeStorage();
        SqliteConnection.ClearAllPools();
        return (sw.ElapsedMilliseconds, rows);
    }

    [TestMethod]
    [Timeout(1_800_000)]
    public void ScopedLoad_TimeWindow_vs_Full()
    {
        var log = FindLog();
        if (log == null) { Assert.Inconclusive("need LargeSamples/large-5M.log"); return; }

        var full = Load(log, rulesJson: null);
        Assert.IsTrue(full.rows > 0, "fixture should load some rows");
        var scoped = Load(log, TimeWindowJson("2026-01-14T00:00:00Z"));

        double ratio = scoped.ms > 0 ? (double)full.ms / scoped.ms : 0;
        TestContext.WriteLine($"{Path.GetFileName(log)} ({new FileInfo(log).Length / 1024.0 / 1024.0:N0} MB)");
        TestContext.WriteLine($"FULL   load: {full.ms,7:N0} ms  ({full.rows:N0} rows)");
        TestContext.WriteLine($"SCOPED load: {scoped.ms,7:N0} ms  ({scoped.rows:N0} rows)  -> {ratio:F1}x, {100.0 * scoped.rows / full.rows:F1}% of rows");

        Assert.IsTrue(scoped.rows < full.rows, "scoped load should ingest fewer rows");
    }
}
