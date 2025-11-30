# Stress Test with Dual Thresholds and Graphs

## Overview

The `StressTest_UntilSevereDegradation` has been enhanced with:
1. **Dual threshold system** (relative + absolute)
2. **Automatic graph generation** (CSV + interactive HTML)
3. **Four specialized charts** for stress analysis

## Dual Threshold System

### Why Two Thresholds?

**Single threshold problem**: 
- If baseline is 5ms, 5x = 25ms might still be acceptable
- If baseline is 100ms, 5x = 500ms is way too slow

**Solution**: Use BOTH thresholds
- **Relative (5x)**: Detects performance degradation compared to start
- **Absolute (200ms/25K)**: Ensures operations never get too slow regardless of baseline

### The Thresholds

#### 1. Relative Threshold: **5x Baseline**
```
If recent_time > baseline_time × 5.0
Then: STOP - Performance degraded significantly
```

**Example**:
- Baseline: 10ms ? Stops at 50ms
- Baseline: 50ms ? Stops at 250ms

**Good for**: Detecting degradation patterns

#### 2. Absolute Threshold: **200ms per 25,000 records**
```
Scaled to batch size: 200ms × (5000 / 25000) = 40ms per 5K batch

If recent_time > 40ms (for 5K batches)
Then: STOP - Too slow regardless of baseline
```

**Example**:
- Any write taking > 40ms per 5K batch stops the test
- Equivalent to > 200ms per 25K batch

**Good for**: Hard performance guarantees

### Test Stops When **EITHER** Condition Met

```
Stop if:
   recent_time > baseline_time × 5.0
   OR
   recent_time > 40ms (per 5K batch)
```

This ensures:
- ? Fast baseline + degradation detected (relative)
- ? Slow baseline doesn't run forever (absolute)
- ? Absolute performance guarantee (200ms/25K)

## Generated Outputs

### 1. CSV File
```
StressTest_Hybrid_20241215_143022.csv
```

**Columns**:
- BatchNumber
- TotalRecords
- WriteTimeMs
- RecentWriteAvgMs
- WriteRatio (recent / baseline)
- MemoryMB
- DiskMB
- BaselineWriteMs
- AbsoluteThresholdMs

### 2. Interactive HTML Graph
```
StressTest_Hybrid_20241215_143022.html
```

**Four Charts**:
1. Write Performance with Dual Thresholds
2. Performance Ratio vs Baseline
3. Storage Usage Over Time
4. Performance Heatmap

## The Four Charts

### Chart 1: Write Performance with Dual Thresholds
```
Y-axis: Write Time (ms) - LOG SCALE
X-axis: Total Records

Lines:
- Blue solid:   Actual write times
- Red dashed:   Absolute threshold (40ms)
- Orange dotted: Relative threshold (baseline × 5)
```

**Why log scale?**: Shows exponential degradation clearly

**Example**:
```
???????????????????????????????????????
? Write Time (ms) - Log Scale         ?
?                                     ?
? 100 ?                          ?    ? ? Hit threshold!
?  50 ?                     ?         ?
?  40 ?? ? ? ? ? ? ? ? ??? ? ? ? ? ?? ? Absolute (red)
?  25 ?           ?                   ? ? Relative (orange)
?  10 ?     ?                         ?
?   5 ??                              ? ? Baseline
???????????????????????????????????????
  0    250K  500K  750K  1M
```

**Interpretation**:
- Flat line: Stable performance
- Gradual slope: Expected degradation
- Exponential curve: Serious issues
- Crossing red line: Hit absolute limit
- Crossing orange line: Hit relative limit

### Chart 2: Performance Ratio vs Baseline
```
Y-axis: Performance Ratio (x baseline)
X-axis: Total Records

Area chart (filled) showing cumulative impact
Lines:
- Purple filled: Write ratio
- Green dashed: Baseline (1.0x)
- Red dashed: Degradation (5.0x)
```

**Example**:
```
???????????????????????????????????????
? Performance Ratio                   ?
?                                     ?
? 6.0x ?                         ???  ? ? Degraded
? 5.0x ?? ? ? ? ? ? ? ? ? ? ??? ? ? ?? ? Threshold
? 4.0x ?                   ???         ?
? 3.0x ?              ???              ?
? 2.0x ?         ???                   ?
? 1.0x ?????????????????????????????? ? Baseline
???????????????????????????????????????
  0    500K   1M    1.5M  2M
```

**Interpretation**:
- Filled area = Total performance impact
- Larger area = More overall degradation
- Steep slope = Rapid degradation
- Plateau = Stabilized (new steady state)

### Chart 3: Storage Usage Over Time (Log Scale)
```
Y-axis: Storage Size (MB) - LOG SCALE
X-axis: Total Records

Filled areas:
- Purple: Memory usage
- Cyan: Disk usage
```

