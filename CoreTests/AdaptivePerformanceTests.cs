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

namespace CoreTests;

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

    private class DummySearchResult : ISearchResult
    {
        public static readonly DateTime FixedTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private readonly string _message;

        public DummySearchResult(string message = "TestMessage")
        {
            _message = message;
        }

        public DateTime GetLogTime() => FixedTime;
        public string GetMachineName() => "TestMachine";
        public void WriteToConsole() { }
        public Level GetLevel() => Level.Error;
        public string GetUsername() => "TestUser";
        public string GetTaskName() => "TestTask";
        public string GetOpCode() => "TestOp";
        public string GetSource() => "TestSource";
        public string GetSearchableData() => "TestData";
        public string GetMessage() => _message;
        public string GetResultSource() => "TestSource";
    }

    private (Func<ISearchStorage> create, Action cleanup) CreateStorageFactory(string kind)
    {
        var searchedFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var dbPath = CachedStorage.GetCacheFilePath(searchedFile, ".db");
        _createdDbPaths.Add(dbPath);
        if (File.Exists(dbPath))
            File.Delete(dbPath);

        return kind switch
        {
            "InMemory" => (() => new InMemoryStorage(), () => { }),
            "Sqlite" => (() => new SqliteStorage(searchedFile), () => { }),
            "Hybrid" => (() => new HybridStorage(searchedFile, memoryThresholdMB: 100), () => { }),
            _ => throw new ArgumentException("Unknown storage kind: " + kind)
        };
    }

    /// <summary>
    /// Adaptive performance test that continues inserting until write performance degrades beyond threshold.
    /// Measures both write and read performance degradation.
    /// </summary>
    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    [TestCategory("Performance")]
    [Timeout(300000)] // 5 minute timeout
    public void AdaptivePerformance_UntilDegradation(string kind)
    {
        var factory = CreateStorageFactory(kind);
        using var storage = factory.create();

        const int batchSize = 1000;
        const int warmupBatches = 10; // Establish baseline
        const double degradationThreshold = 2.0; // 2x slower than baseline
        const int maxBatches = 10000; // Safety limit
        const int checkInterval = 10; // Check every N batches

        var writeTimings = new List<double>();
        var readTimings = new List<double>();
        double baselineWriteTime = 0;
        double baselineReadTime = 0;
        int totalRecords = 0;
        bool degraded = false;
        string degradationReason = "";

        Console.WriteLine($"=== Adaptive Performance Test: {kind} ===");
        Console.WriteLine($"Batch Size: {batchSize:N0}");
        Console.WriteLine($"Degradation Threshold: {degradationThreshold}x baseline");
        Console.WriteLine($"Warmup Batches: {warmupBatches}");
        Console.WriteLine();

        var overallSw = Stopwatch.StartNew();

        for (int batchNum = 0; batchNum < maxBatches && !degraded; batchNum++)
        {
            // Create batch
            var batch = Enumerable.Range(0, batchSize)
                .Select(i => (ISearchResult)new DummySearchResult($"Batch{batchNum:D5}-Record{i:D5}"))
                .ToList();

            // Measure write time
            var writeSw = Stopwatch.StartNew();
            storage.AddRawBatch(batch);
            writeSw.Stop();
            var writeTime = writeSw.Elapsed.TotalMilliseconds;
            writeTimings.Add(writeTime);
            totalRecords += batchSize;

            // Establish baseline after warmup
            if (batchNum == warmupBatches - 1)
            {
                baselineWriteTime = writeTimings.Average();
                Console.WriteLine($"Baseline established after {warmupBatches} batches:");
                Console.WriteLine($"  - Baseline write time: {baselineWriteTime:F2} ms/batch");
                Console.WriteLine($"  - Records so far: {totalRecords:N0}");
                Console.WriteLine();
            }

            // Check for degradation every N batches after warmup
            if (batchNum >= warmupBatches && (batchNum + 1) % checkInterval == 0)
            {
                // Get recent average (last checkInterval batches)
                var recentWrites = writeTimings.Skip(writeTimings.Count - checkInterval).Average();
                
                // Measure read performance
                var readSw = Stopwatch.StartNew();
                int readCount = 0;
                storage.GetRawResultsInBatches(b => readCount += b.Count, batchSize);
                readSw.Stop();
                var readTime = readSw.Elapsed.TotalMilliseconds;
                readTimings.Add(readTime);

                // Check read baseline
                if (readTimings.Count == 1)
                {
                    baselineReadTime = readTime;
                    Console.WriteLine($"Read baseline established: {baselineReadTime:F2} ms");
                }

                // Check for write degradation
                if (recentWrites > baselineWriteTime * degradationThreshold)
                {
                    degraded = true;
                    degradationReason = $"Write performance degraded: {recentWrites:F2} ms > {baselineWriteTime * degradationThreshold:F2} ms ({degradationThreshold}x baseline)";
                }

                // Check for read degradation
                if (readTimings.Count > 1 && readTime > baselineReadTime * degradationThreshold)
                {
                    degraded = true;
                    degradationReason += (degraded ? " AND " : "") + 
                        $"Read performance degraded: {readTime:F2} ms > {baselineReadTime * degradationThreshold:F2} ms ({degradationThreshold}x baseline)";
                }

                // Progress report
                var stats = storage.GetStatistics();
                Console.WriteLine($"Batch {batchNum + 1:N0}: {totalRecords:N0} records");
                Console.WriteLine($"  Write: {writeTime:F2} ms (avg: {recentWrites:F2} ms, baseline: {baselineWriteTime:F2} ms, ratio: {recentWrites / baselineWriteTime:F2}x)");
                Console.WriteLine($"  Read:  {readTime:F2} ms (baseline: {baselineReadTime:F2} ms, ratio: {(baselineReadTime > 0 ? readTime / baselineReadTime : 0):F2}x)");
                Console.WriteLine($"  Memory: {stats.sizeInMemory / 1024.0 / 1024.0:F2} MB, Disk: {stats.sizeOnDisk / 1024.0 / 1024.0:F2} MB");
                
                if (degraded)
                {
                    Console.WriteLine($"  *** DEGRADATION DETECTED ***");
                }
                Console.WriteLine();
            }
        }

        overallSw.Stop();

        // Final statistics
        var finalStats = storage.GetStatistics();
        Console.WriteLine($"=== Test Complete: {(degraded ? "Degradation Detected" : "Max Batches Reached")} ===");
        if (degraded)
        {
            Console.WriteLine($"Reason: {degradationReason}");
        }
        Console.WriteLine($"Total Records: {totalRecords:N0}");
        Console.WriteLine($"Total Time: {overallSw.Elapsed.TotalSeconds:F2}s");
        Console.WriteLine($"Final Memory: {finalStats.sizeInMemory / 1024.0 / 1024.0:F2} MB");
        Console.WriteLine($"Final Disk: {finalStats.sizeOnDisk / 1024.0 / 1024.0:F2} MB");
        Console.WriteLine();

        // Statistical analysis
        Console.WriteLine("=== Performance Analysis ===");
        Console.WriteLine($"Write Timings:");
        Console.WriteLine($"  Min:    {writeTimings.Min():F2} ms");
        Console.WriteLine($"  Max:    {writeTimings.Max():F2} ms");
        Console.WriteLine($"  Mean:   {writeTimings.Average():F2} ms");
        Console.WriteLine($"  Median: {GetMedian(writeTimings):F2} ms");
        Console.WriteLine($"  StdDev: {GetStandardDeviation(writeTimings):F2} ms");
        
        if (readTimings.Count > 0)
        {
            Console.WriteLine($"Read Timings:");
            Console.WriteLine($"  Min:    {readTimings.Min():F2} ms");
            Console.WriteLine($"  Max:    {readTimings.Max():F2} ms");
            Console.WriteLine($"  Mean:   {readTimings.Average():F2} ms");
            Console.WriteLine($"  Median: {GetMedian(readTimings):F2} ms");
            Console.WriteLine($"  StdDev: {GetStandardDeviation(readTimings):F2} ms");
        }

        // Assertions
        Assert.IsTrue(totalRecords >= warmupBatches * batchSize, "Should complete at least warmup phase");
        Assert.IsTrue(baselineWriteTime > 0, "Should establish write baseline");
        Assert.IsTrue(baselineReadTime > 0, "Should establish read baseline");

        factory.cleanup();
    }

    /// <summary>
    /// More aggressive test - continues until operations are 5x slower, useful for stress testing.
    /// </summary>
    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    [TestCategory("Performance")]
    [TestCategory("Stress")]
    [Timeout(600000)] // 10 minute timeout
    public void StressTest_UntilSevereDegradation(string kind)
    {
        var factory = CreateStorageFactory(kind);
        using var storage = factory.create();

        const int batchSize = 5000;
        const int warmupBatches = 5;
        const double degradationThreshold = 5.0; // 5x slower
        const int maxBatches = 5000;

        var writeTimings = new List<double>();
        double baselineWriteTime = 0;
        int totalRecords = 0;
        bool degraded = false;

        Console.WriteLine($"=== Stress Test: {kind} ===");
        Console.WriteLine($"This test will continue until write performance degrades to {degradationThreshold}x baseline");
        Console.WriteLine();

        for (int batchNum = 0; batchNum < maxBatches && !degraded; batchNum++)
        {
            var batch = Enumerable.Range(0, batchSize)
                .Select(i => (ISearchResult)new DummySearchResult($"Stress{batchNum:D5}-{i:D5}"))
                .ToList();

            var sw = Stopwatch.StartNew();
            storage.AddRawBatch(batch);
            sw.Stop();
            
            writeTimings.Add(sw.Elapsed.TotalMilliseconds);
            totalRecords += batchSize;

            if (batchNum == warmupBatches - 1)
            {
                baselineWriteTime = writeTimings.Average();
                Console.WriteLine($"Baseline: {baselineWriteTime:F2} ms/batch ({totalRecords:N0} records)");
            }

            if (batchNum >= warmupBatches && batchNum % 5 == 0)
            {
                var recent = writeTimings.Skip(writeTimings.Count - 5).Average();
                if (recent > baselineWriteTime * degradationThreshold)
                {
                    degraded = true;
                    Console.WriteLine($"SEVERE DEGRADATION at {totalRecords:N0} records: {recent:F2} ms ({recent / baselineWriteTime:F2}x baseline)");
                }
                else
                {
                    Console.WriteLine($"{totalRecords:N0} records: {sw.Elapsed.TotalMilliseconds:F2} ms ({recent / baselineWriteTime:F2}x baseline)");
                }
            }
        }

        Console.WriteLine($"\nTest completed with {totalRecords:N0} records");
        Console.WriteLine($"Final write time: {writeTimings.Last():F2} ms ({writeTimings.Last() / baselineWriteTime:F2}x baseline)");

        factory.cleanup();
    }

    /// <summary>
    /// Measures throughput degradation - records per second.
    /// </summary>
    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    [TestCategory("Performance")]
    [Timeout(300000)]
    public void ThroughputDegradation_RecordsPerSecond(string kind)
    {
        var factory = CreateStorageFactory(kind);
        using var storage = factory.create();

        const int batchSize = 1000;
        const int warmupBatches = 10;
        const double degradationThreshold = 0.5; // Drop to 50% of baseline throughput
        const int maxBatches = 10000;

        var throughputs = new List<double>(); // records per second
        double baselineThroughput = 0;
        int totalRecords = 0;
        bool degraded = false;

        Console.WriteLine($"=== Throughput Degradation Test: {kind} ===");
        Console.WriteLine($"Will stop when throughput drops below {degradationThreshold * 100}% of baseline");
        Console.WriteLine();

        for (int batchNum = 0; batchNum < maxBatches && !degraded; batchNum++)
        {
            var batch = Enumerable.Range(0, batchSize)
                .Select(i => (ISearchResult)new DummySearchResult($"TP{batchNum:D5}-{i:D5}"))
                .ToList();

            var sw = Stopwatch.StartNew();
            storage.AddRawBatch(batch);
            sw.Stop();

            double throughput = batchSize / sw.Elapsed.TotalSeconds;
            throughputs.Add(throughput);
            totalRecords += batchSize;

            if (batchNum == warmupBatches - 1)
            {
                baselineThroughput = throughputs.Average();
                Console.WriteLine($"Baseline throughput: {baselineThroughput:N0} records/sec");
            }

            if (batchNum >= warmupBatches && batchNum % 10 == 0)
            {
                var recentThroughput = throughputs.Skip(throughputs.Count - 10).Average();
                var ratio = recentThroughput / baselineThroughput;

                Console.WriteLine($"{totalRecords:N0} records: {throughput:N0} rec/sec (recent avg: {recentThroughput:N0}, {ratio:P0} of baseline)");

                if (ratio < degradationThreshold)
                {
                    degraded = true;
                    Console.WriteLine($"\n*** THROUGHPUT DEGRADATION DETECTED ***");
                    Console.WriteLine($"Dropped to {ratio:P0} of baseline ({recentThroughput:N0} vs {baselineThroughput:N0} rec/sec)");
                }
            }
        }

        Console.WriteLine($"\nCompleted with {totalRecords:N0} records");
        Console.WriteLine($"Final throughput: {throughputs.Last():N0} rec/sec ({throughputs.Last() / baselineThroughput:P0} of baseline)");

        Assert.IsTrue(totalRecords >= warmupBatches * batchSize);
        Assert.IsTrue(baselineThroughput > 0);

        factory.cleanup();
    }

    // Helper methods for statistics
    private static double GetMedian(List<double> values)
    {
        var sorted = values.OrderBy(x => x).ToList();
        int n = sorted.Count;
        if (n == 0) return 0;
        if (n % 2 == 1)
            return sorted[n / 2];
        return (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }

    private static double GetStandardDeviation(List<double> values)
    {
        if (values.Count < 2) return 0;
        double avg = values.Average();
        double sumSquares = values.Sum(v => Math.Pow(v - avg, 2));
        return Math.Sqrt(sumSquares / (values.Count - 1));
    }
}
