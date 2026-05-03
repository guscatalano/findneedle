# Native Result Viewer Specification

**Version:** 1.0  
**Last Updated:** May 3, 2026  
**Status:** Production Ready

---

## Overview

The **Native Result Viewer** is a WinUI 3-based result viewer for FindNeedle that provides a native Windows experience with full integration into the application's theme and UI framework. It serves as an alternative to the WebView2-based web viewer, offering better performance and deeper Windows integration.

---

## Architecture

### Component Structure

```
NativeResultsPage.xaml
    ↓
NativeResultsPage.xaml.cs (Code-behind)
    ↓
NativeResultsPageViewModel.cs (MVVM ViewModel)
    ↓
MiddleLayerService.GetLogLines() (Data Source)
```

### Data Flow

```
1. User navigates to results page
   ↓
2. ViewModel loads results from MiddleLayerService
   ↓
3. Results stored in _allResults ObservableCollection
   ↓
4. Filters applied to create _filteredResults
   ↓
5. DataGrid binds to _filteredResults
   ↓
6. User interacts with UI
   ↓
7. ViewModel updates based on user input
   ↓
8. DataGrid refreshes automatically (INotifyPropertyChanged)
```

---

## Features

### 1. Data Grid

**Control**: `CommunityToolkit.WinUI.UI.Controls.DataGrid`

**Properties**:
- `AutoGenerateColumns="False"` - Manual column definitions
- `CanUserReorderColumns="True"` - Column reordering
- `CanUserResizeColumns="True"` - Column resizing
- `CanUserSortColumns="True"` - Column sorting
- `IsReadOnly="True"` - Read-only data
- `LoadingRow="ResultsGrid_LoadingRow"` - Row styling event

**Columns**:
| Column | Binding | MinWidth | Description |
|-- ----|----- ----|----- ----|--------- ----|
| Index | `Index` | 60 | Row index |
| Time | `Time` | 160 | Formatted timestamp |
| Provider | `Provider` | 100 | Event provider |
| TaskName | `TaskName` | 120 | Task name |
| Message | `Message` | 200 | Log message |
| Source | `Source` | 100 | Source file path |
| Level | `Level` | 80 | Log level |

### 2. Search

**Control**: `TextBox`

**Features**:
- Search across all columns
- Case-insensitive matching
- Real-time filtering as user types
- Keyboard shortcut: `Ctrl+F`

**Implementation**:
```csharp
// In ViewModel
public string SearchText
{
    get => _searchText;
    set { _searchText = value; ApplyFilters(); }
}

// In code-behind
private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
{
    ViewModel.SearchText = SearchBox.Text;
}
```

### 3. Time Range Filter

**Controls**: `CalendarDatePicker` (From/To)

**Features**:
- Select date/time range
- Filters by `LogTime` property
- Clear button to reset

**Implementation**:
```csharp
// In ViewModel
public DateTime? FromDate { get; set; }
public DateTime? ToDate { get; set; }

// In ApplyFilters()
if (FromDate.HasValue)
    query = query.Where(line => line.LogTime >= FromDate.Value);
if (ToDate.HasValue)
    query = query.Where(line => line.LogTime <= ToDate.Value);
```

### 4. Level Color Coding

**Control**: `ItemsControl` with `ColorPicker`

**Features**:
- Color picker for each log level
- Dynamic row coloring
- Supports all levels: Catastrophic, Critical, Error, Warning, Info, Verbose, Debug

**Implementation**:
```csharp
// In ViewModel
private readonly Dictionary<string, string> _levelColors = new()
{
    { "Catastrophic", "#FFB3B3" },
    { "Critical", "#FFCCCC" },
    // ... other levels
};

// In code-behind
private void ResultsGrid_LoadingRow(object sender, DataGridRowEventArgs e)
{
    if (e.Row.DataContext is LogLine line && 
        _levelBrushKeys.TryGetValue(line.Level, out var key) &&
        Resources.TryGetValue(key, out var brush) && brush is Brush b)
    {
        e.Row.Background = b;
    }
}
```

### 5. Column Visibility Toggle

**Control**: `ContentDialog` with `CheckBox` items

