# Adaptive Performance Tests

## Overview

The `AdaptivePerformanceTests` class provides intelligent performance testing that **continues until performance degrades**, rather than stopping at a fixed record count. This reveals the actual capacity and performance characteristics of each storage implementation under sustained load.

## Why Adaptive Tests?

Traditional performance tests use fixed record counts (e.g., 1 million records), but this has limitations:

1. **Arbitrary Limits**: Why 1M? Why not 500K or 10M?
2. **Doesn't Find Breaking Point**: Test may pass but storage degrades at 2M
3. **No Early Warning**: Doesn't detect gradual degradation
4. **Different for Each Storage**: InMemory might handle 10M easily, SQLite might slow at 500K

**Adaptive tests solve this** by automatically finding where each storage implementation starts to struggle.

## Test Methods

### 1. `AdaptivePerformance_UntilDegradation`

**Purpose**: Continues inserting until write OR read performance degrades beyond threshold (2x baseline).

**How It Works**:
```
1. Warmup Phase (10 batches):
   - Insert batches to establish baseline
   - Calculate average write time
   
2. Measurement Phase:
   - Continue inserting batches of 1,000 records
   - Every 10 batches, check performance:
     * Measure recent write average
     * Measure full read time
     * Compare to baseline
   
3. Stop Conditions:
   - Write time > 2x baseline, OR
   - Read time > 2x baseline, OR
   - Reach 10M records (safety limit)
```

**Example Output**:
```
=== Adaptive Performance Test: Hybrid ===
Batch Size: 1,000
Degradation Threshold: 2.0x baseline
Warmup Batches: 10

Baseline established after 10 batches:
  - Baseline write time: 15.32 ms/batch
  - Records so far: 10,000

Batch 20: 20,000 records
  Write: 16.45 ms (avg: 16.12 ms, baseline: 15.32 ms, ratio: 1.05x)
  Read:  245.67 ms (baseline: 234.21 ms, ratio: 1.05x)
  Memory: 2.45 MB, Disk: 3.21 MB

Batch 30: 30,000 records
  Write: 17.23 ms (avg: 16.89 ms, baseline: 15.32 ms, ratio: 1.10x)
  Read:  267.34 ms (baseline: 234.21 ms, ratio: 1.14x)
  Memory: 3.67 MB, Disk: 4.82 MB

...

Batch 850: 850,000 records
  Write: 45.67 ms (avg: 42.34 ms, baseline: 15.32 ms, ratio: 2.76x)
  Read:  1234.56 ms (baseline: 234.21 ms, ratio: 5.27x)
  Memory: 95.42 MB, Disk: 124.56 MB
  *** DEGRADATION DETECTED ***

=== Test Complete: Degradation Detected ===
Reason: Write performance degraded: 42.34 ms > 30.64 ms (2x baseline) AND Read performance degraded: 1234.56 ms > 468.42 ms (2x baseline)
Total Records: 850,000
Total Time: 145.67s
Final Memory: 95.42 MB
Final Disk: 124.56 MB

=== Performance Analysis ===
Write Timings:
  Min:    14.23 ms
  Max:    48.92 ms
  Mean:   23.45 ms
  Median: 19.67 ms
  StdDev: 8.34 ms
Read Timings:
  Min:    223.45 ms
  Max:    1289.34 ms
  Mean:   567.89 ms
  Median: 445.23 ms
  StdDev: 289.12 ms
```

**Key Metrics**:
- **Degradation Point**: Where performance falls below acceptable levels
- **Capacity**: How many records before degradation
- **Performance Curve**: How performance changes over time
- **Statistical Analysis**: Min/max/mean/median/stddev

### 2. `StressTest_UntilSevereDegradation`

**Purpose**: More aggressive test - continues until 5x degradation for stress testing.

**Configuration**:
- Larger batch size: 5,000 records
- Higher degradation threshold: 5x baseline
- Fewer warmup batches: 5

**Use Case**: Finding absolute breaking points, not practical limits.

**Example**:
```
=== Stress Test: InMemory ===
This test will continue until write performance degrades to 5.0x baseline

Baseline: 42.34 ms/batch (25,000 records)
50,000 records: 45.67 ms (1.08x baseline)
100,000 records: 52.34 ms (1.24x baseline)
500,000 records: 89.23 ms (2.11x baseline)
1,000,000 records: 156.78 ms (3.70x baseline)
1,250,000 records: 223.45 ms (5.28x baseline)
SEVERE DEGRADATION at 1,250,000 records: 223.45 ms (5.28x baseline)

Test completed with 1,250,000 records
Final write time: 223.45 ms (5.28x baseline)
```

### 3. `ThroughputDegradation_RecordsPerSecond`

