namespace PerformanceTests.Configuration;

/// <summary>
/// Configuration constants for performance tests.
/// Centralized location for all test parameters to make tuning easier.
/// </summary>
public static class PerformanceTestConfig
{
    // Test scale
    public const int TotalRecords = 2_000_000;
    public const int BatchSize = 5000;
    public const int TotalBatches = TotalRecords / BatchSize; // 400 batches
    
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
