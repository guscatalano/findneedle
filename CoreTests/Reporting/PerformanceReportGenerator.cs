using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoreTests.Models;

namespace CoreTests.Reporting;

/// <summary>
/// Generates HTML performance comparison reports with interactive Plotly.js charts.
/// </summary>
public static class PerformanceReportGenerator
{
    /// <summary>
    /// Generates an HTML file with interactive charts comparing storage performance.
    /// </summary>
    /// <param name="results">Dictionary of test results by storage type</param>
    /// <param name="targetRecords">Target number of records for the test</param>
    /// <returns>Path to the generated HTML file</returns>
    public static string GenerateHtmlReport(Dictionary<string, WriteTestResult> results, int targetRecords)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var filename = $"WritePerformance_Comparison_{timestamp}.html";
        var filePath = Path.Combine(Path.GetTempPath(), filename);

        var sqlite = results["Sqlite"];
        var hybrid = results["Hybrid"];
        var hybridCapped = results["HybridCapped"];
        var inMemory = results["InMemory"];

        // Prepare data for Plotly charts
        var dataArrays = PrepareChartData(sqlite, hybrid, hybridCapped, inMemory);
        
        // Generate HTML with embedded charts
        var html = GenerateHtmlContent(targetRecords, sqlite, hybrid, hybridCapped, inMemory, dataArrays);
        
        File.WriteAllText(filePath, html);
        return filePath;
    }

    private static ChartDataArrays PrepareChartData(params WriteTestResult[] results)
    {
        return new ChartDataArrays
        {
            SqliteRecords = FormatDataArray(results[0].PerformanceData.Select(d => d.TotalRecords)),
            HybridRecords = FormatDataArray(results[1].PerformanceData.Select(d => d.TotalRecords)),
            HybridCappedRecords = FormatDataArray(results[2].PerformanceData.Select(d => d.TotalRecords)),
            InMemoryRecords = FormatDataArray(results[3].PerformanceData.Select(d => d.TotalRecords)),
            
            SqliteTimes = FormatDataArray(results[0].PerformanceData.Select(d => d.WriteTimeMs)),
            HybridTimes = FormatDataArray(results[1].PerformanceData.Select(d => d.WriteTimeMs)),
            HybridCappedTimes = FormatDataArray(results[2].PerformanceData.Select(d => d.WriteTimeMs)),
            InMemoryTimes = FormatDataArray(results[3].PerformanceData.Select(d => d.WriteTimeMs))
        };
    }

    private static string FormatDataArray<T>(IEnumerable<T> values)
    {
        return string.Join(",", values.Select(v => 
            v is double d ? d.ToString("F2") : v?.ToString() ?? "0"));
    }

    private static string GenerateHtmlContent(
        int targetRecords,
        WriteTestResult sqlite,
        WriteTestResult hybrid,
        WriteTestResult hybridCapped,
        WriteTestResult inMemory,
        ChartDataArrays data)
    {
        var sqliteStatus = sqlite.TimedOut ? "? TIMEOUT" : $"{sqlite.TotalTimeSeconds:F1}s";
        var hybridStatus = hybrid.TimedOut ? "? TIMEOUT" : $"{hybrid.TotalTimeSeconds:F1}s";
        var hybridCappedStatus = hybridCapped.TimedOut ? "? TIMEOUT" : $"{hybridCapped.TotalTimeSeconds:F1}s";
        var inMemoryStatus = inMemory.TimedOut ? "? TIMEOUT" : $"{inMemory.TotalTimeSeconds:F1}s";

        return $@"<!DOCTYPE html>
<html>
<head>
    <title>Write Performance Comparison - {targetRecords:N0} Records Target</title>
    <script src='https://cdn.plot.ly/plotly-2.27.0.min.js'></script>
    {GenerateStyles()}
</head>
<body>
    <div class='container'>
        <h1>?? Write Performance Comparison</h1>
        <div class='subtitle'>Target: {targetRecords:N0} Records · SQLite vs Hybrid vs Hybrid (1M cap) vs InMemory</div>
        
        {GenerateStatsGrid(sqlite, hybrid, hybridCapped, inMemory, sqliteStatus, hybridStatus, hybridCappedStatus, inMemoryStatus)}
        {GenerateDetailedResultsTable(sqlite, hybrid, hybridCapped, inMemory)}
        {GenerateTimeBreakdownTable(sqlite, hybrid, hybridCapped, inMemory)}
        
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
            Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss} · Target: {targetRecords:N0} records
        </div>
    </div>

    {GenerateJavaScript(data, sqlite, hybrid, hybridCapped, inMemory, targetRecords)}