**Why log scale?**: Storage can grow exponentially

**Example**:
```
???????????????????????????????????????
? Storage (MB) - Log Scale            ?
?                                     ?
?1000 ?                   ????????????? ? Disk
? 500 ?            ???????             ?
? 200 ?      ??????                    ?
? 100 ?????????????????                ? ? Memory
?  50 ????????                         ?
?  10 ??                                ?
???????????????????????????????????????
  0    500K   1M    1.5M  2M
```

**Interpretation**:
- Memory plateau + Disk growth = Spilling working
- Both growing = Using both storages
- Memory > Disk = InMemory focused
- Disk > Memory = SQLite/Hybrid with heavy spilling

### Chart 4: Performance Heatmap
```
Y-axis: Performance Ratio - LOG SCALE
X-axis: Total Records

Scatter plot with color-coded markers:
?? Green: Excellent (< 1.5x)
?? Yellow: Acceptable (1.5x - 3.0x)
?? Orange: Concerning (3.0x - 4.5x)
?? Red: Critical (> 4.5x)
```

**Example**:
```
???????????????????????????????????????
? Ratio (Log) - Color Heatmap         ?
?                                     ?
? 10x ?                          ??   ?
? 5x  ?                       ??      ?
? 3x  ?                 ????           ?
? 2x  ?          ????                  ?
? 1x  ? ????????                        ?
???????????????????????????????????????
  0    500K   1M    1.5M  2M
```

**Interpretation**:
- Green cluster: Safe operating zone
- Yellow transition: Starting to degrade
- Orange/Red: Problem zone
- Tight cluster: Consistent performance
- Scattered: Erratic/unpredictable

## Example Test Output

```
=== Stress Test: Hybrid ===
This test will continue until write performance degrades to 5.0x baseline
OR until write time exceeds 40.00 ms per 5,000 record batch
(equivalent to 200.00 ms per 25,000 records)

Baseline: 8.45 ms/batch (25,000 records)

Batch 50: 250,000 records
  Write: 9.23 ms (avg: 9.12 ms, baseline: 8.45 ms, ratio: 1.08x)
  Absolute threshold: 40.00 ms (current: 9.12 ms)
  Memory: 28.42 MB, Disk: 15.67 MB

Batch 500: 2,500,000 records
  Write: 28.45 ms (avg: 27.89 ms, baseline: 8.45 ms, ratio: 3.30x)
  Absolute threshold: 40.00 ms (current: 27.89 ms)
  Memory: 98.23 MB, Disk: 287.45 MB

Batch 850: 4,250,000 records
  Write: 45.67 ms (avg: 43.21 ms, baseline: 8.45 ms, ratio: 5.11x)
  Absolute threshold: 40.00 ms (current: 43.21 ms)
  Memory: 99.87 MB, Disk: 512.34 MB
  *** DEGRADATION DETECTED ***

Stress test data exported to: C:\...\StressTest_Hybrid_20241215.csv

Interactive stress test graph generated: C:\...\StressTest_Hybrid_20241215.html
Open this file in a web browser to view the performance graph

=== Test Complete: Degradation Detected ===
Reason: Write performance degraded: 43.21 ms > 42.25 ms (5.0x baseline) AND Write time exceeded absolute threshold: 43.21 ms > 40.00 ms per batch
Total Records: 4,250,000
Total Time: 145.67s
Final write time: 45.67 ms (5.40x baseline)
```

## Key Differences from Adaptive Test

| Feature | Adaptive Test | Stress Test |
|---------|--------------|-------------|
| **Batch Size** | 1,000 | 5,000 (5x larger) |
| **Degradation Threshold** | 2.0x | 5.0x (more lenient) |
| **Absolute Threshold** | None | 200ms/25K records |
| **Read Testing** | Yes (every check) | No (write-only) |
| **Charts** | 3 charts | 4 charts |
| **Focus** | Practical limits | Breaking points |
| **Warmup** | 10 batches | 5 batches |
| **Duration** | Shorter | Longer |

## When to Use Each Test

### Use Adaptive Test When:
- ? Finding practical capacity limits
- ? Need read performance data
- ? Want detailed degradation tracking
- ? Setting operational thresholds
- ? Comparing storage for typical workloads

### Use Stress Test When:
- ? Finding absolute breaking points
- ? Stress testing with large batches
- ? Need hard performance guarantees (200ms/25K)
- ? Testing extreme scenarios
- ? Capacity planning for worst case

## Interpreting the Dual Threshold Results

### Scenario 1: Hit Relative Threshold First
```
Baseline: 5ms
Hit relative: 25ms (5x)
Absolute threshold: 40ms
```

**Meaning**: Performance degraded significantly but still below absolute limit.
**Action**: Investigate what changed - GC, disk I/O, memory pressure.

### Scenario 2: Hit Absolute Threshold First
```
Baseline: 60ms
Hit absolute: 40ms (0.67x!)
Absolute threshold: 40ms
```