**Features**:
- Toggle columns on/off
- Persistent state in ViewModel
- Apply changes after dialog closes

**Implementation**:
```csharp
// In ViewModel
private readonly Dictionary<string, bool> _columnVisibility = new()
{
    { "Index", true },
    { "Time", true },
    // ... other columns
};

// In code-behind
private void UpdateColumnVisibility()
{
    foreach (var col in ResultsGrid.Columns)
    {
        if (ViewModel.ColumnVisibility.TryGetValue(col.Header.ToString(), out var visible))
        {
            col.MinWidth = visible ? 60 : 0;
            col.Width = visible ? new DataGridLength(1, DataGridLengthUnitType.Star) 
                               : new DataGridLength(0, DataGridLengthUnitType.Auto);
        }
    }
}
```

### 6. CSV Export

**Control**: `FileSavePicker`

**Features**:
- Export visible (filtered) results
- Proper CSV escaping
- Timestamped filename

**Implementation**:
```csharp
// In ViewModel
public async Task ExportToCsvAsync()
{
    var picker = new FileSavePicker();
    picker.SuggestedFileName = $"findneedle-results-{DateTime.Now:yMMdd-HHmmss}.csv";
    
    var file = await picker.PickSaveFileAsync();
    if (file == null) return;
    
    // Generate CSV with proper escaping
    var csvLines = new List<string>();
    csvLines.Add("Index,Time,Provider,TaskName,Message,Source,Level");
    
    foreach (var line in _filteredResults)
    {
        var row = string.Join(",", 
            EscapeCsv(line.Index),
            EscapeCsv(line.Time),
            // ... other fields
        );
        csvLines.Add(row);
    }
    
    await FileIO.WriteLinesAsync(file, csvLines);
}
```

### 7. Row Details Modal

**Control**: `DataGrid.RowDetailsTemplate` with `ContentDialog`

**Features**:
- View full log entry details
- Copy as JSON functionality
- Read-only display

**Implementation**:
```xml
<DataTemplate x:Key="RowDetailsTemplate">
    <Border Padding="16">
        <StackPanel Spacing="8">
            <TextBlock Text="Log Entry Details" FontWeight="SemiBold" />
            <Grid>
                <!-- Display all LogLine properties -->
            </Grid>
            <Button Content="Copy as JSON" Click="CopyJson_Click" />
        </StackPanel>
    </Border>
</DataTemplate>
```

---

## Configuration

### Switching Viewers

**Via Settings UI**:
1. Go to Settings → System Info
2. Find "Default Result Viewer" dropdown
3. Select "Native Viewer" or "Web Viewer"

**Via Code**:
```csharp
GlobalSettings.DefaultResultViewer = "nativereviewer";
```

### Viewer Keys

| Key | Viewer | Description |
|-- --|----- ----|--------- ----|
| `nativereviewer` | Native Viewer | WinUI 3 DataGrid |
| `resultswebpage` | Web Viewer | WebView2 + DataTables |

---

## Performance

### Optimization Strategies

1. **Virtualization**: DataGrid uses recycling mode for large datasets
2. **ObservableCollection**: Automatic UI updates via INotifyCollectionChanged
3. **Lazy Loading**: Results loaded on-demand
4. **Filtering**: LINQ queries with efficient Where clauses

### Performance Targets

| Dataset Size | Expected Load Time | Memory Usage |
|-- -- ----|-- -- ----|-- -- ----|
| 100 rows | < 1 second | < 10 MB |
| 10,000 rows | < 5 seconds | < 50 MB |
| 100,000 rows | < 30 seconds | < 200 MB |

---

## Testing

### Manual Testing Checklist

- [ ] Load 100 rows - should be instant
- [ ] Load 10k rows - should be < 5 seconds
- [ ] Load 100k rows - should be < 30 seconds
- [ ] Search filter - should work on all columns
- [ ] Time range filter - should filter by date
- [ ] Column reordering - drag headers to reorder
- [ ] Column resizing - drag column edges
- [ ] Column visibility - toggle columns on/off
- [ ] Level color editing - change colors via picker
- [ ] CSV export - verify CSV format
- [ ] Row details - click row to see details
- [ ] Viewer switching - config changes take effect

