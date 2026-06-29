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

        // Build the DecodeScope: an explicit allow-list (FINDNEEDLE_SCOPE), else exclude the dropped providers.
        DecodeScope scope;
        Func<string, bool> kept;
        var allow = Environment.GetEnvironmentVariable("FINDNEEDLE_SCOPE");
        if (!string.IsNullOrEmpty(allow))
        {
            var inc = new HashSet<string>(allow.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
            scope = new DecodeScope { IncludeProviders = inc };
            kept = p => inc.Contains(p);
        }
        else
        {
            var drop = new HashSet<string>((Environment.GetEnvironmentVariable("FINDNEEDLE_SCOPE_DROP") ?? "Windows Kernel,MSNT_SystemTrace")
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
            scope = new DecodeScope { ExcludeProviders = drop };
            kept = p => !drop.Contains(p);
        }
        long scopedExpected = info.Providers.Where(kv => kept(kv.Key)).Sum(kv => (long)kv.Value);
        TestContext.WriteLine($"Scope ≈ {scopedExpected:N0} events ({100.0 * scopedExpected / Math.Max(1, info.EventCount):F1}% of the file)");

        var full = Load(etl, scope: null);
        var scoped = Load(etl, scope: scope);

        TestContext.WriteLine($"FULL   load: {full.ms,8:N0} ms  ({full.rows:N0} rows)");
        TestContext.WriteLine($"SCOPED load: {scoped.ms,8:N0} ms  ({scoped.rows:N0} rows)  → {(double)full.ms / Math.Max(1, scoped.ms):F1}× faster, {100.0 * scoped.rows / Math.Max(1, full.rows):F1}% of the rows");

        Assert.IsTrue(scoped.rows < full.rows, "scoped load should ingest fewer rows");
        Assert.IsTrue(scoped.ms < full.ms, "scoped load should be faster");
    }

    private (long ms, long rows) Load(string etl, DecodeScope? scope)
    {
        SqliteStorage.ParallelIngestEnabled = false; // serial — the recommended path for very large logs
        ETWTestUtils.UseTestTraceFmt();
        DecodeScope.Current = scope; // process-global (prototype); null = full load
        var loc = new FolderLocation { path = etl };
        loc.SetExtensionProcessorList(new List<IFileExtensionProcessor> { new ETLProcessor() });
        var cacheDb = CachedStorage.GetCacheFilePath(loc.GetName(), ".db");
        foreach (var p in new[] { cacheDb, cacheDb + "-wal", cacheDb + "-shm", cacheDb + "-journal" })
            try { if (File.Exists(p)) File.Delete(p); } catch { }
        var q = new NuSearchQuery { OverrideStorageType = StorageType.SqlLite, CacheReuseMode = CacheReuseMode.Never };
        q.Locations.Add(loc);
        var sw = Stopwatch.StartNew();
        q.RunThrough();
        sw.Stop();
        long rows = ((SqliteStorage)q.ResultStorage!).GetStatistics().filteredRecordCount;
        q.DisposeStorage();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        return (sw.ElapsedMilliseconds, rows);
    }
}