**Meaning**: Actually improved but still hit absolute limit!
**Action**: Baseline was too slow, need architectural changes.

### Scenario 3: Hit Both Simultaneously
```
Baseline: 8ms
Hit relative: 40ms (5x)
Also hit absolute: 40ms
```

**Meaning**: Degraded to exactly the hard limit.
**Action**: This is the designed breaking point.

### Scenario 4: Never Hit Either
```
Baseline: 5ms
Max reached: 15ms (3x)
Never hit 25ms relative or 40ms absolute
```

**Meaning**: Storage handles max batches without degrading.
**Action**: Increase max batches or find actual limit.

## Real-World Example: Choosing Storage

### Test Results

**InMemory**:
```
Baseline: 2ms
Hit: Absolute threshold at 10M records (45ms)
Reason: Memory exhausted, GC pressure
```

**SQLite**:
```
Baseline: 45ms
Hit: Absolute threshold immediately (45ms > 40ms)
Reason: Baseline too slow for requirement
```

**Hybrid**:
```
Baseline: 8ms  
Hit: Relative threshold at 4.2M records (5.1x = 41ms)
Also hit: Absolute threshold (41ms > 40ms)
Reason: Perfect - degraded at designed limit
```

### Decision

**Requirement**: Must handle bursts of 25K records in < 200ms

**Analysis**:
- ? InMemory: Good until 10M but memory intensive
- ? SQLite: Too slow from start (45ms baseline)
- ? **Hybrid: Best choice**
  - Fast baseline (8ms)
  - Handles 4.2M records
  - Degrades at acceptable point (40ms = 200ms/25K)

## Customizing Thresholds

### Change Relative Threshold
```csharp
const double degradationThreshold = 5.0; // Change to 3.0 for stricter, 10.0 for lenient
```

### Change Absolute Threshold
```csharp
const double absoluteTimeThresholdMs = 200.0; // Change to 100ms for faster requirement
```

**Examples**:
- **High-performance system**: 100ms/25K records
- **Standard requirement**: 200ms/25K records
- **Relaxed requirement**: 500ms/25K records

### Change Batch Size
```csharp
const int batchSize = 5000; // Change to test different batch sizes
```

**Note**: Absolute threshold automatically scales with batch size.

## Advanced Analysis

### Export and Compare Multiple Runs

```python
import pandas as pd
import matplotlib.pyplot as plt

# Load all three storage types
inmem = pd.read_csv('StressTest_InMemory_*.csv')
sqlite = pd.read_csv('StressTest_Sqlite_*.csv')
hybrid = pd.read_csv('StressTest_Hybrid_*.csv')

# Plot all three
fig, (ax1, ax2) = plt.subplots(1, 2, figsize=(16, 6))

# Chart 1: Write times
ax1.plot(inmem['TotalRecords'], inmem['RecentWriteAvgMs'], label='InMemory', linewidth=2)
ax1.plot(sqlite['TotalRecords'], sqlite['RecentWriteAvgMs'], label='SQLite', linewidth=2)
ax1.plot(hybrid['TotalRecords'], hybrid['RecentWriteAvgMs'], label='Hybrid', linewidth=2)
ax1.axhline(y=40, color='r', linestyle='--', label='Absolute Threshold', linewidth=2)
ax1.set_xlabel('Total Records')
ax1.set_ylabel('Write Time (ms)')
ax1.set_yscale('log')
ax1.legend()
ax1.grid(True)
ax1.set_title('Write Performance Comparison')

# Chart 2: Storage usage
ax2.plot(inmem['TotalRecords'], inmem['MemoryMB'] + inmem['DiskMB'], label='InMemory Total', linewidth=2)
ax2.plot(sqlite['TotalRecords'], sqlite['MemoryMB'] + sqlite['DiskMB'], label='SQLite Total', linewidth=2)
ax2.plot(hybrid['TotalRecords'], hybrid['MemoryMB'] + hybrid['DiskMB'], label='Hybrid Total', linewidth=2)
ax2.set_xlabel('Total Records')
ax2.set_ylabel('Total Storage (MB)')
ax2.set_yscale('log')
ax2.legend()
ax2.grid(True)
ax2.set_title('Storage Usage Comparison')

plt.tight_layout()
plt.savefig('stress_test_comparison.png', dpi=300)
plt.show()
```

## Summary

The enhanced stress test provides:

? **Dual protection**: Relative + absolute thresholds  
? **Visual insights**: 4 interactive charts  
? **Data export**: CSV for custom analysis  
? **Clear results**: Know exactly why test stopped  
? **Flexible**: Customize both thresholds  
? **Realistic**: Tests with large batches (5K)  
? **Comprehensive**: Performance + storage tracking  

Use this to **stress test your storage** and **set hard limits** for production!
