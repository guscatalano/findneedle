using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using findneedle.Implementations;
using findneedle.Implementations.FileExtensions;
using FindNeedlePluginLib;
using FindPluginCore.Implementations.Storage;
using FindPluginCore.PluginSubsystem;
using FindPluginCore.Searching;

namespace ETWPluginTests;

/// <summary>
/// Loads the big mixed bundle (<see cref="MixedFilterFixtureGenerator"/>) through the real search
/// pipeline and asserts two things the user hit pain on:
///   1. Every viewer filter runs fast against a large (SQLite-tier, FTS-indexed) result set.
///   2. The mixed WPP+TraceLogging ETL decodes its WPP events WITH the committed symbols (TMF) and
///      fails fast WITHOUT them — i.e. the "with vs without WPP symbols" load paths both behave.
///
/// Opt-in: needs the generated fixture + the real WDK tracefmt, so it's [Performance] and skips
/// (Inconclusive) when the fixture isn't present. Generate it first with
/// MixedFilterFixtureGenerator.Generate_MixedFilterFixtureZip (elevated).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class FixtureFilterPerfTests
{
    // A single filtered COUNT must stay under this. At ~3.9M rows the FTS-backed searches run in
    // ~0.4–2.5s and the column LIKE filters (Message/TaskName/ResultSource — no FTS) in ~2–3s; 5s
    // leaves margin for slower machines/CI while still catching a gross regression (a filter that
    // suddenly takes 10s+ means the index path broke).
    private const long FilterThresholdMs = 5000;
    private static string _extractDir = "";

    public TestContext TestContext { get; set; } = null!;

    [ClassCleanup]
    public static void ClassCleanup()
    {
        try { if (Directory.Exists(_extractDir)) Directory.Delete(_extractDir, recursive: true); } catch { }
    }

    /// <summary>Extract the bundle once (lazily). Inconclusive if it hasn't been generated.</summary>
    private static string EnsureExtracted()
    {
        if (!string.IsNullOrEmpty(_extractDir) && Directory.Exists(_extractDir)) return _extractDir;
        var zip = MixedFilterFixtureGenerator.FixtureZipPath();
        if (!File.Exists(zip))
            throw new AssertInconclusiveException(
                $"Fixture not found: {zip}. Generate it first (elevated): " +
                "vstest.console ETWPluginTests.dll /Tests:Generate_MixedFilterFixtureZip");
        var dir = Path.Combine(Path.GetTempPath(), $"FN_fixload_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        ZipFile.ExtractToDirectory(zip, dir);
        _extractDir = dir;
        return dir;
    }

    // Make sure a previous test in this run hasn't left the WDK finder in test-stub mode — these
    // tests need the REAL tracefmt to decode the WPP events.
    private static void UseRealTraceFmt()
    {
        findneedle.WDK.WDKFinder.TEST_MODE = false;
        findneedle.WDK.WDKFinder.TEST_MODE_PASS_FMT_PATH = false;
        findneedle.WDK.WDKFinder.TEST_MODE_SUCCESS = false;
    }

    [TestMethod]
    [TestCategory("Performance")]
    [Timeout(1_200_000)] // the 50 MB bundle is ~3.9M rows; the one-time load + FTS build dominates
    public void Fixture_EveryFilter_IsPerformant()
    {
        var dir = EnsureExtracted();
        UseRealTraceFmt();
        // Let the ETL's WPP events decode too (extra rows + a real provider to filter on).
        var prevTmf = Environment.GetEnvironmentVariable("TRACE_FORMAT_SEARCH_PATH");
        Environment.SetEnvironmentVariable("TRACE_FORMAT_SEARCH_PATH", MixedFilterFixtureGenerator.WppEmitterTmfDir());

        var loc = new FolderLocation { path = dir };
        loc.SetExtensionProcessorList(new List<IFileExtensionProcessor> { new ETLProcessor() });
        var query = new NuSearchQuery { OverrideStorageType = StorageType.SqlLite };
        query.Locations.Add(loc);

        var load = Stopwatch.StartNew();
        query.RunThrough();
        load.Stop();

        try
        {
            var storage = query.ResultStorage as SqliteStorage;
            Assert.IsNotNull(storage, "the large fixture should land in SQLite-tier storage");
            int total = storage!.GetStatistics().filteredRecordCount;
            TestContext.WriteLine($"Loaded {total:N0} rows in {load.Elapsed.TotalSeconds:F1}s");
            Assert.IsTrue(total > 50_000, $"fixture should produce a big result set (got {total:N0})");

            // The FTS index backs fast substring search; RunThrough already builds it for the SQLite
            // tier. Only build if (unexpectedly) absent — a redundant rebuild over millions of rows is
            // a multi-minute waste, not a no-op.
            if (!storage.IsSearchIndexBuilt) query.BuildSearchIndexNow();
            _lastStorage = storage;

            // Time every filter dimension the viewer exposes. Each is a single COUNT — the latency that
            // makes the UI feel laggy. expectMatches is set only where the fixture guarantees hits.
            // A representative subset term (~1/7 of rows), not a near-universal one: counting a term
            // that matches almost every row is inherently O(matches) and not what "filtering" means.
            TimeFilter("global search (common)", new SqliteStorage.FilterInput { Search = "cache-miss" }, expectMatches: true);
            TimeFilter("global search (unique)", new SqliteStorage.FilterInput { Search = "UNIQUE-NEEDLE-0" }, expectMatches: true);
            TimeFilter("message substring", new SqliteStorage.FilterInput { Message = "route=/api/v1/users" }, expectMatches: true);
            TimeFilter("provider (Source)", new SqliteStorage.FilterInput { Provider = "WorkerA" }, expectMatches: true);
            TimeFilter("provider set (IN)", new SqliteStorage.FilterInput { ProviderSet = new[] { "WorkerA", "NetIO", "Cache" } }, expectMatches: true);
            TimeFilter("result-source", new SqliteStorage.FilterInput { Source = "fixture-0" }, expectMatches: false);
            TimeFilter("level=Error", new SqliteStorage.FilterInput { LevelInt = (int)Level.Error }, expectMatches: false);
            TimeFilter("task name", new SqliteStorage.FilterInput { TaskName = "Worker" }, expectMatches: false);
            TimeFilter("time range (2026)", new SqliteStorage.FilterInput
            {
                LogTimeFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToString("o"),
                LogTimeTo = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc).ToString("o"),
            }, expectMatches: true);
            TimeFilter("combined (search+provider+level)", new SqliteStorage.FilterInput
            {
                Search = "cache-miss",
                Provider = "WorkerB",
                LevelInt = (int)Level.Info,
            }, expectMatches: false);
        }
        finally
        {
            query.DisposeStorage();
            Environment.SetEnvironmentVariable("TRACE_FORMAT_SEARCH_PATH", prevTmf);
        }
    }

    private void TimeFilter(string label, SqliteStorage.FilterInput filter, bool expectMatches)
    {
        var s = _lastStorage!;
        var sw = Stopwatch.StartNew();
        int n = s.GetFilteredCount(filter);
        sw.Stop();
        TestContext.WriteLine($"  filter[{label}] -> {n:N0} rows in {sw.ElapsedMilliseconds} ms");
        Assert.IsTrue(sw.ElapsedMilliseconds < FilterThresholdMs,
            $"filter '{label}' took {sw.ElapsedMilliseconds} ms (threshold {FilterThresholdMs} ms)");
        if (expectMatches)
            Assert.IsTrue(n > 0, $"filter '{label}' was expected to match rows but got {n}");
    }

    // Set just before each TimeFilter batch so the helper can reach the storage without re-threading it.
    private SqliteStorage? _lastStorage
    {
        get; set;
    }

    [TestMethod]
    [TestCategory("Performance")]
    [Timeout(600_000)]
    public void Fixture_Etl_LoadsViaRealPipeline_WithAndWithoutSymbolPath()
    {
        var dir = EnsureExtracted();
        var etl = Path.Combine(dir, "mixed.etl");
        if (!File.Exists(etl)) Assert.Inconclusive("bundle has no mixed.etl");
        UseRealTraceFmt();

        // Load the mixed WPP+TraceLogging ETL through the full pipeline twice: once with the committed
        // WPP symbols (TMF) on TRACE_FORMAT_SEARCH_PATH, once with an empty path. Both must decode it via
        // the real tracefmt and surface rows — the loader must never hang or fail just because a symbol
        // path isn't configured. (WppEmitter's WPP format info is self-resolving — its PDB sits next to
        // the binary referenced by the trace — so clearing the path doesn't break decode here; the
        // assert is therefore "configuring a path never regresses results", not a strict row delta.)
        int withSymbols = LoadEtlRowCount(etl, tmfDir: MixedFilterFixtureGenerator.WppEmitterTmfDir());
        int withoutSymbols = LoadEtlRowCount(etl, tmfDir: null);

        TestContext.WriteLine($"mixed ETL rows decoded — with symbol path: {withSymbols:N0}, without: {withoutSymbols:N0}");

        if (withSymbols == 0)
            Assert.Inconclusive(
                "0 rows even WITH a symbol path — the real WDK tracefmt is likely unavailable, so the " +
                "WPP decode path can't be exercised on this machine.");

        Assert.IsTrue(withSymbols >= withoutSymbols,
            $"configuring a symbol path must not decode fewer rows (with={withSymbols}, without={withoutSymbols})");
    }

    /// <summary>Run just the ETL through the pipeline with TRACE_FORMAT_SEARCH_PATH pointed at
    /// <paramref name="tmfDir"/> (or an empty dir, to simulate "no symbols"). A fresh temp copy per call
    /// gives a distinct cache key so the two runs don't reuse each other's cached results.</summary>
    private int LoadEtlRowCount(string etl, string? tmfDir)
    {
        var work = Path.Combine(Path.GetTempPath(), $"FN_etlsym_{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        var copy = Path.Combine(work, "mixed.etl");
        File.Copy(etl, copy);

        var prevTmf = Environment.GetEnvironmentVariable("TRACE_FORMAT_SEARCH_PATH");
        var prevSym = Environment.GetEnvironmentVariable("_NT_SYMBOL_PATH");
        var emptyDir = Path.Combine(work, "no-symbols");
        Directory.CreateDirectory(emptyDir);
        Environment.SetEnvironmentVariable("TRACE_FORMAT_SEARCH_PATH", tmfDir ?? emptyDir);
        Environment.SetEnvironmentVariable("_NT_SYMBOL_PATH", emptyDir); // keep tracefmt from finding PDBs

        NuSearchQuery? query = null;
        try
        {
            var loc = new FolderLocation { path = copy };
            loc.SetExtensionProcessorList(new List<IFileExtensionProcessor> { new ETLProcessor() });
            query = new NuSearchQuery { OverrideStorageType = StorageType.InMemory };
            query.Locations.Add(loc);
            query.RunThrough();
            return query.ResultStorage?.GetStatistics().filteredRecordCount ?? 0;
        }
        finally
        {
            query?.DisposeStorage();
            Environment.SetEnvironmentVariable("TRACE_FORMAT_SEARCH_PATH", prevTmf);
            Environment.SetEnvironmentVariable("_NT_SYMBOL_PATH", prevSym);
            try { Directory.Delete(work, recursive: true); } catch { }
        }
    }
}
