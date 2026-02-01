using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using FindPluginCore.Implementations.Storage;
using FindNeedlePluginLib;
using FindNeedlePluginLib.Interfaces;
using FindNeedleCoreUtils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PerformanceTests.Models;
using PerformanceTests.Configuration;
using PerformanceTests.Helpers;
using PerformanceTests.Reporting;

namespace PerformanceTests;

[TestClass]
[DoNotParallelize]
public class AdaptivePerformanceTests
{
    private readonly List<string> _createdDbPaths = new();

    [TestInitialize]
    public void TestInitialize()
    {
        _createdDbPaths.Clear();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        foreach (var path in _createdDbPaths.Distinct())
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
                Thread.Sleep(50);
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }
    }

    private (Func<ISearchStorage> create, Action cleanup) CreateStorageFactory(string kind)
    {
        var searchedFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_{kind}");
        var dbPath = CachedStorage.GetCacheFilePath(searchedFile, ".db");
        _createdDbPaths.Add(dbPath);
        if (File.Exists(dbPath))
            File.Delete(dbPath);

        return kind switch
        {
            "InMemory" => (() => new InMemoryStorage(), () => { }),
            "Sqlite" => (() => new SqliteStorage(searchedFile), () => { }),
            "Hybrid" => (() => new HybridStorage(searchedFile, memoryThresholdMB: PerformanceTestConfig.HybridMemoryThresholdMB), () => { }),
            "HybridCapped" => (() => new HybridStorage(searchedFile, 
                memoryThresholdMB: PerformanceTestConfig.HybridMemoryThresholdMB, 
                maxRecordsInMemory: PerformanceTestConfig.HybridCappedMaxRecords), () => { }),
            _ => throw new ArgumentException("Unknown storage kind: " + kind)
        };
    }

    /// <summary>
    /// Comparative write performance test: writes 2,000,000 records across all storage types.
    /// Each storage type must complete within 80 seconds.
    /// Generates an HTML graph comparing performance degradation.
    /// </summary>
    [TestMethod]
    [TestCategory("Performance")]
    [Timeout(PerformanceTestConfig.TotalTestTimeoutMilliseconds)]
    public void ComparativeWritePerformance_2MillionRecords()
    {
        var results = new Dictionary<string, WriteTestResult>();

        Console.WriteLine("=== COMPARATIVE WRITE PERFORMANCE TEST ===");
        Console.WriteLine($"Target: {PerformanceTestConfig.TotalRecords:N0} records ({PerformanceTestConfig.TotalBatches} batches of {PerformanceTestConfig.BatchSize:N0})");
        Console.WriteLine($"Timeout: {PerformanceTestConfig.TimeoutSeconds}s per storage type");
        Console.WriteLine($"Hybrid: No record cap (memory threshold only)");
        Console.WriteLine($"HybridCapped: {PerformanceTestConfig.HybridCappedMaxRecords:N0} record cap in memory");
        Console.WriteLine();

        // Run test for each storage type
        foreach (var storageKind in PerformanceTestConfig.StorageTypes)
        {
            results[storageKind] = TestStorageType(storageKind);
        }

        // Generate comparison report
        PrintComparisonResults(results);

        // Generate HTML graph
        var htmlPath = PerformanceReportGenerator.GenerateHtmlReport(results, PerformanceTestConfig.TotalRecords);
        Console.WriteLine($"?? Interactive graph generated: {htmlPath}");
        Console.WriteLine($"   Open in browser to view performance comparison");
        Console.WriteLine();

        // Validate all completed within timeout
        ValidateResults(results);

        Console.WriteLine();
        Console.WriteLine("? ALL TESTS PASSED");
    }

    private WriteTestResult TestStorageType(string storageKind)
    {
        Console.WriteLine($"? Testing: {storageKind}");
        Console.WriteLine(new string('?', 60));

        var factory = CreateStorageFactory(storageKind);
        using var storage = factory.create();

        var writeTimings = new List<double>();
        var performanceData = new List<PerformanceDataPoint>();
        
        var overallSw = Stopwatch.StartNew();
        bool timedOut = false;
        int batchesCompleted = 0;
        
        // Track overhead separately
        double totalGetStatisticsTimeMs = 0;
        double totalBatchCreationTimeMs = 0;
        int getStatisticsCallCount = 0;

        // Write all batches with timeout check
        for (int batchNum = 0; batchNum < PerformanceTestConfig.TotalBatches; batchNum++)
        {
            // Check if we're approaching timeout (with 2 second buffer)
            if (overallSw.Elapsed.TotalSeconds > PerformanceTestConfig.TimeoutSeconds - 2)
            {
                timedOut = true;
                Console.WriteLine($"  ? Approaching timeout at batch {batchNum + 1}/{PerformanceTestConfig.TotalBatches}");
                Console.WriteLine($"  ? Completed {batchNum * PerformanceTestConfig.BatchSize:N0} records in {overallSw.Elapsed.TotalSeconds:F2}s");
                break;
            }

            // Measure batch creation time
            var batchCreationSw = Stopwatch.StartNew();
            var batch = Enumerable.Range(0, PerformanceTestConfig.BatchSize)
                .Select(i => (ISearchResult)new DummySearchResult($"{storageKind}{batchNum:D5}-{i:D5}"))
                .ToList();
            batchCreationSw.Stop();
            totalBatchCreationTimeMs += batchCreationSw.Elapsed.TotalMilliseconds;

            // Measure pure write time
            var sw = Stopwatch.StartNew();
            storage.AddRawBatch(batch);
            sw.Stop();

            var writeTime = sw.Elapsed.TotalMilliseconds;
            writeTimings.Add(writeTime);
            batchesCompleted = batchNum + 1;

            // Record data point every N batches for graphing
            if (batchNum % PerformanceTestConfig.StatisticsSampleInterval == 0 || batchNum == PerformanceTestConfig.TotalBatches - 1)
            {
                // Measure GetStatistics time
                var statsSw = Stopwatch.StartNew();
                var stats = storage.GetStatistics();
                statsSw.Stop();
                
                var statsTime = statsSw.Elapsed.TotalMilliseconds;
                totalGetStatisticsTimeMs += statsTime;
                getStatisticsCallCount++;
                
                performanceData.Add(new PerformanceDataPoint
                {
                    BatchNumber = batchNum + 1,
                    TotalRecords = (batchNum + 1) * PerformanceTestConfig.BatchSize,
                    WriteTimeMs = writeTime,
                    MemoryMB = stats.sizeInMemory / 1024.0 / 1024.0,
                    DiskMB = stats.sizeOnDisk / 1024.0 / 1024.0
                });
            }

            // Progress every N batches
            if ((batchNum + 1) % PerformanceTestConfig.ProgressReportInterval == 0)
            {
                var elapsed = overallSw.Elapsed.TotalSeconds;
                var recordsWritten = (batchNum + 1) * PerformanceTestConfig.BatchSize;
                var remainingTime = PerformanceTestConfig.TimeoutSeconds - elapsed;
                Console.WriteLine($"  [{batchNum + 1}/{PerformanceTestConfig.TotalBatches}] {recordsWritten:N0} records, {elapsed:F1}s elapsed, {remainingTime:F1}s remaining");
            }
        }

        overallSw.Stop();

        // Calculate statistics
        var finalStats = storage.GetStatistics();
        var baseline = StatisticsHelper.GetBaseline(writeTimings);
        var actualRecords = batchesCompleted * PerformanceTestConfig.BatchSize;
        
        // Calculate time breakdown
        var totalWriteTimeMs = writeTimings.Sum();
        var totalElapsedMs = overallSw.Elapsed.TotalMilliseconds;
        var otherOverheadMs = totalElapsedMs - totalWriteTimeMs - totalGetStatisticsTimeMs - totalBatchCreationTimeMs;
        
        var result = new WriteTestResult
        {
            StorageKind = storageKind,
            TotalRecords = actualRecords,
            TotalBatches = batchesCompleted,
            TotalTimeSeconds = overallSw.Elapsed.TotalSeconds,
            AverageWriteMs = writeTimings.Average(),
            MedianWriteMs = StatisticsHelper.GetMedian(writeTimings),
            MinWriteMs = writeTimings.Min(),
            MaxWriteMs = writeTimings.Max(),
            BaselineWriteMs = baseline,
            FinalMemoryMB = finalStats.sizeInMemory / 1024.0 / 1024.0,
            FinalDiskMB = finalStats.sizeOnDisk / 1024.0 / 1024.0,
            PerformanceData = performanceData,
            TimedOut = timedOut,
            TotalWriteTimeMs = totalWriteTimeMs,
            TotalGetStatisticsTimeMs = totalGetStatisticsTimeMs,
            GetStatisticsCallCount = getStatisticsCallCount,
            TotalBatchCreationTimeMs = totalBatchCreationTimeMs,
            OtherOverheadMs = otherOverheadMs
        };

        PrintTestResult(result);
        factory.cleanup();
        
        return result;
    }

    private void PrintTestResult(WriteTestResult result)
    {
        if (result.TimedOut)
        {
            Console.WriteLine($"? {result.StorageKind} timed out at {result.TotalTimeSeconds:F2}s ({result.TotalRecords:N0} records)");
        }
        else
        {
            Console.WriteLine($"? {result.StorageKind} completed in {result.TotalTimeSeconds:F2}s");
        }
        
        Console.WriteLine($"  Avg: {result.AverageWriteMs:F2}ms, Median: {result.MedianWriteMs:F2}ms, Baseline: {result.BaselineWriteMs:F2}ms");
        Console.WriteLine();
        Console.WriteLine($"  Time Breakdown:");
        Console.WriteLine($"    Pure writes:      {result.TotalWriteTimeMs / 1000.0,8:F2}s ({result.TotalWriteTimeMs / (result.TotalTimeSeconds * 1000) * 100,5:F1}%)");
        Console.WriteLine($"    GetStatistics:    {result.TotalGetStatisticsTimeMs / 1000.0,8:F2}s ({result.TotalGetStatisticsTimeMs / (result.TotalTimeSeconds * 1000) * 100,5:F1}%) - {result.GetStatisticsCallCount} calls");
        Console.WriteLine($"    Batch creation:   {result.TotalBatchCreationTimeMs / 1000.0,8:F2}s ({result.TotalBatchCreationTimeMs / (result.TotalTimeSeconds * 1000) * 100,5:F1}%)");
        Console.WriteLine($"    Other overhead:   {result.OtherOverheadMs / 1000.0,8:F2}s ({result.OtherOverheadMs / (result.TotalTimeSeconds * 1000) * 100,5:F1}%)");
        Console.WriteLine();
    }

    private void PrintComparisonResults(Dictionary<string, WriteTestResult> results)
    {
        Console.WriteLine();
        Console.WriteLine("???????????????????????????????????????");
        Console.WriteLine("              COMPARATIVE RESULTS");
        Console.WriteLine("???????????????????????????????????????");
        Console.WriteLine();

        Console.WriteLine($"{"Storage",-12} {"Status",-12} {"Total Time",-12} {"Records",-12} {"Avg Write",-12} {"Median",-12}");
        Console.WriteLine(new string('?', 80));
        foreach (var result in results.Values.OrderBy(r => r.TotalTimeSeconds))
        {
            var status = result.TimedOut ? "TIMEOUT" : "PASS";
            Console.WriteLine($"{result.StorageKind,-12} {status,-12} {result.TotalTimeSeconds,8:F2}s     {result.TotalRecords,10:N0}  {result.AverageWriteMs,8:F2}ms   {result.MedianWriteMs,8:F2}ms");
        }
        Console.WriteLine();
    }

    private void ValidateResults(Dictionary<string, WriteTestResult> results)
    {
        foreach (var result in results.Values)
        {
            if (result.TimedOut)
            {
                Assert.Fail($"{result.StorageKind} exceeded timeout: {result.TotalTimeSeconds:F2}s > {PerformanceTestConfig.TimeoutSeconds}s (completed {result.TotalRecords:N0}/{PerformanceTestConfig.TotalRecords:N0} records)");
            }
            else
            {
                Assert.IsTrue(result.TotalTimeSeconds <= PerformanceTestConfig.TimeoutSeconds,
                    $"{result.StorageKind} exceeded timeout: {result.TotalTimeSeconds:F2}s > {PerformanceTestConfig.TimeoutSeconds}s");
                Console.WriteLine($"? {result.StorageKind} passed: {result.TotalTimeSeconds:F2}s ? {PerformanceTestConfig.TimeoutSeconds}s");
            }
        }
    }
}
