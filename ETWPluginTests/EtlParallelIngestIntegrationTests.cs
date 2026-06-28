using System.Diagnostics.Tracing;
using System.IO;
using System.Threading;
using findneedle.Implementations;
using findneedle.Implementations.FileExtensions;
using FindNeedlePluginLib;
using FindPluginCore.Implementations.Storage;
using FindPluginCore.PluginSubsystem;
using FindPluginCore.Searching;
using Microsoft.Diagnostics.Tracing.Session;

namespace ETWPluginTests;

/// <summary>
/// Integration test for the PRODUCTION parallel fan-out ingest wiring in NuSearchQuery.RunThrough (the
/// estimate gate, the streaming callback routing to the shard sink, the merge into the real SQLite store,
/// and the FTS build on the merged DB) — not just the isolated sink (covered by CoreTests). Generates a
/// real .etl, runs it through the full pipeline twice (parallel ON vs serial OFF), and asserts the merged
/// DB is equivalent: same row count, same scan order (ORDER BY Id), same substring-search result.
///
/// The fan-out is forced on by lowering ParallelIngestMinRows so a small generated trace exercises the path
/// without generating a multi-million-event file. Windows/ETW + admin dependent, so Performance/local-only.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EtlParallelIngestIntegrationTests
{
    [EventSource(Name = "FindNeedle-ParIngestTest")]
    private sealed class Src : EventSource
    {
        public static readonly Src Log = new();
        public void Tick(int id, string message) => WriteEvent(1, id, message);
    }

    public TestContext TestContext { get; set; } = null!;

    private bool _savedEnabled;
    private int _savedMin;

    [TestInitialize]
    public void Init()
    {
        _savedEnabled = SqliteStorage.ParallelIngestEnabled;
        _savedMin = SqliteStorage.ParallelIngestMinRows;
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteStorage.ParallelIngestEnabled = _savedEnabled;
        SqliteStorage.ParallelIngestMinRows = _savedMin;
    }

    private static void GenerateEtl(string etlPath, int events)
    {
        using var session = new TraceEventSession("FindNeedle_ParIngest_Session", etlPath);
        session.EnableProvider(Src.Log.Guid);
        Thread.Sleep(300);
        for (int i = 0; i < events; i++)
            Src.Log.Tick(i, $"generated event {i} - payload");
        Thread.Sleep(500);
    }

    /// <summary>Run the generated .etl through the full pipeline with SQLite storage; return the rows read
    /// back in default (Id / scan) order plus the substring-search count for a known token.</summary>
    private (int count, List<string> messagesInOrder, int searchHits) RunPipeline(string etlDir, string needle)
    {
        ETWTestUtils.UseTestTraceFmt();
        var loc = new FolderLocation { path = etlDir };
        loc.SetExtensionProcessorList(new List<IFileExtensionProcessor> { new ETLProcessor() });

        var query = new NuSearchQuery { OverrideStorageType = StorageType.SqlLite };
        query.Locations.Add(loc);
        query.RunThrough();

        var storage = (SqliteStorage)query.ResultStorage!;
        int count = storage.GetStatistics().filteredRecordCount;

        // Read in default ORDER BY Id ASC (the viewer's scan order) so we can compare ordering.
        var page = storage.GetFilteredPage(new SqliteStorage.FilterInput(), new SqliteStorage.SortInput(), 0, 5_000_000);
        var msgs = page.Select(r => r.GetMessage()).ToList();
        int hits = storage.GetFilteredCount(new SqliteStorage.FilterInput { Message = needle });

        query.DisposeStorage();
        return (count, msgs, hits);
    }

    [TestMethod]
    [TestCategory("Performance")]
    [Timeout(300000)]
    public void ParallelIngest_ProductionPipeline_MatchesSerial()
    {
        // Two copies of the SAME trace in separate dirs → distinct cache paths, so neither run reuses the
        // other's cache (the cache key is derived from the source path).
        var root = Path.Combine(Path.GetTempPath(), $"FN_paringest_{Guid.NewGuid():N}");
        var serialDir = Path.Combine(root, "serial");
        var parDir = Path.Combine(root, "parallel");
        Directory.CreateDirectory(serialDir);
        Directory.CreateDirectory(parDir);
        const int events = 8000;
        const string needle = "event 1234 -"; // matches exactly one event's message

        try
        {
            var srcEtl = Path.Combine(serialDir, "gen.etl");
            GenerateEtl(srcEtl, events);
            Assert.IsTrue(File.Exists(srcEtl), "a real .etl should have been generated");
            File.Copy(srcEtl, Path.Combine(parDir, "gen.etl"));
            TestContext.WriteLine($"generated .etl: {new FileInfo(srcEtl).Length / 1024.0:F1} KB, {events} events");

            // ----- serial baseline (fan-out OFF) -----
            SqliteStorage.ParallelIngestEnabled = false;
            var serial = RunPipeline(serialDir, needle);
            TestContext.WriteLine($"SERIAL : {serial.count} rows, {serial.searchHits} search hits");

            // ----- parallel (fan-out forced ON for any streaming load) -----
            SqliteStorage.ParallelIngestEnabled = true;
            SqliteStorage.ParallelIngestMinRows = 1;
            var parallel = RunPipeline(parDir, needle);
            TestContext.WriteLine($"PARALLEL: {parallel.count} rows, {parallel.searchHits} search hits");

            Assert.IsTrue(serial.count > 0, "serial run should load rows");
            Assert.AreEqual(serial.count, parallel.count, "parallel ingest must load the same row count as serial");
            Assert.AreEqual(serial.searchHits, parallel.searchHits, "substring search must return the same hits after the merge + FTS");
            CollectionAssert.AreEqual(serial.messagesInOrder, parallel.messagesInOrder,
                "parallel ingest must preserve scan order (ORDER BY Id) identically to serial");
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
