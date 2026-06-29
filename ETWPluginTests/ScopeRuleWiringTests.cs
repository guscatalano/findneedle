using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Threading;
using FindNeedleCoreUtils;
using findneedle.Implementations;
using findneedle.Implementations.FileExtensions;
using FindNeedlePluginLib;
using FindPluginCore.Implementations.Storage;
using FindPluginCore.PluginSubsystem;
using FindPluginCore.Searching;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ETWPluginTests;

/// <summary>
/// End-to-end test that a `scope` rule loaded from a rules file actually filters the load at DECODE time via
/// NuSearchQuery's auto-wiring (RulesConfigPaths → ResolveDecodeScope → DecodeScope.Current → ETLProcessor).
/// Generates a single-provider .etl, then loads it with (a) no rules, (b) a scope that INCLUDES the provider,
/// (c) a scope that EXCLUDES it — and asserts the stored row count reflects the rule.
/// SkipCI / local-only (ETW capture).
/// </summary>
[TestClass]
[TestCategory("Performance")]
[TestCategory("SkipCI")]
[DoNotParallelize]
public sealed class ScopeRuleWiringTests
{
    private const string ProviderName = "FindNeedle-ScopeWiringTest";

    [EventSource(Name = ProviderName)]
    private sealed class Src : EventSource
    {
        public static readonly Src Log = new();
        public void Tick(int id, string message) => WriteEvent(1, id, message);
    }

    public TestContext TestContext { get; set; } = null!;

    [TestCleanup]
    public void Cleanup() => DecodeScope.Current = null;

    [TestMethod]
    [Timeout(180_000)]
    public void ScopeRule_FromRulesFile_FiltersDecode()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FN_scopewire_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var etl = Path.Combine(dir, "gen.etl");
        try
        {
            GenerateEtl(etl, 50_000);

            long full = Run(etl, rulesJson: null);
            long included = Run(etl, rulesJson: ScopeRulesJson(ProviderName, exclude: false));
            long excluded = Run(etl, rulesJson: ScopeRulesJson(ProviderName, exclude: true));

            // The capture also holds a couple of ETW session-header events from other providers (Windows
            // Kernel / EventTrace), so the scope cleanly PARTITIONS by provider: include keeps our provider's
            // bulk (dropping the few headers), exclude keeps only those few headers.
            TestContext.WriteLine($"full={full:N0}  included={included:N0}  excluded={excluded:N0}");
            Assert.IsTrue(full > 1000, $"full load should have rows, saw {full}");
            Assert.AreEqual(full, included + excluded, "scope partitions the file by provider (include + exclude = full)");
            Assert.IsTrue(included > full - 100, $"include-scope keeps our provider's bulk (kept {included} of {full})");
            Assert.IsTrue(excluded < 100, $"exclude-scope drops our provider's bulk, leaving only a few header events (kept {excluded})");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    private static string ScopeRulesJson(string provider, bool exclude) =>
        "{ \"sections\": [ { \"name\": \"scope\", \"purpose\": \"scope\", \"providers\": [\"" + provider + "\"], " +
        "\"rules\": [ { \"name\": \"scope\", \"action\": { \"type\": \"scope\"" +
        (exclude ? ", \"providerMode\": \"exclude\"" : "") + " } } ] } ] }";

    private long Run(string etl, string? rulesJson)
    {
        DecodeScope.Current = null;
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
        q.RunThrough();
        long rows = ((SqliteStorage)q.ResultStorage!).GetStatistics().filteredRecordCount;
        q.DisposeStorage();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        return rows;
    }

    private void GenerateEtl(string path, long count)
    {
        using var session = new TraceEventSession("FindNeedle_ScopeWire_Session", path);
        session.BufferSizeMB = 64;
        session.EnableProvider(Src.Log.Guid);
        Thread.Sleep(400);
        for (long i = 0; i < count; i++)
        {
            Src.Log.Tick((int)i, $"needle event {i} payload");
            if ((i & 0x3FFF) == 0x3FFF) Thread.Sleep(1);
        }
        Thread.Sleep(1500);
    }
}
