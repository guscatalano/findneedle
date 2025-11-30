# Adaptive Performance Test - Graphing Features

## Overview

The `AdaptivePerformance_UntilDegradation` test now automatically generates **interactive visualizations** of performance data. No manual graphing required!

## What Gets Generated

When you run the test, it automatically creates:

### 1. **CSV File** (for data analysis)
```
AdaptivePerf_InMemory_20241215_143022.csv
```
**Location**: `%TEMP%\AdaptivePerf_{StorageType}_{Timestamp}.csv`

**Contents**:
- Batch numbers
- Total record counts
- Write times (ms)
- Recent write averages
- Performance ratios
- Read times
- Memory/Disk usage
- Baselines

**Use for**: Excel, Python pandas, R, MATLAB, or any data analysis tool

### 2. **Interactive HTML Graph** (for visualization)
```
AdaptivePerf_InMemory_20241215_143022.html
```
**Location**: `%TEMP%\AdaptivePerf_{StorageType}_{Timestamp}.html`

**Features**:
- Three interactive charts using Plotly.js
- Hover for detailed data points
- Zoom, pan, and download capabilities
- Color-coded performance indicators
- Automatic interpretation guide

## The Three Charts

### Chart 1: Performance Ratio Over Time
**Shows**: How performance degrades compared to baseline

```
Y-axis: Performance Ratio (1.0x = baseline, 2.0x = degradation threshold)
X-axis: Total Records

Lines:
- Blue:   Write Performance Ratio
- Orange: Read Performance Ratio
- Green:  Baseline (1.0x) - dashed
- Red:    Degradation Threshold (2.0x) - dashed
```

**Interpretation**:
- Flat line near 1.0x ? Stable performance
- Gradual increase ? Predictable degradation
- Sharp spike ? Hit a bottleneck
- Above red line ? Degradation detected

**Example**:
```
???????????????????????????????????????
? Performance Ratio                   ?
?                                     ?
? 3.0x ?                         ?    ? ? Degraded!
? 2.5x ?                      ?       ?
? 2.0x ?? ? ? ? ? ? ? ? ?? ? ? ? ? ?? ? Threshold
? 1.5x ?            ?                 ?
? 1.0x ?????????????????????????????? ? Baseline
? 0.5x ?                              ?
???????????????????????????????????????
  0     250K   500K   750K   1M
         Total Records
```

### Chart 2: Storage Usage Over Time
**Shows**: How memory and disk usage grow

```
Y-axis: Storage Size (MB)
X-axis: Total Records

Areas:
- Purple: Memory Usage (filled)
- Cyan:   Disk Usage (filled)
```

**Interpretation**:
- Memory grows linearly ? Good caching
- Memory plateaus ? Hit threshold, spilling to disk
- Disk grows rapidly ? Most data on disk
- Memory >> Disk ? InMemory storage
- Disk >> Memory ? SQLite or Hybrid with spilling

**Example**:
```
???????????????????????????????????????
? Storage Size (MB)                   ?
?                                     ?
? 200 ?             ????????????????? ? Disk
? 150 ?          ???????????          ?
? 100 ?       ???????                 ?
?  50 ? ???????????????????????       ? ? Memory
?   0 ??????????????????????????????  ?
???????????????????????????????????????
  0     250K   500K   750K   1M
```

### Chart 3: Performance vs Record Count (Scatter)
**Shows**: Correlation between data size and performance

```
Y-axis: Write Performance Ratio
X-axis: Total Records

Points:
- Color scale: Green (good) ? Yellow (warning) ? Red (degraded)
- Size: 10px markers
- Hover: Shows exact values
```

**Interpretation**:
- Green cluster ? Healthy performance range
- Yellow points ? Warning zone
- Red points ? Degradation zone
- Linear pattern ? Predictable scaling
- Cluster then spike ? Hit a bottleneck
- Random scatter ? Erratic performance

**Example**:
```
???????????????????????????????????????
? Write Performance Ratio             ?
?                                     ?
? 3.0x ?                          ??  ?
? 2.5x ?                       ??     ?
? 2.0x ?                    ??        ?
? 1.5x ?              ??              ?
? 1.0x ?  ??????????                   ?
???????????????????????????????????????
  0     250K   500K   750K   1M
```

## Example Test Output

```
=== Adaptive Performance Test: Hybrid ===
Batch Size: 1,000
Degradation Threshold: 2.0x baseline
Warmup Batches: 10

Baseline established after 10 batches:
  - Baseline write time: 15.32 ms/batch
  - Records so far: 10,000

... (test progress) ...

Batch 850: 850,000 records
  Write: 45.67 ms (avg: 42.34 ms, ratio: 2.76x)
  *** DEGRADATION DETECTED ***

Performance data exported to: C:\Users\...\Temp\AdaptivePerf_Hybrid_20241215_143022.csv
You can graph this data in Excel, Python, or online tools like plot.ly

Interactive graph generated: C:\Users\...\Temp\AdaptivePerf_Hybrid_20241215_143022.html
Open this file in a web browser to view the performance graph

=== Test Complete: Degradation Detected ===
```

## Using the Generated Files

