using System;
using System.Collections.Generic;
using FindNeedlePluginLib;
using FindNeedlePluginLib.Interfaces;

namespace CoreTests.Models;

/// <summary>
/// Test result data for a single search result used in performance tests.
/// </summary>
public class DummySearchResult : ISearchResult
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

/// <summary>
/// Results from a write performance test for a single storage type.
/// </summary>
public class WriteTestResult
{
    public string StorageKind { get; set; } = string.Empty;
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
    public List<PerformanceDataPoint> PerformanceData { get; set; } = new();
    public bool TimedOut { get; set; }
    public double TotalWriteTimeMs { get; set; }
    public double TotalGetStatisticsTimeMs { get; set; }
    public int GetStatisticsCallCount { get; set; }
    public double TotalBatchCreationTimeMs { get; set; }
    public double OtherOverheadMs { get; set; }
}

/// <summary>
/// A single data point captured during performance testing for graphing.
/// </summary>
public class PerformanceDataPoint
{
    public int BatchNumber { get; set; }
    public int TotalRecords { get; set; }
    public double WriteTimeMs { get; set; }
    public double MemoryMB { get; set; }
    public double DiskMB { get; set; }
}
