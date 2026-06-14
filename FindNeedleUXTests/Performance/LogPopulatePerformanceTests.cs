using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BasicTextPlugin;
using FindNeedlePluginLib;
using findneedle.Implementations;
using FindPluginCore.PluginSubsystem;
using FindPluginCore.Searching;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Performance;

/// <summary>
/// Measures the real "open a log → results populated" cost (what happens before you can scroll),
/// broken into its parts so it's clear where the time goes:
///   1. parse only        — FolderLocation + PlainTextProcessor reads + parses the file (CPU/IO read)
///   2. full → InMemory    — full NuSearchQuery.RunThrough storing to RAM (parse + filter + RAM store)
///   3. full → SQLite      — same but storing to the on-disk cache (parse + filter + disk write + FTS)
/// The InMemory-vs-SQLite delta isolates the disk-write + FTS-index cost, which is the slow part on
/// a slow/AV-scanned disk. [TestCategory("Performance")] → local-only; real numbers in the output.
/// </summary>
[TestClass]
[TestCategory("Performance")]
[DoNotParallelize]
public class LogPopulatePerformanceTests
{
    private const int LineCount = 100_000;

    public TestContext TestContext { get; set; } = null!;

    private string _dir = null!;
    private string _logPath = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"FN_pop_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _logPath = Path.Combine(_dir, "big.log");

        var genSw = Stopwatch.StartNew();
        using (var w = new StreamWriter(_logPath))
        {
            string[] levels = { "INFO", "ERROR", "WARNING", "DEBUG", "INFO" };
            var baseTime = new DateTime(2024, 1, 1, 0, 0, 0);
            for (int i = 0; i < LineCount; i++)
            {
                var t = baseTime.AddSeconds(i).ToString("yyyy-MM-dd HH:mm:ss");
                w.WriteLine($"[{t}] {levels[i % levels.Length]}: event {i} - some representative log message payload");
            }
        }
        genSw.Stop();
        TestContext.WriteLine($"Generated {LineCount:N0}-line log ({new FileInfo(_logPath).Length / 1024.0 / 1024.0:F1} MB) in {genSw.ElapsedMilliseconds:N0} ms");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private FolderLocation FreshLocation()
    {
        var loc = new FolderLocation { path = _logPath };
        loc.SetExtensionProcessorList(new List<IFileExtensionProcessor> { new PlainTextProcessor() });
        return loc;
    }

    private static double Rate(int rows, double ms) => ms > 0 ? rows / (ms / 1000.0) : 0;

    [TestMethod]
    public void OpenLog_PopulateCost_Breakdown()
    {
        // 1) Parse only — read + parse the file into result objects, no search pipeline / storage.
        var loc = FreshLocation();
        var sw = Stopwatch.StartNew();
        loc.LoadInMemory();
        var parsed = loc.Search();
        sw.Stop();
        var parseMs = sw.Elapsed.TotalMilliseconds;
        Assert.AreEqual(LineCount, parsed.Count, "every line should parse");

        // 2) Full search → InMemory storage.
        var qMem = new NuSearchQuery { OverrideStorageType = StorageType.InMemory };
        qMem.Locations.Add(FreshLocation());
        sw.Restart();
        qMem.RunThrough();
        sw.Stop();
        var memMs = sw.Elapsed.TotalMilliseconds;
        var memRows = qMem.ResultStorage!.GetStatistics().filteredRecordCount;
        qMem.DisposeStorage();

        // 3) Full search → SQLite storage (on-disk cache + FTS index).
        var qSql = new NuSearchQuery { OverrideStorageType = StorageType.SqlLite };
        qSql.Locations.Add(FreshLocation());
        sw.Restart();
        qSql.RunThrough();
        sw.Stop();
        var sqlMs = sw.Elapsed.TotalMilliseconds;
        var sqlRows = qSql.ResultStorage!.GetStatistics().filteredRecordCount;
        qSql.DisposeStorage();

        TestContext.WriteLine($"parse only          {parseMs,8:F0} ms   {Rate(parsed.Count, parseMs),9:N0} rows/s");
        TestContext.WriteLine($"full -> InMemory    {memMs,8:F0} ms   {Rate(memRows, memMs),9:N0} rows/s   ({memRows:N0} rows)");
        TestContext.WriteLine($"full -> SQLite      {sqlMs,8:F0} ms   {Rate(sqlRows, sqlMs),9:N0} rows/s   ({sqlRows:N0} rows)");
        TestContext.WriteLine($"  └ disk+FTS store overhead (SQLite - InMemory) ≈ {sqlMs - memMs,8:F0} ms");

        Assert.AreEqual(LineCount, memRows, "InMemory populate should hold every row");
        Assert.AreEqual(LineCount, sqlRows, "SQLite populate should hold every row");
    }
}
