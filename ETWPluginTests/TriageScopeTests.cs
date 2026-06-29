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
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ETWPluginTests;

/// <summary>
/// PROTOTYPE measurement for the large-file triage idea: instead of ingesting an entire multi-GB capture,
/// inspect it for its provider mix, then load ONLY the providers the user cares about (decode-time filter
/// in ETLProcessor). Compares a full load vs a scoped load on a real multi-provider .etl (FINDNEEDLE_ETL).
///
/// Scope = every provider EXCEPT the dropped set (default "Windows Kernel,MSNT_SystemTrace" — the ~90%
/// kernel bulk), i.e. "give me the app/.NET/Defender/RPC events, not the kernel scheduling firehose".
/// Override the drop list with FINDNEEDLE_SCOPE_DROP, or set an explicit allow-list with FINDNEEDLE_SCOPE.
///
/// SkipCI / Performance / local-only (needs a real multi-provider .etl).
/// </summary>
[TestClass]
[TestCategory("Performance")]
[TestCategory("SkipCI")]
public sealed class TriageScopeTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestCleanup]
    public void Cleanup() => DecodeScope.Current = null; // don't leak the global scope to other tests

    [TestMethod]
    [Timeout(2_400_000)]
    public void Prototype_ScopedLoad_vs_Full()
    {
        var etl = Environment.GetEnvironmentVariable("FINDNEEDLE_ETL");
        if (string.IsNullOrEmpty(etl) || !File.Exists(etl)) { Assert.Inconclusive("Set FINDNEEDLE_ETL to a multi-provider .etl."); return; }
        SqliteStorage.DisableFtsForMeasurement = true; // isolate ingest

        // --- Triage step: inspect for the provider mix (this is what the panel would show the user) ---
        var info = findneedle.ETWPlugin.EtlInfoExtractor.Inspect(etl);
        TestContext.WriteLine($"Inspected {Path.GetFileName(etl)}: {info.EventCount:N0} events across {info.Providers.Count} providers");
        foreach (var kv in info.Providers.OrderByDescending(k => k.Value).Take(12))
            TestContext.WriteLine($"  {kv.Value,14:N0}  {kv.Key}");

        // Build a `scope` rule and drive it the SHIPPED way (rules file → NuSearchQuery → decode filter),
        // not by poking DecodeScope.Current directly (NuSearchQuery would overwrite that from the rules).
        // An explicit allow-list (FINDNEEDLE_SCOPE), else exclude the dropped providers.
        string scopeJson;
        Func<string, bool> kept;
        var allow = Environment.GetEnvironmentVariable("FINDNEEDLE_SCOPE");
        if (!string.IsNullOrEmpty(allow))
        {
            var inc = allow.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var incSet = new HashSet<string>(inc, StringComparer.OrdinalIgnoreCase);
            scopeJson = ScopeRulesJson(inc, exclude: false);
            kept = p => incSet.Contains(p);
        }
        else
        {
            var drop = (Environment.GetEnvironmentVariable("FINDNEEDLE_SCOPE_DROP") ?? "Windows Kernel,MSNT_SystemTrace")
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var dropSet = new HashSet<string>(drop, StringComparer.OrdinalIgnoreCase);
            scopeJson = ScopeRulesJson(drop, exclude: true);
            kept = p => !dropSet.Contains(p);
        }
        long scopedExpected = info.Providers.Where(kv => kept(kv.Key)).Sum(kv => (long)kv.Value);
        TestContext.WriteLine($"Scope ≈ {scopedExpected:N0} events ({100.0 * scopedExpected / Math.Max(1, info.EventCount):F1}% of the file)");

        var full = Load(etl, rulesJson: null);
        var scoped = Load(etl, rulesJson: scopeJson);

        TestContext.WriteLine($"FULL   load: {full.ms,8:N0} ms  ({full.rows:N0} rows)");
        TestContext.WriteLine($"SCOPED load: {scoped.ms,8:N0} ms  ({scoped.rows:N0} rows)  → {(double)full.ms / Math.Max(1, scoped.ms):F1}× faster, {100.0 * scoped.rows / Math.Max(1, full.rows):F1}% of the rows");

        Assert.IsTrue(scoped.rows < full.rows, "scoped load should ingest fewer rows");
        Assert.IsTrue(scoped.ms < full.ms, "scoped load should be faster");
    }

    private static string ScopeRulesJson(IEnumerable<string> providers, bool exclude)
    {
        var arr = string.Join(",", providers.Select(p => "\"" + p.Replace("\"", "\\\"") + "\""));
        return "{ \"sections\": [ { \"name\": \"scope\", \"purpose\": \"scope\", \"providers\": [" + arr + "], " +
               "\"rules\": [ { \"name\": \"scope\", \"action\": { \"type\": \"scope\"" +
               (exclude ? ", \"providerMode\": \"exclude\"" : "") + " } } ] } ] }";
    }

    private (long ms, long rows) Load(string etl, string? rulesJson)
    {
        SqliteStorage.ParallelIngestEnabled = false; // serial — the recommended path for very large logs
        DecodeScope.Current = null; // the rule path (NuSearchQuery.ResolveDecodeScope) sets this; don't pre-set
        ETWTestUtils.UseTestTraceFmt();
        var loc = new FolderLocation { path = etl };
        loc.SetExtensionProcessorList(new List<IFileExtensionProcessor> { new ETLProcessor() });
        var cacheDb = CachedStorage.GetCacheFilePath(loc.GetName(), ".db");
        foreach (var p in new[] { cacheDb, cacheDb + "-wal", cacheDb + "-shm", cacheDb + "-journal" })
            try { if (File.Exists(p)) File.Delete(p); } catch { }
        var q = new NuSearchQuery { OverrideStorageType = StorageType.SqlLite, CacheReuseMode = CacheReuseMode.Never };
        q.Locations.Add(loc);
        if (rulesJson != null)
        {
            var rulesPath = Path.Combine(Path.GetDirectoryName(etl)!, $"scope_{Guid.NewGuid():N}.rules.json");
            File.WriteAllText(rulesPath, rulesJson);
            q.RulesConfigPaths.Add(rulesPath);
        }
        var sw = Stopwatch.StartNew();
        q.RunThrough();
        sw.Stop();
        long rows = ((SqliteStorage)q.ResultStorage!).GetStatistics().filteredRecordCount;
        q.DisposeStorage();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        return (sw.ElapsedMilliseconds, rows);
    }
}
