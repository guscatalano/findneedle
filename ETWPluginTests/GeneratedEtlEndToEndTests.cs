using System.Diagnostics.Tracing;
using System.IO;
using System.Threading;
using findneedle.Implementations;
using findneedle.Implementations.FileExtensions;
using FindNeedlePluginLib;
using FindPluginCore.PluginSubsystem;
using FindPluginCore.Searching;
using Microsoft.Diagnostics.Tracing.Session;

namespace ETWPluginTests;

/// <summary>
/// Generates a real .etl on the fly (a file-mode ETW session capturing a custom EventSource — no
/// LiveCollector), then loads it through the full search pipeline the UX uses: FolderLocation +
/// ETLProcessor run by NuSearchQuery.RunThrough, results read back from storage. Proves a freshly
/// produced, modern (non-WPP) ETL flows end-to-end — which relies on ETLProcessor's TraceEvent
/// fallback (tracefmt alone can't decode EventSource traces). Slow + Windows/ETW + admin
/// dependent, so Performance / local-only.
/// </summary>
[TestClass]
public sealed class GeneratedEtlEndToEndTests
{
    [EventSource(Name = "FindNeedle-GenTest")]
    private sealed class GenSource : EventSource
    {
        public static readonly GenSource Log = new();
        public void Tick(int id, string message) => WriteEvent(1, id, message);
    }

    public TestContext TestContext { get; set; } = null!;

    private static void GenerateEtl(string etlPath, int events)
    {
        // File-mode session: writes the trace straight to etlPath instead of real-time delivery.
        using (var session = new TraceEventSession("FindNeedle_GenTest_Session", etlPath))
        {
            session.EnableProvider(GenSource.Log.Guid);
            Thread.Sleep(300); // let the session start before we emit
            for (int i = 0; i < events; i++)
                GenSource.Log.Tick(i, $"generated event {i} - payload");
            Thread.Sleep(500); // let the buffers flush to the file
        } // Dispose stops the session and finalizes the .etl
    }

    private static void GenerateTraceLoggingEtl(string etlPath, int events)
    {
        // TraceLogging = self-describing ETW (no manifest; schema travels with each event), emitted
        // via EventSource.Write<T>. Distinct from the manifest path above and common in modern
        // Windows components.
        using var es = new EventSource("FindNeedle-TraceLoggingTest", EventSourceSettings.EtwSelfDescribingEventFormat);
        using (var session = new TraceEventSession("FindNeedle_GenTL_Session", etlPath))
        {
            session.EnableProvider(es.Guid);
            Thread.Sleep(300);
            for (int i = 0; i < events; i++)
                es.Write("Tick", new { id = i, message = $"tracelogging event {i}" });
            Thread.Sleep(500);
        }
    }

    /// <summary>Emit TraceLogging events at distinct severities + event names so we can verify the
    /// TraceEvent-decode path captures Level and TaskName (not just Message). EnableProvider at Verbose
    /// so Error/Warning/Info events are all captured.</summary>
    private static void GenerateTraceLoggingEtlWithLevels(string etlPath, int eventsPerLevel)
    {
        using var es = new EventSource("FindNeedle-TLLevelTest", EventSourceSettings.EtwSelfDescribingEventFormat);
        using (var session = new TraceEventSession("FindNeedle_GenTLLevel_Session", etlPath))
        {
            session.EnableProvider(es.Guid, Microsoft.Diagnostics.Tracing.TraceEventLevel.Verbose);
            Thread.Sleep(300);
            for (int i = 0; i < eventsPerLevel; i++)
            {
                es.Write("ErrorTick", new EventSourceOptions { Level = EventLevel.Error }, new { id = i, message = $"err {i}" });
                es.Write("WarnTick", new EventSourceOptions { Level = EventLevel.Warning }, new { id = i, message = $"warn {i}" });
                es.Write("InfoTick", new EventSourceOptions { Level = EventLevel.Informational }, new { id = i, message = $"info {i}" });
            }
            Thread.Sleep(500);
        }
    }

