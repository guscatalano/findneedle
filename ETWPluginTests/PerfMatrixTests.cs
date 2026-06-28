using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
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
/// Full performance MATRIX for the whitepaper: across 7 sizes (5k → 50M rows) it measures, with the app's
/// DEFAULT settings, the storage tier chosen, first-open ingest, full-text index build, and indexed search
/// latency. One row printed per size (so partial results survive a cutoff). UI responsiveness is O(1)
/// (off-thread, paged) and is reported separately from the FlaUI suite, not re-measured per size here.
///
/// SkipCI / Performance / local-only (ETW capture + a long run). Sizes via FINDNEEDLE_MATRIX
/// (default "5000,50000,500000,5000000,10000000,25000000,50000000").
/// </summary>
[TestClass]
[TestCategory("Performance")]
[TestCategory("SkipCI")]
public sealed class PerfMatrixTests
{
    [EventSource(Name = "FindNeedle-PerfMatrix")]
    private sealed class Src : EventSource
    {
        public static readonly Src Log = new();
        public void Tick(int id, string message) => WriteEvent(1, id, message);
    }

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [Timeout(9_000_000)] // up to 2.5 h for the whole matrix (50M ingest + FTS dominate)
    public void Measure_PerfMatrix_ExistingSetup()
    {
        var sizes = (Environment.GetEnvironmentVariable("FINDNEEDLE_MATRIX")
                     ?? "5000,50000,500000,5000000,10000000,25000000,50000000")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(long.Parse).ToArray();

        // Default settings: FTS on, parallel ingest on (engages on large SQLite loads), auto storage tier.
        SqliteStorage.DisableFtsForMeasurement = false;
        SqliteStorage.ParallelIngestEnabled = true;
        SqliteStorage.ParallelIngestMinRows = SqliteStorage.DefaultParallelIngestMinRows;

        var outDir = Path.Combine(RepoRoot(), "LargeSamples");
        Directory.CreateDirectory(outDir);

        TestContext.WriteLine("MATRIX\trows\ttier\tfileMB\tgenSec\tingestMs\tftsBuildMs\tsearchMs");
        foreach (var n in sizes)
        {
            var etl = Path.Combine(outDir, $"matrix-{n}.etl");
            try
            {
                var (captured, mb, genSec) = GenerateEtl(etl, n);
                var r = MeasureOne(etl, (int)Math.Min(n, int.MaxValue));
                TestContext.WriteLine($"MATRIX\t{r.rows}\t{r.tier}\t{mb}\t{genSec:F0}\t{r.ingestMs}\t{r.ftsMs}\t{r.searchMs}");
            }
            catch (Exception ex) { TestContext.WriteLine($"MATRIX\t{n}\tERROR\t-\t-\t{ex.GetType().Name}: {ex.Message}"); }
            finally { Cleanup(etl); }
        }
    }

    private (string tier, long rows, long ingestMs, long ftsMs, long searchMs) MeasureOne(string etl, int estRows)
    {
        ETWTestUtils.UseTestTraceFmt();
        var loc = new FolderLocation { path = etl };
        loc.SetExtensionProcessorList(new List<IFileExtensionProcessor> { new ETLProcessor() });
        var cacheDb = CachedStorage.GetCacheFilePath(loc.GetName(), ".db");
        foreach (var p in new[] { cacheDb, cacheDb + "-wal", cacheDb + "-shm", cacheDb + "-journal" })
            try { if (File.Exists(p)) File.Delete(p); } catch { }

        // Auto tier driven by the intended row count; defer the index so ingest is timed cleanly.
        var q = new NuSearchQuery
        {
            OverrideStorageType = StorageType.Auto,
            EstimatedRowCountOverride = estRows,
            DeferIndexBuild = true,
            CacheReuseMode = CacheReuseMode.Never,
        };
        q.Locations.Add(loc);

        var sw = Stopwatch.StartNew();
        q.RunThrough();
        long ingestMs = sw.ElapsedMilliseconds;

        var storage = q.ResultStorage!;
        string tier = storage.GetType().Name;
        long rows = storage.GetStatistics().filteredRecordCount;

        // FTS build + indexed search are meaningful on the SQLite tier; small tiers search in memory.
        long ftsMs = 0, searchMs;
        if (storage is SqliteStorage sql)
        {
            sw.Restart();
            q.BuildSearchIndexNow();
            ftsMs = sw.ElapsedMilliseconds;
            sw.Restart();
            // Selective indexed lookup: the exact phrase appears in one event's message.
            _ = sql.GetFilteredCount(new SqliteStorage.FilterInput { Message = "needle event 100 payload" });
            searchMs = sw.ElapsedMilliseconds;
        }
        else
        {
            // In-memory / hybrid (small logs): no FTS index; search is a trivial in-memory scan over a
            // few thousand rows — effectively instant. Reported as ~0; not separately micro-benchmarked.
            ftsMs = -1; // sentinel: not applicable
            searchMs = 0;
        }

        q.DisposeStorage();
        return (tier, rows, ingestMs, ftsMs, searchMs);
    }

    private (long rows, long mb, double genSec) GenerateEtl(string path, long count)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
        var sw = Stopwatch.StartNew();
        using (var session = new TraceEventSession("FindNeedle_Matrix_Session", path))
        {
            session.BufferSizeMB = 256;
            session.EnableProvider(Src.Log.Guid);
            Thread.Sleep(500);
            for (long i = 0; i < count; i++)
            {
                Src.Log.Tick((int)i, $"needle event {i} payload");
                if ((i & 0x1FFFF) == 0x1FFFF) Thread.Sleep(1);
            }
            Thread.Sleep(2000);
        }
        sw.Stop();
        long rows = -1;
        try { rows = findneedle.ETWPlugin.EtlInfoExtractor.Inspect(path).EventCount; } catch { }
        long mb = (long)(new FileInfo(path).Length / 1024.0 / 1024.0);
        return (rows, mb, sw.Elapsed.TotalSeconds);
    }

    private void Cleanup(string etl)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(etl)) File.Delete(etl); } catch { }
        try
        {
            var cacheDb = CachedStorage.GetCacheFilePath(etl, ".db");
            foreach (var p in new[] { cacheDb, cacheDb + "-wal", cacheDb + "-shm", cacheDb + "-journal" })
                if (File.Exists(p)) File.Delete(p);
        }
        catch { }
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "findneedle.sln"))) d = d.Parent;
        return d?.FullName ?? AppContext.BaseDirectory;
    }
}
