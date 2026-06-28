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
/// Generates a SERIES of synthetic .etl files at increasing sizes (default 5/10/25/50 M events) and
/// measures first-open ingest — serial and parallel (fan-out) — for each, to produce a real scaling curve
/// (the whitepaper's graph). Synthetic single-provider events are deliberate: the scaling shape we're
/// charting is in the ROW COUNT, not provider diversity, and this keeps counts controllable + the run
/// self-contained (no elevation/xperf). Each size is generated → measured → deleted before the next so
/// disk stays bounded, and a result row is printed per size so partial data survives a cutoff.
///
/// SkipCI / Performance / local-only (ETW capture + long run). Sizes via FINDNEEDLE_SERIES="5,10,25,50".
/// </summary>
[TestClass]
[TestCategory("Performance")]
[TestCategory("SkipCI")]
public sealed class ScalingSeriesTests
{
    [EventSource(Name = "FindNeedle-ScalingSeries")]
    private sealed class Src : EventSource
    {
        public static readonly Src Log = new();
        public void Tick(int id, string message) => WriteEvent(1, id, message);
    }

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [Timeout(7_200_000)] // up to 2 h for the whole series
    public void Generate_ScalingSeries_AndMeasure()
    {
        var sizes = (Environment.GetEnvironmentVariable("FINDNEEDLE_SERIES") ?? "5,10,25,50")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.Parse(s)).ToArray();

        SqliteStorage.DisableFtsForMeasurement = true; // isolate ingest from the FTS build
        var outDir = Path.Combine(RepoRoot(), "LargeSamples");
        Directory.CreateDirectory(outDir);

        TestContext.WriteLine("RESULT\ttargetM\tcapturedRows\tfileMB\tgenSec\tserialMs\tparallelMs\tspeedup");
        foreach (var m in sizes)
        {
            long target = (long)m * 1_000_000;
            var etl = Path.Combine(outDir, $"series-{m}M.etl");
            try
            {
                var (rows, mb, genSec) = GenerateEtl(etl, target);
                var serial = MeasureIngest(etl, parallel: false);
                var parallel = MeasureIngest(etl, parallel: true);
                double speedup = parallel.ms > 0 ? (double)serial.ms / parallel.ms : 0;
                // Tab-separated so it's trivial to lift into the graph.
                TestContext.WriteLine($"RESULT\t{m}\t{rows}\t{mb}\t{genSec:F0}\t{serial.ms}\t{parallel.ms}\t{speedup:F2}");
                Assert.AreEqual(serial.rows, parallel.rows, $"row counts must match at {m}M");
            }
            finally
            {
                Cleanup(etl);
            }
        }
    }

    private (long rows, long mb, double genSec) GenerateEtl(string path, long count)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
        var sw = Stopwatch.StartNew();
        using (var session = new TraceEventSession("FindNeedle_Series_Session", path))
        {
            session.BufferSizeMB = 256; // big buffers reduce ETW under-load drops
            session.EnableProvider(Src.Log.Guid);
            Thread.Sleep(500);
            for (long i = 0; i < count; i++)
            {
                Src.Log.Tick((int)i, $"needle event {i} payload");
                if ((i & 0x1FFFF) == 0x1FFFF) Thread.Sleep(1); // yield ~every 128k events to flush
            }
            Thread.Sleep(3000); // flush last buffers before the file is finalized
        }
        sw.Stop();
        long rows = -1;
        try { rows = findneedle.ETWPlugin.EtlInfoExtractor.Inspect(path).EventCount; } catch { }
        long mb = (long)(new FileInfo(path).Length / 1024.0 / 1024.0);
        TestContext.WriteLine($"  generated {path}: target {count:N0}, captured {rows:N0}, {mb} MB, {sw.Elapsed.TotalSeconds:F0}s");
        return (rows, mb, sw.Elapsed.TotalSeconds);
    }

    private (long ms, long rows) MeasureIngest(string etl, bool parallel)
    {
        SqliteStorage.ParallelIngestEnabled = parallel;
        SqliteStorage.ParallelIngestMinRows = 1;
        ETWTestUtils.UseTestTraceFmt();
        var loc = new FolderLocation { path = etl };
        loc.SetExtensionProcessorList(new List<IFileExtensionProcessor> { new ETLProcessor() });
        // Fresh cache each run → a true first-open every time.
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
        return (sw.ElapsedMilliseconds, rows);
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