**Purpose**: Measures throughput (records/sec) instead of time, stops when throughput drops below 50% of baseline.

**Why Throughput?**: More intuitive metric - "X records per second" vs "Y milliseconds per batch"

**Configuration**:
- Batch size: 1,000 records
- Warmup: 10 batches
- Degradation: Drop to 50% throughput

**Example Output**:
```
=== Throughput Degradation Test: Sqlite ===
Will stop when throughput drops below 50% of baseline

Baseline throughput: 15,234 records/sec
10,000 records: 15,456 rec/sec (recent avg: 15,345, 101% of baseline)
20,000 records: 14,987 rec/sec (recent avg: 15,123, 99% of baseline)
100,000 records: 12,456 rec/sec (recent avg: 12,789, 84% of baseline)
150,000 records: 9,876 rec/sec (recent avg: 10,234, 67% of baseline)
180,000 records: 7,456 rec/sec (recent avg: 7,567, 50% of baseline)

*** THROUGHPUT DEGRADATION DETECTED ***
Dropped to 50% of baseline (7,567 vs 15,234 rec/sec)

Completed with 180,000 records
Final throughput: 7,456 rec/sec (49% of baseline)
```

## Comparison: Fixed vs Adaptive Tests

| Aspect | Fixed Test (1M records) | Adaptive Test |
|--------|------------------------|---------------|
| **Duration** | Predictable | Variable (stops when degraded) |
| **Insight** | "Can handle 1M" | "Degrades at 850K" |
| **Early Warning** | No | Yes - detects gradual slowdown |
| **Breaking Point** | Unknown | Found automatically |
| **Storage Comparison** | Hard to compare | Easy - see where each struggles |
| **CI/CD** | Safe (known time) | Risky (could timeout) |

## When to Use Each

### Use Fixed Tests When:
- ? CI/CD pipeline (need predictable duration)
- ? Regression testing (compare to previous runs)
- ? Acceptance criteria (must handle X records)
- ? Quick smoke tests

