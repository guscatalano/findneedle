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
    /// Comparative write performance test: writes 3,000,000 records across all storage types.
    /// Each storage type must complete within 80 seconds.
    /// Generates an HTML graph comparing performance degradation.
    /// </summary>
    [TestMethod]
    [TestCategory("Performance")]
    [Timeout(360000)] // 6 minute total timeout for all three tests (80s each + overhead)
    public void ComparativeWritePerformance_3MillionRecords()
    {
        const int totalRecords = 3_000_000;
        const int batchSize = 5000;
        const int totalBatches = totalRecords / batchSize; // 600 batches
        const double timeoutSeconds = 80.0;

        var storageTypes = new[] { "Sqlite", "Hybrid", "InMemory" };
        var results = new Dictionary<string, WriteTestResult>();

        Console.WriteLine("=== COMPARATIVE WRITE PERFORMANCE TEST ===");
        Console.WriteLine($"Target: {totalRecords:N0} records ({totalBatches} batches of {batchSize:N0})");
        Console.WriteLine($"Timeout: {timeoutSeconds}s per storage type");
        Console.WriteLine();

        // Run test for each storage type
        foreach (var storageKind in storageTypes)
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
            for (int batchNum = 0; batchNum < totalBatches; batchNum++)
            {
                // Check if we're approaching timeout (with 2 second buffer)
                if (overallSw.Elapsed.TotalSeconds > timeoutSeconds - 2)
                {
                    timedOut = true;
                    Console.WriteLine($"  ?? Approaching timeout at batch {batchNum + 1}/{totalBatches}");
                    Console.WriteLine($"  ?? Completed {batchNum * batchSize:N0} records in {overallSw.Elapsed.TotalSeconds:F2}s");
                    break;
                }

                // Measure batch creation time
                var batchCreationSw = Stopwatch.StartNew();
                var batch = Enumerable.Range(0, batchSize)
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

                // Record data point every 10 batches for graphing
                if (batchNum % 10 == 0 || batchNum == totalBatches - 1)
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
                        TotalRecords = (batchNum + 1) * batchSize,
                        WriteTimeMs = writeTime,
                        MemoryMB = stats.sizeInMemory / 1024.0 / 1024.0,
                        DiskMB = stats.sizeOnDisk / 1024.0 / 1024.0
                    });
                }

                // Progress every 100 batches
                if ((batchNum + 1) % 100 == 0)
                {
                    var elapsed = overallSw.Elapsed.TotalSeconds;
                    var recordsWritten = (batchNum + 1) * batchSize;
                    var remainingTime = timeoutSeconds - elapsed;
                    Console.WriteLine($"  [{batchNum + 1}/{totalBatches}] {recordsWritten:N0} records, {elapsed:F1}s elapsed, {remainingTime:F1}s remaining");
                }
            }

            overallSw.Stop();

            // Calculate statistics
            var finalStats = storage.GetStatistics();
            var baseline = writeTimings.Take(Math.Min(10, writeTimings.Count)).Average(); // First 10 batches as baseline
            var actualRecords = batchesCompleted * batchSize;
            
            // Calculate time breakdown
            var totalWriteTimeMs = writeTimings.Sum();
            var totalElapsedMs = overallSw.Elapsed.TotalMilliseconds;
            var otherOverheadMs = totalElapsedMs - totalWriteTimeMs - totalGetStatisticsTimeMs - totalBatchCreationTimeMs;
            
            results[storageKind] = new WriteTestResult
            {
                StorageKind = storageKind,
                TotalRecords = actualRecords,
                TotalBatches = batchesCompleted,
                TotalTimeSeconds = overallSw.Elapsed.TotalSeconds,
                AverageWriteMs = writeTimings.Average(),
                MedianWriteMs = GetMedian(writeTimings),
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

            if (timedOut)
            {
                Console.WriteLine($"?? {storageKind} timed out at {overallSw.Elapsed.TotalSeconds:F2}s ({actualRecords:N0} records)");
            }
            else
            {
                Console.WriteLine($"? {storageKind} completed in {overallSw.Elapsed.TotalSeconds:F2}s");
            }
            Console.WriteLine($"  Avg: {writeTimings.Average():F2}ms, Median: {GetMedian(writeTimings):F2}ms, Baseline: {baseline:F2}ms");
            Console.WriteLine();
            Console.WriteLine($"  Time Breakdown:");
            Console.WriteLine($"    Pure writes:      {totalWriteTimeMs / 1000.0,8:F2}s ({totalWriteTimeMs / totalElapsedMs * 100,5:F1}%)");
            Console.WriteLine($"    GetStatistics:    {totalGetStatisticsTimeMs / 1000.0,8:F2}s ({totalGetStatisticsTimeMs / totalElapsedMs * 100,5:F1}%) - {getStatisticsCallCount} calls");
            Console.WriteLine($"    Batch creation:   {totalBatchCreationTimeMs / 1000.0,8:F2}s ({totalBatchCreationTimeMs / totalElapsedMs * 100,5:F1}%)");
            Console.WriteLine($"    Other overhead:   {otherOverheadMs / 1000.0,8:F2}s ({otherOverheadMs / totalElapsedMs * 100,5:F1}%)");
            Console.WriteLine();

            factory.cleanup();
        }

        // Generate comparison report
        Console.WriteLine();
        Console.WriteLine("???????????????????????????????????????????????????????");
        Console.WriteLine("              COMPARATIVE RESULTS");
        Console.WriteLine("???????????????????????????????????????????????????????");
        Console.WriteLine();

        Console.WriteLine($"{"Storage",-12} {"Status",-12} {"Total Time",-12} {"Records",-12} {"Avg Write",-12} {"Median",-12}");
        Console.WriteLine(new string('?', 80));
        foreach (var result in results.Values.OrderBy(r => r.TotalTimeSeconds))
        {
            var status = result.TimedOut ? "TIMEOUT" : "PASS";
            Console.WriteLine($"{result.StorageKind,-12} {status,-12} {result.TotalTimeSeconds,8:F2}s     {result.TotalRecords,10:N0}  {result.AverageWriteMs,8:F2}ms   {result.MedianWriteMs,8:F2}ms");
        }
        Console.WriteLine();

        // Generate HTML graph
        var htmlPath = GenerateComparisonHtml(results, totalRecords);
        Console.WriteLine($"?? Interactive graph generated: {htmlPath}");
        Console.WriteLine($"   Open in browser to view performance comparison");
        Console.WriteLine();

        // Validate all completed within timeout
        foreach (var result in results.Values)
        {
            if (result.TimedOut)
            {
                Assert.Fail($"{result.StorageKind} exceeded timeout: {result.TotalTimeSeconds:F2}s > {timeoutSeconds}s (completed {result.TotalRecords:N0}/{totalRecords:N0} records)");
            }
            else
            {
                Assert.IsTrue(result.TotalTimeSeconds <= timeoutSeconds,
                    $"{result.StorageKind} exceeded timeout: {result.TotalTimeSeconds:F2}s > {timeoutSeconds}s");
                Console.WriteLine($"? {result.StorageKind} passed: {result.TotalTimeSeconds:F2}s ? {timeoutSeconds}s");
            }
        }

        Console.WriteLine();
        Console.WriteLine("? ALL TESTS PASSED");
    }

    /// <summary>
    /// Generate HTML with Plotly.js graphs comparing all storage types.
    /// </summary>
    private string GenerateComparisonHtml(Dictionary<string, WriteTestResult> results, int targetRecords)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var filename = $"WritePerformance_Comparison_{timestamp}.html";
        var filePath = Path.Combine(Path.GetTempPath(), filename);

        var sqlite = results["Sqlite"];
        var hybrid = results["Hybrid"];
        var inMemory = results["InMemory"];

        // Prepare data for Plotly
        var sqliteRecords = string.Join(",", sqlite.PerformanceData.Select(d => d.TotalRecords));
        var hybridRecords = string.Join(",", hybrid.PerformanceData.Select(d => d.TotalRecords));
        var inMemoryRecords = string.Join(",", inMemory.PerformanceData.Select(d => d.TotalRecords));

        var sqliteTimes = string.Join(",", sqlite.PerformanceData.Select(d => d.WriteTimeMs.ToString("F2")));
        var hybridTimes = string.Join(",", hybrid.PerformanceData.Select(d => d.WriteTimeMs.ToString("F2")));
        var inMemoryTimes = string.Join(",", inMemory.PerformanceData.Select(d => d.WriteTimeMs.ToString("F2")));

        var sqliteStatus = sqlite.TimedOut ? "?? TIMEOUT" : $"{sqlite.TotalTimeSeconds:F1}s";
        var hybridStatus = hybrid.TimedOut ? "?? TIMEOUT" : $"{hybrid.TotalTimeSeconds:F1}s";
        var inMemoryStatus = inMemory.TimedOut ? "?? TIMEOUT" : $"{inMemory.TotalTimeSeconds:F1}s";

        var html = $@"<!DOCTYPE html>