### Option 1: View HTML Graph (Easiest)

1. Look for the console output: `Interactive graph generated: ...`
2. Copy the file path
3. Open in any web browser:
   ```
   - Double-click the file in Explorer
   - Or drag-and-drop into browser
   - Or right-click ? Open with ? Browser
   ```
4. Interact with the charts:
   - **Hover**: See exact values
   - **Zoom**: Click and drag on chart
   - **Pan**: Double-click to reset
   - **Download**: Camera icon (top right of each chart)

### Option 2: Analyze CSV Data

#### Excel
```
1. Open Excel
2. Data ? From Text/CSV
3. Select the generated CSV file
4. Create your own charts
```

#### Python (pandas + matplotlib)
```python
import pandas as pd
import matplotlib.pyplot as plt

# Load data
df = pd.read_csv('AdaptivePerf_Hybrid_20241215_143022.csv')

# Plot write performance
plt.figure(figsize=(12, 6))
plt.plot(df['TotalRecords'], df['WriteRatio'], label='Write Ratio')
plt.plot(df['TotalRecords'], df['ReadRatio'], label='Read Ratio')
plt.axhline(y=1.0, color='g', linestyle='--', label='Baseline')
plt.axhline(y=2.0, color='r', linestyle='--', label='Threshold')
plt.xlabel('Total Records')
plt.ylabel('Performance Ratio')
plt.title('Adaptive Performance Test')
plt.legend()
plt.grid(True)
plt.show()
```

#### Python (pandas + plotly)
```python
import pandas as pd
import plotly.graph_objects as go

df = pd.read_csv('AdaptivePerf_Hybrid_20241215_143022.csv')

fig = go.Figure()
fig.add_trace(go.Scatter(x=df['TotalRecords'], y=df['WriteRatio'], 
                         mode='lines+markers', name='Write'))
fig.add_trace(go.Scatter(x=df['TotalRecords'], y=df['ReadRatio'], 
                         mode='lines+markers', name='Read'))
fig.update_layout(title='Performance Degradation', 
                  xaxis_title='Records', 
                  yaxis_title='Ratio')
fig.show()
```

#### R
```r
library(ggplot2)

data <- read.csv('AdaptivePerf_Hybrid_20241215_143022.csv')

ggplot(data, aes(x=TotalRecords)) +
  geom_line(aes(y=WriteRatio, color='Write')) +
  geom_line(aes(y=ReadRatio, color='Read')) +
  geom_hline(yintercept=1.0, linetype='dashed', color='green') +
  geom_hline(yintercept=2.0, linetype='dashed', color='red') +
  labs(title='Performance Degradation',
       x='Total Records',
       y='Performance Ratio') +
  theme_minimal()
```

### Option 3: Online Graphing Tools

Upload the CSV to:
- **Plot.ly Chart Studio**: https://chart-studio.plotly.com/
- **Google Sheets**: Import ? Create chart
- **Excel Online**: OneDrive ? Upload ? Create chart
- **Datawrapper**: https://www.datawrapper.de/

## Understanding the HTML Graph Features

### Interactive Controls

| Control | Action |
|---------|--------|
| **Hover** | See exact values for any point |
| **Click Legend** | Show/hide specific traces |
| **Click & Drag** | Zoom into a region |
| **Double-Click** | Reset zoom to original view |
| **Scroll Wheel** | Zoom in/out |
| **Camera Icon** | Download chart as PNG |
| **Home Icon** | Reset view |

### Color Coding

**Performance Ratio Chart**:
- ?? Green line: Baseline (1.0x) - Target performance
- ?? Red line: Threshold (2.0x) - Degradation limit
- ?? Blue line: Write performance
- ?? Orange line: Read performance

**Scatter Plot**:
- ?? Green dots: Good performance (< 1.5x)
- ?? Yellow dots: Warning (1.5x - 2.0x)
- ?? Red dots: Degraded (> 2.0x)

### Information Sections

The HTML includes:
1. **Test Summary**: Key metrics, status, degradation reason
2. **Chart Interpretations**: What each chart shows
3. **Usage Guide**: How to read the charts
4. **Interpretation Guide**: What patterns mean

## Comparing Multiple Storage Types

Run the test for each storage type:

```bash
# Generate graphs for all storage types
dotnet test --filter "FullyQualifiedName~AdaptivePerformance_UntilDegradation"
```

This creates three sets of files:
- `AdaptivePerf_InMemory_*.html`
- `AdaptivePerf_Sqlite_*.html`
- `AdaptivePerf_Hybrid_*.html`

**Visual Comparison**:
1. Open all three HTML files in browser tabs
2. Switch between tabs to compare
3. Look for:
   - Which degrades first?
   - Which has steeper growth?
   - Which uses more memory/disk?
   - Which has more stable performance?

