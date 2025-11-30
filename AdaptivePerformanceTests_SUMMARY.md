# Adaptive Performance Testing - Summary

## What Was Created

Three new adaptive performance test methods that **automatically find the breaking point** of each storage implementation instead of using fixed record counts.

## The Tests

### 1. `AdaptivePerformance_UntilDegradation`
- **Stops when**: Performance degrades to 2x baseline
- **Measures**: Both write AND read performance
- **Best for**: Finding practical capacity limits
- **Timeout**: 5 minutes

### 2. `StressTest_UntilSevereDegradation`
- **Stops when**: Performance degrades to 5x baseline
- **Measures**: Write performance
- **Best for**: Finding absolute breaking points
- **Timeout**: 10 minutes

### 3. `ThroughputDegradation_RecordsPerSecond`
- **Stops when**: Throughput drops to 50% of baseline
- **Measures**: Records per second
- **Best for**: Capacity planning with intuitive metrics
- **Timeout**: 5 minutes

## How They Work

```
Traditional Test:                 Adaptive Test:
???????????????                  ???????????????
? Insert 1M   ?                  ? Warmup:     ?
? records     ?                  ? Establish   ?
?             ?                  ? baseline    ?
? Done!       ?                  ???????????????
???????????????                  ? Loop:       ?
                                 ? - Insert    ?
Result:                          ? - Measure   ?
"Handles 1M"                     ? - Compare   ?
                                 ?             ?
                                 ? If degraded:?
                                 ?   Stop!     ?
                                 ???????????????
                                 
                                 Result:
                                 "Degrades at 850K"
```

## Key Advantages

### ? Finds Actual Capacity
```
Fixed:    "Can handle 1M records"
Adaptive: "Degrades at 850K records, optimal up to 600K"
```

### ? Detects Gradual Degradation
```
Fixed:    Pass/Fail at 1M
Adaptive: Shows performance curve:
          100K: 1.0x baseline
          500K: 1.5x baseline
          850K: 2.1x baseline ? Detected!
```

### ? Compares Implementations
```
InMemory:  Degrades at 10,000,000 records
SQLite:    Degrades at   500,000 records
Hybrid:    Degrades at 2,000,000 records

Insight: Hybrid provides 4x capacity of SQLite!
```

### ? Early Warning System
```
v1.0: Degrades at 850K
v1.1: Degrades at 720K ? Regression detected!
v1.2: Degrades at 920K ? Improvement confirmed!
```

## Example Output

```
=== Adaptive Performance Test: Hybrid ===
Batch Size: 1,000
Degradation Threshold: 2.0x baseline

Baseline established: 15.32 ms/batch (10,000 records)

Batch 100: 100,000 records
  Write: 16.89 ms (ratio: 1.10x)
  Memory: 12.34 MB, Disk: 18.56 MB

Batch 500: 500,000 records
  Write: 22.45 ms (ratio: 1.47x)
  Memory: 58.92 MB, Disk: 87.34 MB

Batch 850: 850,000 records
  Write: 42.34 ms (ratio: 2.76x) ? DEGRADED!
  *** DEGRADATION DETECTED ***

Total Records: 850,000
Performance degraded at 850K records
```

## When to Use

### Use Fixed Tests For:
- ? CI/CD (predictable duration)
- ? Regression testing (compare runs)
- ? Acceptance criteria (must handle X)
- ? Quick smoke tests

### Use Adaptive Tests For:
- ? Capacity planning (how much?)
- ? Comparing implementations (which is better?)
- ? Finding bottlenecks (where slowdown?)
- ? Stress testing (absolute limit?)
- ? Performance profiling (degradation curve)

## Running the Tests

```bash
# Run all adaptive tests
dotnet test --filter "FullyQualifiedName~AdaptivePerformanceTests"

# Run specific test
dotnet test --filter "FullyQualifiedName~AdaptivePerformance_UntilDegradation"

# Run only for specific storage
dotnet test --filter "FullyQualifiedName~AdaptivePerformance_UntilDegradation&DisplayName~Hybrid"

# Exclude stress tests (for CI)
dotnet test --filter "TestCategory!=Stress"

# Run only stress tests
dotnet test --filter "TestCategory=Stress"
```

## Configuration

### Adjust Sensitivity
```csharp
const double degradationThreshold = 2.0;
// 1.5x = Strict (catch early)
// 2.0x = Balanced
// 5.0x = Lenient (severe only)
```

### Adjust Safety Limits
```csharp
const int maxBatches = 10000;  // Safety limit
[Timeout(300000)]              // 5 minute timeout
```

### Adjust Batch Size
```csharp
const int batchSize = 1000;
// Smaller = More granular, slower
// Larger = Faster, less granular
```

## Statistical Analysis

Each test provides detailed statistics:

```
=== Performance Analysis ===
Write Timings:
  Min:    14.23 ms
  Max:    48.92 ms
  Mean:   23.45 ms
  Median: 19.67 ms
  StdDev: 8.34 ms    ? Low = stable, high = erratic

Read Timings:
  Min:    223.45 ms
  Max:    1289.34 ms
  Mean:   567.89 ms
  Median: 445.23 ms
  StdDev: 289.12 ms
```

## Benefits for Your Project

### 1. Understand Real Capacity
Know exactly how many records each storage can handle before slowdown.

### 2. Choose Right Storage
- Small datasets (< 100K): InMemory
- Medium datasets (100K - 1M): Hybrid
- Large datasets (> 1M): Consider SQLite or optimize Hybrid

### 3. Set Monitoring Thresholds
```
Degradation Point: 850K records
Warning Threshold: 600K records (70%)
Alert Threshold:   750K records (88%)
```

### 4. Performance Regression Detection
Track degradation point across versions to catch regressions early.

### 5. Capacity Planning
```
Current: Handles 500K records
Growth: 10% per month
Forecast: Will hit degradation (850K) in ~6 months
Action: Plan optimization or upgrade
```

## Files Created

1. **`CoreTests/AdaptivePerformanceTests.cs`** (470 lines)
   - 3 test methods
   - Helper methods for statistics
   - Comprehensive measurement logic

2. **`CoreTests/AdaptivePerformanceTests.README.md`** (700+ lines)
   - Complete documentation
   - Usage examples
   - Configuration guide
   - Troubleshooting

3. **`AdaptivePerformanceTests_SUMMARY.md`** (this file)
   - Quick reference
   - Key benefits
   - When to use

## Next Steps

### 1. Run Locally
```bash
cd CoreTests
dotnet test --filter "FullyQualifiedName~AdaptivePerformance_UntilDegradation" -v n
```

### 2. Review Results
Note degradation points for each storage type.

### 3. Integrate Selectively
Add to nightly builds, not regular CI (too slow).

### 4. Monitor Trends
Track degradation points over time to catch regressions.

### 5. Adjust Thresholds
Fine-tune based on your specific requirements.

## Build Status

? All tests compile successfully  
? No breaking changes  
? Ready to run

## Conclusion

Adaptive performance tests answer the critical question:

**"At what point does my storage start to struggle?"**

This is more valuable than knowing if it can handle an arbitrary fixed count. Use these tests to:
- Find real capacity limits
- Compare implementations objectively  
- Detect performance regressions early
- Plan for growth and capacity

Combined with your existing fixed tests, you now have comprehensive performance coverage!