    /// <summary>Generate an .etl with <paramref name="generate"/>, run it through the full pipeline,
    /// and assert results loaded. Shared by the manifest and TraceLogging cases.</summary>
    private void GenerateThenAssertLoads(string label, Action<string> generate)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"FN_genetl_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var etl = Path.Combine(dir, "generated.etl");
        try
        {
            generate(etl);
            Assert.IsTrue(File.Exists(etl), "a real .etl file should have been generated");
            TestContext.WriteLine($"[{label}] generated .etl: {new FileInfo(etl).Length / 1024.0:F1} KB");

            // full pipeline: FolderLocation + ETLProcessor -> NuSearchQuery -> storage.
            // tracefmt can't decode these non-WPP traces, so ETLProcessor's TraceEvent fallback runs.
            ETWTestUtils.UseTestTraceFmt();

            var loc = new FolderLocation { path = etl };
            loc.SetExtensionProcessorList(new List<IFileExtensionProcessor> { new ETLProcessor() });

            var query = new NuSearchQuery { OverrideStorageType = StorageType.InMemory };
            query.Locations.Add(loc);
            query.RunThrough();

            var storage = query.ResultStorage;
            Assert.IsNotNull(storage, "search should have created result storage");
            int rows = storage!.GetStatistics().filteredRecordCount;
            TestContext.WriteLine($"[{label}] pipeline loaded {rows} results");

            Assert.IsTrue(rows > 0, $"[{label}] the generated .etl should load results via the TraceEvent fallback");

            // Regression guard: the TraceEvent-decode path must populate Message + a real timestamp.
            // (PreLoad used to wipe the message because the json field is empty on that path.)
            var sample = new List<ISearchResult>();
            storage.GetFilteredResultsInBatches(b => { if (sample.Count == 0 && b.Count > 0) sample.Add(b[0]); }, 16);
            Assert.IsTrue(sample.Count > 0, $"[{label}] should be able to read a decoded row back");
            var first = sample[0];
            Assert.IsFalse(string.IsNullOrWhiteSpace(first.GetMessage()),
                $"[{label}] decoded row Message should not be empty (got '{first.GetMessage()}')");
            Assert.AreNotEqual(default(DateTime), first.GetLogTime(),
                $"[{label}] decoded row should have a real timestamp");
            TestContext.WriteLine($"[{label}] sample row: time={first.GetLogTime():o} msg='{first.GetMessage()}'");

            query.DisposeStorage();
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    [TestCategory("Performance")]
    [Timeout(180000)]
    public void GenerateEtl_FullPipeline_LoadsResults()
        => GenerateThenAssertLoads("manifest", etl => GenerateEtl(etl, events: 2000));

    [TestMethod]
    [TestCategory("Performance")]
    [Timeout(180000)]
    public void GenerateTraceLoggingEtl_FullPipeline_LoadsResults()
        => GenerateThenAssertLoads("tracelogging", etl => GenerateTraceLoggingEtl(etl, events: 2000));

    [TestMethod]
    [TestCategory("Performance")]
    [Timeout(180000)]
    public void GenerateTraceLoggingEtl_CapturesLevelAndTaskName()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"FN_tllevel_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var etl = Path.Combine(dir, "leveled.etl");
        try
        {
            GenerateTraceLoggingEtlWithLevels(etl, eventsPerLevel: 300);
            Assert.IsTrue(File.Exists(etl), "a real .etl should have been generated");
            ETWTestUtils.UseTestTraceFmt();

            var loc = new FolderLocation { path = etl };
            loc.SetExtensionProcessorList(new List<IFileExtensionProcessor> { new ETLProcessor() });
            var query = new NuSearchQuery { OverrideStorageType = StorageType.InMemory };
            query.Locations.Add(loc);
            query.RunThrough();

            var rows = new List<ISearchResult>();
            query.ResultStorage!.GetFilteredResultsInBatches(b => rows.AddRange(b), 1000);
            query.DisposeStorage();

            Assert.IsTrue(rows.Count > 0, "the generated TraceLogging .etl should load results");
            var levels = rows.Select(r => r.GetLevel()).Distinct().ToList();
            TestContext.WriteLine($"levels seen: {string.Join(", ", levels)} over {rows.Count} rows");

            // The fix: TraceLogging events must NOT all collapse to Info — Error/Warning events
            // emitted above must come back as Error/Warning.
            Assert.IsTrue(levels.Contains(Level.Error), "Error-level TraceLogging events should parse as Error");
            Assert.IsTrue(levels.Contains(Level.Warning), "Warning-level TraceLogging events should parse as Warning");
            // TaskName must be populated from the event (the event name).
            Assert.IsTrue(rows.Any(r => !string.IsNullOrWhiteSpace(r.GetTaskName())),
                "TaskName should be captured from the TraceLogging event");
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [TestMethod]
    [TestCategory("Performance")]
    [Timeout(180000)]
    public void InspectEtl_ReportsBuildProvidersAndCounts()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"FN_inspect_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var etl = Path.Combine(dir, "inspect.etl");
        try
        {
            GenerateEtl(etl, events: 1500);

            var info = findneedle.ETWPlugin.EtlInfoExtractor.Inspect(etl);
            TestContext.WriteLine(findneedle.ETWPlugin.EtlInfoExtractor.Format(info));

            Assert.IsFalse(string.IsNullOrEmpty(info.OsVersion), "should report the Windows/OS build");
            Assert.IsTrue(info.OsVersion.StartsWith("10.") || info.OsVersion.StartsWith("11."),
                $"OS version looks like a Windows build: {info.OsVersion}");
            Assert.IsTrue(info.PointerSizeBits == 32 || info.PointerSizeBits == 64);
            Assert.IsTrue(info.NumberOfProcessors > 0);
            Assert.IsTrue(info.EventCount > 0, "should have counted events");
            Assert.IsTrue(info.Providers.Keys.Any(p => p.Contains("FindNeedle-GenTest", StringComparison.OrdinalIgnoreCase)),
                "the provider we emitted should be listed");
            Assert.IsTrue(info.ManifestOrTraceLoggingEventCount > 0, "our EventSource events are manifest-format");
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