**CSV Comparison** (Python):
```python
import pandas as pd
import matplotlib.pyplot as plt

# Load all three
inmem = pd.read_csv('AdaptivePerf_InMemory_*.csv')
sqlite = pd.read_csv('AdaptivePerf_Sqlite_*.csv')
hybrid = pd.read_csv('AdaptivePerf_Hybrid_*.csv')

# Plot comparison
plt.figure(figsize=(14, 6))
plt.plot(inmem['TotalRecords'], inmem['WriteRatio'], label='InMemory', linewidth=2)
plt.plot(sqlite['TotalRecords'], sqlite['WriteRatio'], label='SQLite', linewidth=2)
plt.plot(hybrid['TotalRecords'], hybrid['WriteRatio'], label='Hybrid', linewidth=2)
plt.axhline(y=2.0, color='r', linestyle='--', label='Threshold')
plt.xlabel('Total Records')
plt.ylabel('Write Performance Ratio')
plt.title('Storage Type Comparison')
plt.legend()
plt.grid(True)
plt.savefig('storage_comparison.png', dpi=300, bbox_inches='tight')
plt.show()
```

## Real-World Example Analysis

### Scenario: Choosing Storage for 500K Records

**Test Results**:
```
InMemory:  Degrades at 10,000,000 records (ratio: 2.1x)
SQLite:    Degrades at   500,000 records (ratio: 2.3x)
Hybrid:    Degrades at 2,000,000 records (ratio: 2.0x)
```

**Graph Analysis**:

**InMemory** (Green line):
- Flat until 8M records
- Slight increase at 9M
- Degradation at 10M

**SQLite** (Blue line):
- Linear growth from start
- Crosses threshold at 500K
- Steeper than others

**Hybrid** (Purple line):
- Flat until 1.5M
- Moderate increase
- Degradation at 2M

**Decision**:
- **For 500K records**: Use Hybrid (well below degradation point)
- **InMemory**: Overkill (degrades at 10M, wastes memory)
- **SQLite**: At limit (degrades exactly at 500K)
- **Hybrid**: Optimal (degrades at 2M, 4x headroom)

## File Management

### Finding Generated Files

Console output shows full paths:
```
Performance data exported to: C:\Users\YourName\AppData\Local\Temp\AdaptivePerf_Hybrid_20241215_143022.csv
Interactive graph generated: C:\Users\YourName\AppData\Local\Temp\AdaptivePerf_Hybrid_20241215_143022.html
```

### Windows
```cmd
# Open temp folder
%TEMP%

# Search for files
dir %TEMP%\AdaptivePerf_*.html
dir %TEMP%\AdaptivePerf_*.csv
```

### PowerShell
```powershell
# List all adaptive perf files
Get-ChildItem $env:TEMP -Filter "AdaptivePerf_*.*"

# Open most recent HTML
Get-ChildItem $env:TEMP -Filter "AdaptivePerf_*.html" | 
    Sort-Object LastWriteTime -Descending | 
    Select-Object -First 1 | 
    Invoke-Item
```

### Cleanup
```powershell
# Remove old test files (older than 7 days)
Get-ChildItem $env:TEMP -Filter "AdaptivePerf_*.*" | 
    Where-Object {$_.LastWriteTime -lt (Get-Date).AddDays(-7)} | 
    Remove-Item
```

## Advanced: Custom Graphs

### Add Your Own Chart to HTML

The HTML generator is in `GeneratePlotlyHtml()`. You can:

1. **Add a fourth chart**:
```csharp
// In GeneratePlotlyHtml method
var html = $@"...
    <div class='chart'>
        <h2>Your Custom Chart</h2>
        <div id='customChart'></div>
    </div>
...
<script>
    // Add your Plotly chart
    var customTrace = {{ ... }};
    Plotly.newPlot('customChart', [customTrace], layout);
</script>
...";
```

2. **Change chart types**:
- `type: 'scatter'` ? `type: 'bar'` for bar chart
- `type: 'scatter'` ? `type: 'box'` for box plot
- Add `mode: 'lines'` for line only
- Add `mode: 'markers'` for scatter only

3. **Customize colors**:
```javascript
line: { color: '#YOUR_HEX_COLOR', width: 2 }
```

## Troubleshooting

### HTML File Won't Open
**Issue**: Security warning or file won't open  
**Solution**: 
- Right-click ? Properties ? Unblock
- Or copy to Desktop and open from there

### CSV Shows in Notepad
**Issue**: CSV opens in Notepad instead of Excel  
**Solution**: 
- Right-click ? Open with ? Excel
- Or change default program for .csv files

### Charts Are Empty
**Issue**: No data points visible  
**Solution**:
- Check if test ran long enough (past warmup)
- Verify data was collected (check CSV has rows)
- Check console for errors

### Performance: Too Fast/Too Slow
**Issue**: Test completes too quickly or takes too long  
**Solution**:
- Adjust `batchSize` (smaller = more points, slower test)
- Adjust `checkInterval` (smaller = more frequent checks)
- Adjust `degradationThreshold` (lower = stops sooner)

## Summary

? **Automatic**: Graphs generated without extra work  
? **Interactive**: Hover, zoom, pan in browser  
? **Exportable**: Download as PNG or use CSV  
? **Comprehensive**: Three different views of performance  
? **Shareable**: Send HTML file to colleagues  
? **Analyzable**: CSV for deep analysis in any tool  

The adaptive performance test now provides **complete visibility** into how your storage performs as data grows!