### Unit Tests

- [ ] Filter logic (search, time range, per-column)
- [ ] CSV export formatting
- [ ] Color parsing
- [ ] Column visibility toggle

---

## Known Limitations

1. **No per-column filter row UI** - Filter row exists in XAML but needs implementation
2. **No state persistence** - Column widths/visibility not saved between sessions
3. **No keyboard shortcuts** - Ctrl+F not implemented
4. **No loading progress** - No progress indicator for large datasets

---

## Future Enhancements

### Phase 2
- [ ] Per-column filter row implementation
- [ ] State persistence (column widths, visible columns)
- [ ] Keyboard shortcuts (Ctrl+F, Escape)

### Phase 3
- [ ] Loading progress indicator
- [ ] Background incremental loading
- [ ] Caching strategies

### Phase 4
- [ ] Pivot table view
- [ ] Chart visualization
- [ ] Custom column templates

---

## Code Structure

### ViewModel Properties

| Property | Type | Description |
|-- -- ----|-- -- ----|--------- ----|
| `Results` | `ObservableCollection<LogLine>` | Filtered results for DataGrid |
| `SearchText` | `string` | Search text for filtering |
| `FromDate` | `DateTime?` | Start of time range filter |
| `ToDate` | `DateTime?` | End of time range filter |
| `TotalCount` | `int` | Total results count |
| `IsLoading` | `bool` | Loading state indicator |
| `StatusText` | `string` | Status display text |
| `LevelColors` | `Dictionary<string, string>` | Level to color mapping |
| `ColumnVisibility` | `Dictionary<string, bool>` | Column visibility state |

### Event Handlers

| Handler | Purpose |
|-- -- ----|--------- |
| `SearchBox_TextChanged` | Update search filter |
| `ExportCsvButton_Click` | Export to CSV |
| `ClearFiltersButton_Click` | Clear all filters |
| `ColumnsButton_Click` | Show column visibility panel |
| `ApplyTimeFilterButton_Click` | Apply time range filter |
| `CopyJson_Click` | Copy row details as JSON |
| `ResultsGrid_LoadingRow` | Apply level-based coloring |
| `LevelColorButton_Click` | Open color picker for level |
| `ColumnVisibilityCheckBox_Checked/Unchecked` | Toggle column visibility |
| `CloseColumnPanelButton_Click` | Close column visibility panel |

---

## Integration Points

### With FindNeedleUX

1. **Navigation**: Registered in `MainWindow.xaml.cs` ResultViewerPages dictionary
2. **Configuration**: Uses `GlobalSettings.DefaultResultViewer`
3. **Data Source**: `MiddleLayerService.GetLogLines()`
4. **Window Handle**: `WindowUtil.GetMainWindow()` for WinRT interop

### With Other Components

- **RuleDSL**: Uses filtered results from RuleDSL pipeline
- **Storage**: Reads from `CachedStorage` via `MiddleLayerService`
- **Services**: Uses `WindowUtil` for window handle, `WinRT` for file picker

---

## Build Requirements

### Dependencies

| Dependency | Purpose | Version |
|-- -- ----|---------|-- -------|
| CommunityToolkit.WinUI.UI.Controls.DataGrid | DataGrid control | 7.1.2 |
| Microsoft.UI.Xaml.Controls | WinUI controls | 2.8.0 |
| Windows App SDK | WinUI 3 framework | 1.7.250310001 |

### Build Command

```bash
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" `
  "FindNeedleUX\FindNeedleUX.csproj" `
  -t:Build `
  -p:Configuration=Debug `
  -p:Platform=x64 `
  -v:minimal
```

---

## Deployment

### Output Files

```
FindNeedleUX/bin/x64/Debug/net8.0-windows10.0.19041.0/win-x64/
├── FindNeedleUX.exe          # Main executable (includes native viewer)
├── FindNeedleUX.deps.json    # Dependencies
├── FindNeedleUX.runtimeconfig.json
└── ...                       # Other assets
```

### Installation

1. Build the project
2. Copy `FindNeedleUX.exe` to target machine
3. Run executable (no additional dependencies required)

---

**Document Status**: Complete  
**Next Review**: June 1, 2026