<html>
<head>
    <title>Write Performance Comparison - {targetRecords:N0} Records Target</title>
    <script src='https://cdn.plot.ly/plotly-2.27.0.min.js'></script>
    <style>
        body {{ font-family: 'Segoe UI', Arial, sans-serif; margin: 20px; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); min-height: 100vh; }}
        .container {{ max-width: 1400px; margin: 0 auto; }}
        h1 {{ color: white; text-align: center; font-size: 2.5em; margin-bottom: 10px; text-shadow: 2px 2px 4px rgba(0,0,0,0.3); }}
        .subtitle {{ color: rgba(255,255,255,0.9); text-align: center; font-size: 1.2em; margin-bottom: 30px; }}
        .card {{ background: white; padding: 25px; margin: 20px 0; border-radius: 12px; box-shadow: 0 8px 32px rgba(0,0,0,0.2); }}
        .stats-grid {{ display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 20px; margin: 20px 0; }}
        .stat-box {{ background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 20px; border-radius: 8px; text-align: center; }}
        .stat-box.timeout {{ background: linear-gradient(135deg, #f093fb 0%, #f5576c 100%); }}
        .stat-box h3 {{ margin: 0 0 10px 0; font-size: 1.1em; opacity: 0.9; }}
        .stat-box .value {{ font-size: 2.5em; font-weight: bold; margin: 10px 0; }}
        .stat-box .label {{ font-size: 0.9em; opacity: 0.8; }}
        table {{ width: 100%; border-collapse: collapse; margin: 20px 0; }}
        th, td {{ padding: 12px; text-align: left; border-bottom: 1px solid #ddd; }}
        th {{ background: #f8f9fa; font-weight: 600; color: #495057; }}
        .winner {{ background: #d4edda; font-weight: bold; }}
        .timeout {{ background: #fff3cd; }}
        .chart-container {{ margin: 30px 0; }}
        .footer {{ text-align: center; color: rgba(255,255,255,0.8); margin-top: 30px; font-size: 0.9em; }}
    </style>
</head>
<body>
    <div class='container'>
        <h1>?? Write Performance Comparison</h1>
        <div class='subtitle'>Target: {targetRecords:N0} Records • SQLite vs Hybrid vs InMemory</div>
        
        <div class='stats-grid'>
            <div class='stat-box{(sqlite.TimedOut ? " timeout" : "")}'>
                <h3>SQLite</h3>
                <div class='value'>{sqliteStatus}</div>
                <div class='label'>{sqlite.TotalRecords:N0} records • {sqlite.AverageWriteMs:F2}ms avg</div>
            </div>
            <div class='stat-box{(hybrid.TimedOut ? " timeout" : "")}'>
                <h3>Hybrid</h3>
                <div class='value'>{hybridStatus}</div>
                <div class='label'>{hybrid.TotalRecords:N0} records • {hybrid.AverageWriteMs:F2}ms avg</div>
            </div>
            <div class='stat-box{(inMemory.TimedOut ? " timeout" : "")}'>
                <h3>InMemory</h3>
                <div class='value'>{inMemoryStatus}</div>
                <div class='label'>{inMemory.TotalRecords:N0} records • {inMemory.AverageWriteMs:F2}ms avg</div>
            </div>
        </div>

        <div class='card'>
            <h2>Detailed Results</h2>
            <table>
                <tr>
                    <th>Storage Type</th>
                    <th>Status</th>
                    <th>Total Time</th>
                    <th>Records</th>
                    <th>Average Write</th>
                    <th>Median Write</th>
                    <th>Baseline</th>
                    <th>Min</th>
                    <th>Max</th>
                </tr>
                <tr class='{(sqlite.TimedOut ? "timeout" : (sqlite.TotalTimeSeconds < hybrid.TotalTimeSeconds && sqlite.TotalTimeSeconds < inMemory.TotalTimeSeconds ? "winner" : ""))}'>
                    <td><strong>SQLite</strong></td>
                    <td>{(sqlite.TimedOut ? "?? TIMEOUT" : "? PASS")}</td>
                    <td>{sqlite.TotalTimeSeconds:F2}s</td>
                    <td>{sqlite.TotalRecords:N0}</td>
                    <td>{sqlite.AverageWriteMs:F2}ms</td>
                    <td>{sqlite.MedianWriteMs:F2}ms</td>
                    <td>{sqlite.BaselineWriteMs:F2}ms</td>
                    <td>{sqlite.MinWriteMs:F2}ms</td>
                    <td>{sqlite.MaxWriteMs:F2}ms</td>
                </tr>
                <tr class='{(hybrid.TimedOut ? "timeout" : (hybrid.TotalTimeSeconds < sqlite.TotalTimeSeconds && hybrid.TotalTimeSeconds < inMemory.TotalTimeSeconds ? "winner" : ""))}'>
                    <td><strong>Hybrid</strong></td>
                    <td>{(hybrid.TimedOut ? "?? TIMEOUT" : "? PASS")}</td>
                    <td>{hybrid.TotalTimeSeconds:F2}s</td>
                    <td>{hybrid.TotalRecords:N0}</td>
                    <td>{hybrid.AverageWriteMs:F2}ms</td>
                    <td>{hybrid.MedianWriteMs:F2}ms</td>
                    <td>{hybrid.BaselineWriteMs:F2}ms</td>
                    <td>{hybrid.MinWriteMs:F2}ms</td>
                    <td>{hybrid.MaxWriteMs:F2}ms</td>
                </tr>
                <tr class='{(inMemory.TimedOut ? "timeout" : (inMemory.TotalTimeSeconds < sqlite.TotalTimeSeconds && inMemory.TotalTimeSeconds < hybrid.TotalTimeSeconds ? "winner" : ""))}'>
                    <td><strong>InMemory</strong></td>
                    <td>{(inMemory.TimedOut ? "?? TIMEOUT" : "? PASS")}</td>
                    <td>{inMemory.TotalTimeSeconds:F2}s</td>
                    <td>{inMemory.TotalRecords:N0}</td>
                    <td>{inMemory.AverageWriteMs:F2}ms</td>
                    <td>{inMemory.MedianWriteMs:F2}ms</td>
                    <td>{inMemory.BaselineWriteMs:F2}ms</td>
                    <td>{inMemory.MinWriteMs:F2}ms</td>
                    <td>{inMemory.MaxWriteMs:F2}ms</td>
                </tr>
            </table>
        </div>

        <div class='card chart-container'>
            <h2>Write Time Over Records</h2>
            <div id='writeTimeChart'></div>
        </div>

        <div class='card chart-container'>
            <h2>Performance Degradation (Ratio to Baseline)</h2>
            <div id='degradationChart'></div>
        </div>

        <div class='card chart-container'>
            <h2>Total Time Comparison</h2>
            <div id='barChart'></div>
        </div>

        <div class='footer'>
            Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss} • Target: {targetRecords:N0} records
        </div>
    </div>

    <script>
        // Write Time Over Records
        var sqliteTrace = {{
            x: [{sqliteRecords}],
            y: [{sqliteTimes}],
            name: 'SQLite{(sqlite.TimedOut ? " (timeout)" : "")}',
            type: 'scatter',
            mode: 'lines',
            line: {{ color: '#FF6B6B', width: 3 }}
        }};

        var hybridTrace = {{
            x: [{hybridRecords}],
            y: [{hybridTimes}],
            name: 'Hybrid{(hybrid.TimedOut ? " (timeout)" : "")}',
            type: 'scatter',
            mode: 'lines',
            line: {{ color: '#4ECDC4', width: 3 }}
        }};

        var inMemoryTrace = {{
            x: [{inMemoryRecords}],
            y: [{inMemoryTimes}],
            name: 'InMemory{(inMemory.TimedOut ? " (timeout)" : "")}',
            type: 'scatter',
            mode: 'lines',
            line: {{ color: '#95E1D3', width: 3 }}
        }};

        var writeTimeLayout = {{
            xaxis: {{ title: 'Records Written', gridcolor: '#e0e0e0' }},
            yaxis: {{ title: 'Write Time (ms)', gridcolor: '#e0e0e0' }},
            hovermode: 'x unified',
            plot_bgcolor: '#fafafa',
            paper_bgcolor: 'white'
        }};

        Plotly.newPlot('writeTimeChart', [sqliteTrace, hybridTrace, inMemoryTrace], writeTimeLayout, {{responsive: true}});

        // Performance Degradation (ratio to baseline)
        var sqliteBaseline = {sqlite.BaselineWriteMs:F2};
        var hybridBaseline = {hybrid.BaselineWriteMs:F2};
        var inMemoryBaseline = {inMemory.BaselineWriteMs:F2};

        var sqliteDegradation = {{
            x: [{sqliteRecords}],
            y: [{sqliteTimes}].map(t => t / sqliteBaseline),
            name: 'SQLite',
            type: 'scatter',
            mode: 'lines',
            line: {{ color: '#FF6B6B', width: 3 }}
        }};

        var hybridDegradation = {{
            x: [{hybridRecords}],
            y: [{hybridTimes}].map(t => t / hybridBaseline),
            name: 'Hybrid',
            type: 'scatter',
            mode: 'lines',
            line: {{ color: '#4ECDC4', width: 3 }}
        }};

        var inMemoryDegradation = {{
            x: [{inMemoryRecords}],
            y: [{inMemoryTimes}].map(t => t / inMemoryBaseline),
            name: 'InMemory',
            type: 'scatter',
            mode: 'lines',
            line: {{ color: '#95E1D3', width: 3 }}
        }};

        var degradationLayout = {{
            xaxis: {{ title: 'Records Written', gridcolor: '#e0e0e0' }},
            yaxis: {{ title: 'Performance Ratio (×baseline)', gridcolor: '#e0e0e0' }},
            hovermode: 'x unified',
            plot_bgcolor: '#fafafa',
            paper_bgcolor: 'white',
            shapes: [{{
                type: 'line',
                x0: 0,
                x1: {targetRecords},
                y0: 1,
                y1: 1,
                line: {{ color: '#999', width: 2, dash: 'dash' }}
            }}]
        }};

        Plotly.newPlot('degradationChart', [sqliteDegradation, hybridDegradation, inMemoryDegradation], degradationLayout, {{responsive: true}});

        // Bar Chart Comparison
        var barTrace = {{
            x: ['SQLite', 'Hybrid', 'InMemory'],
            y: [{sqlite.TotalTimeSeconds:F2}, {hybrid.TotalTimeSeconds:F2}, {inMemory.TotalTimeSeconds:F2}],
            type: 'bar',
            marker: {{
                color: [{(sqlite.TimedOut ? "'#f5576c'" : "'#FF6B6B'")}, {(hybrid.TimedOut ? "'#f5576c'" : "'#4ECDC4'")}, {(inMemory.TimedOut ? "'#f5576c'" : "'#95E1D3'")}],
                line: {{ color: 'white', width: 2 }}
            }},
            text: ['{sqlite.TotalTimeSeconds:F2}s{(sqlite.TimedOut ? " (timeout)" : "")}', '{hybrid.TotalTimeSeconds:F2}s{(hybrid.TimedOut ? " (timeout)" : "")}', '{inMemory.TotalTimeSeconds:F2}s{(inMemory.TimedOut ? " (timeout)" : "")}'],
            textposition: 'outside'
        }};

        var barLayout = {{
            yaxis: {{ title: 'Total Time (seconds)', gridcolor: '#e0e0e0' }},
            plot_bgcolor: '#fafafa',
            paper_bgcolor: 'white',
            showlegend: false
        }};

        Plotly.newPlot('barChart', [barTrace], barLayout, {{responsive: true}});
    </script>
</body>
</html>";

        File.WriteAllText(filePath, html);
        return filePath;
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

    // Data structures
    private class WriteTestResult
    {
        public string StorageKind { get; set; }
        public int TotalRecords { get; set; }
        public int TotalBatches { get; set; }
        public double TotalTimeSeconds { get; set; }
        public double AverageWriteMs { get; set; }
        public double MedianWriteMs { get; set; }
        public double MinWriteMs { get; set; }
        public double MaxWriteMs { get; set; }
        public double BaselineWriteMs { get; set; }
        public double FinalMemoryMB { get; set; }
        public double FinalDiskMB { get; set; }
        public List<PerformanceDataPoint> PerformanceData { get; set; }
        public bool TimedOut { get; set; }
        public double TotalWriteTimeMs { get; set; }
        public double TotalGetStatisticsTimeMs { get; set; }
        public int GetStatisticsCallCount { get; set; }
        public double TotalBatchCreationTimeMs { get; set; }
        public double OtherOverheadMs { get; set; }
    }

    private class PerformanceDataPoint
    {
        public int BatchNumber { get; set; }
        public int TotalRecords { get; set; }
        public double WriteTimeMs { get; set; }
        public double MemoryMB { get; set; }
        public double DiskMB { get; set; }
    }
}