</body>
</html>";
    }

    private static string GenerateStyles()
    {
        return @"<style>
        body { font-family: 'Segoe UI', Arial, sans-serif; margin: 20px; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); min-height: 100vh; }
        .container { max-width: 1400px; margin: 0 auto; }
        h1 { color: white; text-align: center; font-size: 2.5em; margin-bottom: 10px; text-shadow: 2px 2px 4px rgba(0,0,0,0.3); }
        .subtitle { color: rgba(255,255,255,0.9); text-align: center; font-size: 1.2em; margin-bottom: 30px; }
        .card { background: white; padding: 25px; margin: 20px 0; border-radius: 12px; box-shadow: 0 8px 32px rgba(0,0,0,0.2); }
        .stats-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(250px, 1fr)); gap: 20px; margin: 20px 0; }
        .stat-box { background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 20px; border-radius: 8px; text-align: center; }
        .stat-box.timeout { background: linear-gradient(135deg, #f093fb 0%, #f5576c 100%); }
        .stat-box h3 { margin: 0 0 10px 0; font-size: 1.1em; opacity: 0.9; }
        .stat-box .value { font-size: 2.5em; font-weight: bold; margin: 10px 0; }
        .stat-box .label { font-size: 0.9em; opacity: 0.8; }
        table { width: 100%; border-collapse: collapse; margin: 20px 0; }
        th, td { padding: 12px; text-align: left; border-bottom: 1px solid #ddd; }
        th { background: #f8f9fa; font-weight: 600; color: #495057; }
        .winner { background: #d4edda; font-weight: bold; }
        .timeout { background: #fff3cd; }
        .chart-container { margin: 30px 0; }
        .footer { text-align: center; color: rgba(255,255,255,0.8); margin-top: 30px; font-size: 0.9em; }
    </style>";
    }

    private static string GenerateStatsGrid(
        WriteTestResult sqlite, WriteTestResult hybrid, 
        WriteTestResult hybridCapped, WriteTestResult inMemory,
        string sqliteStatus, string hybridStatus, 
        string hybridCappedStatus, string inMemoryStatus)
    {
        return $@"<div class='stats-grid'>
            <div class='stat-box{(sqlite.TimedOut ? " timeout" : "")}'>
                <h3>SQLite</h3>
                <div class='value'>{sqliteStatus}</div>
                <div class='label'>{sqlite.TotalRecords:N0} records · {sqlite.AverageWriteMs:F2}ms avg</div>
            </div>
            <div class='stat-box{(hybrid.TimedOut ? " timeout" : "")}'>
                <h3>Hybrid</h3>
                <div class='value'>{hybridStatus}</div>
                <div class='label'>{hybrid.TotalRecords:N0} records · {hybrid.AverageWriteMs:F2}ms avg<br/><small>No cap</small></div>
            </div>
            <div class='stat-box{(hybridCapped.TimedOut ? " timeout" : "")}'>
                <h3>Hybrid (Capped)</h3>
                <div class='value'>{hybridCappedStatus}</div>
                <div class='label'>{hybridCapped.TotalRecords:N0} records · {hybridCapped.AverageWriteMs:F2}ms avg<br/><small>1M record cap</small></div>
            </div>
            <div class='stat-box{(inMemory.TimedOut ? " timeout" : "")}'>
                <h3>InMemory</h3>
                <div class='value'>{inMemoryStatus}</div>
                <div class='label'>{inMemory.TotalRecords:N0} records · {inMemory.AverageWriteMs:F2}ms avg</div>
            </div>
        </div>";
    }

    private static string GenerateDetailedResultsTable(
        WriteTestResult sqlite, WriteTestResult hybrid,
        WriteTestResult hybridCapped, WriteTestResult inMemory)
    {
        bool IsFastest(WriteTestResult r) => !r.TimedOut && 
            r.TotalTimeSeconds < sqlite.TotalTimeSeconds &&
            r.TotalTimeSeconds < hybrid.TotalTimeSeconds &&
            r.TotalTimeSeconds < hybridCapped.TotalTimeSeconds &&
            r.TotalTimeSeconds < inMemory.TotalTimeSeconds;

        return $@"<div class='card'>
            <h2>Detailed Results</h2>
            <table>
                <tr>
                    <th>Storage Type</th><th>Status</th><th>Total Time</th><th>Records</th>
                    <th>Average Write</th><th>Median Write</th><th>Baseline</th><th>Min</th><th>Max</th>
                </tr>
                {GenerateResultRow("SQLite", sqlite, IsFastest(sqlite))}
                {GenerateResultRow("Hybrid", hybrid, IsFastest(hybrid))}
                {GenerateResultRow("Hybrid (Capped)", hybridCapped, IsFastest(hybridCapped))}
                {GenerateResultRow("InMemory", inMemory, IsFastest(inMemory))}
            </table>
        </div>";
    }

    private static string GenerateResultRow(string name, WriteTestResult result, bool isWinner)
    {
        var cssClass = result.TimedOut ? "timeout" : (isWinner ? "winner" : "");
        var status = result.TimedOut ? "? TIMEOUT" : "? PASS";
        
        return $@"<tr class='{cssClass}'>
                    <td><strong>{name}</strong></td>
                    <td>{status}</td>
                    <td>{result.TotalTimeSeconds:F2}s</td>
                    <td>{result.TotalRecords:N0}</td>
                    <td>{result.AverageWriteMs:F2}ms</td>
                    <td>{result.MedianWriteMs:F2}ms</td>
                    <td>{result.BaselineWriteMs:F2}ms</td>
                    <td>{result.MinWriteMs:F2}ms</td>
                    <td>{result.MaxWriteMs:F2}ms</td>
                </tr>";
    }

    private static string GenerateTimeBreakdownTable(
        WriteTestResult sqlite, WriteTestResult hybrid,
        WriteTestResult hybridCapped, WriteTestResult inMemory)
    {
        return $@"<div class='card'>
            <h2>? Time Breakdown - Where Did The Time Go?</h2>
            <table>
                <tr>
                    <th>Storage Type</th><th>Pure Writes</th><th>GetStatistics</th>
                    <th>Batch Creation</th><th>Other Overhead</th>
                </tr>
                {GenerateBreakdownRow("SQLite", sqlite)}
                {GenerateBreakdownRow("Hybrid", hybrid)}
                {GenerateBreakdownRow("Hybrid (Capped)", hybridCapped)}
                {GenerateBreakdownRow("InMemory", inMemory)}
            </table>
            <p style='margin-top: 15px; padding: 10px; background: #fff3cd; border-left: 4px solid #ffc107; border-radius: 4px;'>
                <strong>?? Key Insight:</strong> If GetStatistics takes more than 50% of total time, it's a bottleneck that needs optimization!
            </p>
        </div>";
    }

    private static string GenerateBreakdownRow(string name, WriteTestResult result)
    {
        double totalMs = result.TotalTimeSeconds * 1000;
        double writePct = result.TotalWriteTimeMs / totalMs * 100;
        double statsPct = result.TotalGetStatisticsTimeMs / totalMs * 100;
        double batchPct = result.TotalBatchCreationTimeMs / totalMs * 100;
        double otherPct = result.OtherOverheadMs / totalMs * 100;
        
        bool isBottleneck = statsPct > 50;
        var cssClass = isBottleneck ? "timeout" : "";
        var bottleneckWarning = isBottleneck ? "? BOTTLENECK!" : "";
        
        return $@"<tr class='{cssClass}'>
                    <td><strong>{name}</strong></td>
                    <td>{result.TotalWriteTimeMs / 1000.0:F2}s ({writePct:F1}%)</td>
                    <td style='font-weight: {(isBottleneck ? "bold" : "normal")}; color: {(isBottleneck ? "#f5576c" : "inherit")};'>
                        {result.TotalGetStatisticsTimeMs / 1000.0:F2}s ({statsPct:F1}%) {bottleneckWarning}
                        <br/><small>{result.GetStatisticsCallCount} calls, {result.TotalGetStatisticsTimeMs / result.GetStatisticsCallCount:F2}ms avg</small>
                    </td>
                    <td>{result.TotalBatchCreationTimeMs / 1000.0:F2}s ({batchPct:F1}%)</td>
                    <td>{result.OtherOverheadMs / 1000.0:F2}s ({otherPct:F1}%)</td>
                </tr>";
    }

    private static string GenerateJavaScript(
        ChartDataArrays data,
        WriteTestResult sqlite, WriteTestResult hybrid,
        WriteTestResult hybridCapped, WriteTestResult inMemory,
        int targetRecords)
    {
        return $@"<script>
        // Chart 1: Write Time Over Records
        var writeTimeLayout = {{
            xaxis: {{ title: 'Records Written', gridcolor: '#e0e0e0' }},
            yaxis: {{ title: 'Write Time (ms)', gridcolor: '#e0e0e0' }},
            hovermode: 'x unified',
            plot_bgcolor: '#fafafa',
            paper_bgcolor: 'white'
        }};
        Plotly.newPlot('writeTimeChart', [
            {{x: [{data.SqliteRecords}], y: [{data.SqliteTimes}], name: 'SQLite{(sqlite.TimedOut ? " (timeout)" : "")}', type: 'scatter', mode: 'lines', line: {{color: '#FF6B6B', width: 3}}}},
            {{x: [{data.HybridRecords}], y: [{data.HybridTimes}], name: 'Hybrid{(hybrid.TimedOut ? " (timeout)" : "")}', type: 'scatter', mode: 'lines', line: {{color: '#4ECDC4', width: 3}}}},
            {{x: [{data.HybridCappedRecords}], y: [{data.HybridCappedTimes}], name: 'Hybrid (Capped){(hybridCapped.TimedOut ? " (timeout)" : "")}', type: 'scatter', mode: 'lines', line: {{color: '#FFD93D', width: 3}}}},
            {{x: [{data.InMemoryRecords}], y: [{data.InMemoryTimes}], name: 'InMemory{(inMemory.TimedOut ? " (timeout)" : "")}', type: 'scatter', mode: 'lines', line: {{color: '#95E1D3', width: 3}}}}
        ], writeTimeLayout, {{responsive: true}});

        // Chart 2: Performance Degradation
        var degradationLayout = {{
            xaxis: {{ title: 'Records Written', gridcolor: '#e0e0e0' }},
            yaxis: {{ title: 'Performance Ratio (×baseline)', gridcolor: '#e0e0e0' }},
            hovermode: 'x unified',
            plot_bgcolor: '#fafafa',
            paper_bgcolor: 'white',
            shapes: [{{type: 'line', x0: 0, x1: {targetRecords}, y0: 1, y1: 1, line: {{color: '#999', width: 2, dash: 'dash'}}}}]
        }};
        Plotly.newPlot('degradationChart', [
            {{x: [{data.SqliteRecords}], y: [{data.SqliteTimes}].map(t => t / {sqlite.BaselineWriteMs:F2}), name: 'SQLite', type: 'scatter', mode: 'lines', line: {{color: '#FF6B6B', width: 3}}}},
            {{x: [{data.HybridRecords}], y: [{data.HybridTimes}].map(t => t / {hybrid.BaselineWriteMs:F2}), name: 'Hybrid', type: 'scatter', mode: 'lines', line: {{color: '#4ECDC4', width: 3}}}},
            {{x: [{data.HybridCappedRecords}], y: [{data.HybridCappedTimes}].map(t => t / {hybridCapped.BaselineWriteMs:F2}), name: 'Hybrid (Capped)', type: 'scatter', mode: 'lines', line: {{color: '#FFD93D', width: 3}}}},
            {{x: [{data.InMemoryRecords}], y: [{data.InMemoryTimes}].map(t => t / {inMemory.BaselineWriteMs:F2}), name: 'InMemory', type: 'scatter', mode: 'lines', line: {{color: '#95E1D3', width: 3}}}}
        ], degradationLayout, {{responsive: true}});

        // Chart 3: Bar Chart Comparison
        var barLayout = {{
            yaxis: {{ title: 'Total Time (seconds)', gridcolor: '#e0e0e0' }},
            plot_bgcolor: '#fafafa',
            paper_bgcolor: 'white',
            showlegend: false
        }};
        Plotly.newPlot('barChart', [{{
            x: ['SQLite', 'Hybrid', 'Hybrid (Capped)', 'InMemory'],
            y: [{sqlite.TotalTimeSeconds:F2}, {hybrid.TotalTimeSeconds:F2}, {hybridCapped.TotalTimeSeconds:F2}, {inMemory.TotalTimeSeconds:F2}],
            type: 'bar',
            marker: {{
                color: [{(sqlite.TimedOut ? "'#f5576c'" : "'#FF6B6B'")}, {(hybrid.TimedOut ? "'#f5576c'" : "'#4ECDC4'")}, {(hybridCapped.TimedOut ? "'#f5576c'" : "'#FFD93D'")}, {(inMemory.TimedOut ? "'#f5576c'" : "'#95E1D3'")}],
                line: {{color: 'white', width: 2}}
            }},
            text: ['{sqlite.TotalTimeSeconds:F2}s{(sqlite.TimedOut ? " (timeout)" : "")}', '{hybrid.TotalTimeSeconds:F2}s{(hybrid.TimedOut ? " (timeout)" : "")}', '{hybridCapped.TotalTimeSeconds:F2}s{(hybridCapped.TimedOut ? " (timeout)" : "")}', '{inMemory.TotalTimeSeconds:F2}s{(inMemory.TimedOut ? " (timeout)" : "")}'],
            textposition: 'outside'
        }}], barLayout, {{responsive: true}});
    </script>";
    }

    private class ChartDataArrays
    {
        public string SqliteRecords { get; set; } = string.Empty;
        public string HybridRecords { get; set; } = string.Empty;
        public string HybridCappedRecords { get; set; } = string.Empty;
        public string InMemoryRecords { get; set; } = string.Empty;
        public string SqliteTimes { get; set; } = string.Empty;
        public string HybridTimes { get; set; } = string.Empty;
        public string HybridCappedTimes { get; set; } = string.Empty;
        public string InMemoryTimes { get; set; } = string.Empty;
    }
}
