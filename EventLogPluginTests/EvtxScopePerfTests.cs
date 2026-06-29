using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FindNeedleCoreUtils;
using findneedle.Implementations;
using findneedle.Implementations.FileExtensions;
using FindNeedlePluginLib;
using FindPluginCore.Implementations.Storage;
using FindPluginCore.PluginSubsystem;
using FindPluginCore.Searching;
using Microsoft.Data.Sqlite;

namespace EventLogPluginTests;

/// <summary>
/// Performance: decode-time scope on a real Event Log (.evtx). Loads LargeSamples\large-app.evtx full, then
/// scoped to drop its single noisiest provider, through the shipped rule -> NuSearchQuery -> decode-filter
/// path. Reports both wall-clock times + row counts for the whitepaper (§9). FTS disabled + serial ingest to
/// isolate the decode/wrap cost the scope actually skips (EVTX's wrap renders the message via
/// FormatDescription(), so dropping events before the wrap is the win). SkipCI / local-only.
/// </summary>
[TestClass]
[TestCategory("Performance")]
[TestCategory("SkipCI")]
[DoNotParallelize]
public sealed class EvtxScopePerfTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestCleanup]
    public void Cleanup() { DecodeScope.Current = null; SqliteConnection.ClearAllPools(); }

    private static string? FindEvtx()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            var c = Path.Combine(dir, "LargeSamples", "large-app.evtx");
            if (File.Exists(c)) return c;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

    private static string ExcludeProviderJson(string provider)
        => "{ \"sections\": [ { \"name\": \"scope\", \"purpose\": \"scope\", \"providers\": [\"" +
           provider.Replace("\"", "\\\"") + "\"], \"rules\": [ { \"name\": \"scope\", \"action\": " +
           "{ \"type\": \"scope\", \"providerMode\": \"exclude\" } } ] } ] }";

    private (long ms, long rows, List<ISearchResult> all) Load(string evtx, string? rulesJson, bool keepRows)
    {
        SqliteStorage.ParallelIngestEnabled = false;
        SqliteStorage.DisableFtsForMeasurement = true;
        DecodeScope.Current = null; // the rule path sets this; don't pre-set
        var loc = new FolderLocation { path = evtx };
        loc.SetExtensionProcessorList(new List<IFileExtensionProcessor> { new EVTXProcessor() });
        var cacheDb = CachedStorage.GetCacheFilePath(loc.GetName(), ".db");
        foreach (var p in new[] { cacheDb, cacheDb + "-wal", cacheDb + "-shm", cacheDb + "-journal" })
            try { if (File.Exists(p)) File.Delete(p); } catch { }
        var q = new NuSearchQuery { OverrideStorageType = StorageType.SqlLite, CacheReuseMode = CacheReuseMode.Never };
        q.Locations.Add(loc);
        if (rulesJson != null)
        {
            var path = Path.Combine(Path.GetDirectoryName(evtx)!, $"scope_{Guid.NewGuid():N}.rules.json");
            File.WriteAllText(path, rulesJson);
            q.RulesConfigPaths.Add(path);
        }
        var sw = Stopwatch.StartNew();
        q.RunThrough();
        sw.Stop();
        var storage = (SqliteStorage)q.ResultStorage!;
        long rows = storage.GetStatistics().filteredRecordCount;
        var all = keepRows
            ? storage.GetFilteredPage(new SqliteStorage.FilterInput(), new SqliteStorage.SortInput(), 0, 5_000_000)
            : new List<ISearchResult>();
        q.DisposeStorage();
        SqliteConnection.ClearAllPools();
        return (sw.ElapsedMilliseconds, rows, all);
    }

    [TestMethod]
    [Timeout(1_200_000)]
    public void ScopedLoad_DropNoisiestProvider_vs_Full()
    {
        var evtx = FindEvtx();
        if (evtx == null) { Assert.Inconclusive("need LargeSamples/large-app.evtx"); return; }

        var full = Load(evtx, rulesJson: null, keepRows: true);
        var hist = full.all.GroupBy(r => r.GetSource())
                           .Select(g => (provider: g.Key, count: g.Count()))
                           .OrderByDescending(x => x.count).ToList();
        Assert.IsTrue(full.rows > 0, "fixture should load some events");
        var top = hist.First();
        TestContext.WriteLine($"{Path.GetFileName(evtx)} ({new FileInfo(evtx).Length / 1024.0 / 1024.0:N0} MB): {full.rows:N0} events across {hist.Count} providers");
        foreach (var h in hist.Take(8)) TestContext.WriteLine($"  {h.count,10:N0}  {h.provider}");

        var scoped = Load(evtx, ExcludeProviderJson(top.provider), keepRows: false);

        double ratio = scoped.ms > 0 ? (double)full.ms / scoped.ms : 0;
        TestContext.WriteLine($"FULL   load: {full.ms,7:N0} ms  ({full.rows:N0} rows)");
        TestContext.WriteLine($"SCOPED load: {scoped.ms,7:N0} ms  ({scoped.rows:N0} rows, dropped '{top.provider}' = {100.0 * top.count / full.rows:F1}%)  -> {ratio:F1}x, {100.0 * scoped.rows / full.rows:F1}% of rows");

        Assert.IsTrue(scoped.rows < full.rows, "scoped load should ingest fewer rows");
    }
}
