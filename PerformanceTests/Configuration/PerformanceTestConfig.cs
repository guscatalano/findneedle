namespace PerformanceTests.Configuration;

/// <summary>
/// Configuration constants for performance tests.
/// Centralized location for all test parameters to make tuning easier.
/// </summary>
public static class PerformanceTestConfig
{
    // Test scale is per-storage. Direct SQLite gets a smaller set so it finishes within the
    // per-storage TimeoutSeconds budget even on a slow/AV-scanned disk (~12k rows/s there, so 2M
    // never finished its 80s budget; 500k lands ~40s). The in-memory-backed tiers still get a
    // genuinely large set to prove they scale — InMemory/Hybrid/HybridCapped all do 2M inside the
    // budget.
    public const int LargeRecordCount = 2_000_000;
    public const int SqliteRecordCount = 500_000;
    public const int BatchSize = 5000;

    /// <summary>Records written for a given storage kind — SQLite gets the smaller set.</summary>
    public static int RecordCountFor(string storageKind) =>
        storageKind == "Sqlite" ? SqliteRecordCount : LargeRecordCount;
    
    // Timing
    public const double TimeoutSeconds = 80.0;
    public const int TimeoutMilliseconds = (int)(TimeoutSeconds * 1000);
    public const int TotalTestTimeoutMilliseconds = 360000; // 6 minutes for all tests
    
    // Sampling intervals
    public const int StatisticsSampleInterval = 10; // Capture statistics every N batches
    public const int ProgressReportInterval = 100; // Report progress every N batches
    
    // Storage types to test
    public static readonly string[] StorageTypes = 
    { 
        "Sqlite", 
        "Hybrid", 
        "HybridCapped", 
        "InMemory" 
    };
    
    // HybridCapped configuration
    public const int HybridCappedMaxRecords = 1_000_000;
    public const int HybridMemoryThresholdMB = 100;
    
    // Reporting
    public const string HtmlReportPrefix = "WritePerformance_Comparison_";
    public const string DateTimeFormat = "yyyyMMdd_HHmmss";
}