### Use Adaptive Tests When:
- ? Capacity planning (how much can it handle?)
- ? Comparing implementations (which is better for large datasets?)
- ? Finding bottlenecks (where does it slow down?)
- ? Stress testing (what's the absolute limit?)
- ? Performance profiling (understand degradation curve)

## Configuration Parameters

### Degradation Threshold
```csharp
const double degradationThreshold = 2.0; // 2x slower
```
- **Conservative (1.5x)**: Strict - stops at first slowdown
- **Balanced (2.0x)**: Reasonable - allows some variance
- **Aggressive (5.0x)**: Lenient - finds severe issues only

### Warmup Batches
```csharp
const int warmupBatches = 10;
```
- **Purpose**: Establish stable baseline, account for JIT/GC warmup
- **Too Few (< 5)**: Unstable baseline
- **Too Many (> 20)**: Delays test start
- **Recommended**: 5-10 for stress tests, 10-20 for adaptive tests

### Check Interval
```csharp
const int checkInterval = 10; // Check every N batches
```
- **Frequent (every 1-5)**: Catches degradation early, more read overhead
- **Moderate (every 10)**: Balanced
- **Infrequent (every 50)**: Less overhead, might overshoot degradation point

### Batch Size
```csharp
const int batchSize = 1000;
```
- **Small (100-500)**: More data points, longer test
- **Medium (1000-5000)**: Balanced
- **Large (10000+)**: Faster test, less granular

## Test Categories

Tests are marked with categories for easy filtering:

```csharp
[TestCategory("Performance")]  // All performance tests
[TestCategory("Stress")]        // Aggressive stress tests
```

**Run only adaptive tests**:
```bash
dotnet test --filter "FullyQualifiedName~AdaptivePerformanceTests"
```

**Run only stress tests**:
```bash
dotnet test --filter "TestCategory=Stress"
```

**Exclude stress tests from CI**:
```bash
dotnet test --filter "TestCategory!=Stress"
```

## Timeout Configuration

All tests have timeouts to prevent hanging:

```csharp
[Timeout(300000)]  // 5 minutes for adaptive tests
[Timeout(600000)]  // 10 minutes for stress tests
```

Adjust based on expected duration:
- Faster storage (InMemory): Lower timeout
- Slower storage (SQLite): Higher timeout
- CI/CD: Conservative timeout

## Interpreting Results

### Healthy Storage
```
Degradation at: 2,000,000+ records
Write ratio at degradation: ~2.0x
Read ratio at degradation: ~2.0x
Pattern: Gradual, linear increase
```

### Problem Storage
```
Degradation at: < 100,000 records
Write ratio at degradation: > 5.0x
Read ratio at degradation: > 10.0x
Pattern: Sudden spike, exponential growth
```

### Performance Curves

**Good (InMemory)**:
```
Records  | Write Time | Ratio
---------|------------|-------
10K      | 15 ms      | 1.0x
100K     | 18 ms      | 1.2x
500K     | 25 ms      | 1.67x
1M       | 32 ms      | 2.13x  ? Gradual increase
```

**Problematic (Hypothetical bad storage)**:
```
Records  | Write Time | Ratio
---------|------------|-------
10K      | 15 ms      | 1.0x
50K      | 16 ms      | 1.07x
100K     | 89 ms      | 5.93x  ? Sudden spike!
150K     | 234 ms     | 15.6x
```

## Statistical Analysis

The test provides detailed statistics:

### Write Timings
- **Min/Max**: Range of performance
- **Mean**: Average performance
- **Median**: Typical performance (less affected by outliers)
- **StdDev**: Consistency (low = stable, high = erratic)

### Performance Patterns

**Stable Storage** (Low StdDev):
```
Mean:   25.34 ms
Median: 24.67 ms  ? Close to mean
StdDev: 3.45 ms   ? Low variance
```

**Unstable Storage** (High StdDev):
```
Mean:   67.89 ms
Median: 34.23 ms  ? Much lower than mean (outliers)
StdDev: 89.12 ms  ? High variance
```

## Example Scenarios

### Scenario 1: Comparing Implementations

Run adaptive test for all three storage types:

```bash
dotnet test --filter "FullyQualifiedName~AdaptivePerformance_UntilDegradation"
```

**Expected Results**:
- **InMemory**: Degrades at ~10M records (memory limit)
- **SQLite**: Degrades at ~500K records (disk I/O)
- **Hybrid**: Degrades at ~2M records (balanced)

**Insight**: Hybrid provides 4x capacity of pure SQLite!

### Scenario 2: Capacity Planning

"How many log entries can we process before slowdown?"

Run `AdaptivePerformance_UntilDegradation` with your data:
- Finds degradation point: 850K records
- Plan for: 500K records (safety margin)
- Monitor: Alert if approaching 700K

### Scenario 3: Regression Detection

Track degradation point over time:
- **v1.0**: Degrades at 850K records
- **v1.1**: Degrades at 720K records ? **Regression!**
- **v1.2**: Degrades at 920K records ? **Improvement!**

## Best Practices

### 1. Run Locally First
Don't run adaptive tests in CI until you know their duration:
```bash
# Local run to gauge time
dotnet test --filter "FullyQualifiedName~AdaptivePerformance" -v n
```

### 2. Use Appropriate Thresholds
- **Development**: 1.5x (catch issues early)
- **CI/CD**: 2.0x (balance detection and false positives)
- **Stress**: 5.0x (find absolute limits)

### 3. Combine with Fixed Tests
```
CI Pipeline:
  ??? Fixed Test (1M records) ? Fast, predictable
  ??? Nightly:
      ??? Adaptive Test ? Thorough, finds issues
```

### 4. Monitor Trends
Log degradation points over time:
```
v1.0: 850K records
v1.1: 720K records ? Investigate!
v1.2: 920K records ? Improvement confirmed
```

### 5. Set Realistic Timeouts
Base on observed duration:
```csharp
// InMemory: Fast, tight timeout
[Timeout(120000)]  // 2 minutes

// Hybrid: Medium, moderate timeout  
[Timeout(300000)]  // 5 minutes

// SQLite: Slow, generous timeout
[Timeout(600000)]  // 10 minutes
```

## Future Enhancements

Potential improvements to adaptive tests:

1. **Memory Pressure Monitoring**: Stop if process memory exceeds limit
2. **Dynamic Threshold**: Adjust based on variance (more lenient if unstable)
3. **Multi-Metric**: Combine write, read, and memory into single score
4. **Visualization**: Export to CSV for graphing performance curves
5. **Comparison Mode**: Run all storage types, generate comparison report
6. **Predictive**: Extrapolate to estimate when degradation will occur

## Troubleshooting

### Test Times Out
**Cause**: Degradation threshold too high or storage very slow  
**Solution**: Lower threshold or increase timeout

### Test Completes at Max Batches
**Cause**: Storage never degrades (threshold too high)  
**Solution**: Lower threshold or increase max batches

### Erratic Results
**Cause**: Background processes, GC pauses  
**Solution**: Increase warmup batches, run on dedicated machine

### Test Fails Assertion
**Cause**: Didn't complete warmup or establish baseline  
**Solution**: Check for early crashes, lower batch sizes

## Conclusion

Adaptive performance tests provide insights that fixed tests cannot:
- **Actual capacity** of each storage implementation
- **Performance curves** showing degradation patterns
- **Early warning** of performance issues
- **Comparative analysis** between implementations

Use them alongside fixed tests for comprehensive performance coverage.
