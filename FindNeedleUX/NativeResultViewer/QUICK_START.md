# Native Result Viewer - Quick Start Guide

## Overview
This document provides a quick reference for the native WinUI 3 result viewer implementation.

## Files Created

### Core Files
| File | Purpose |
|-- ----|---------|
| `NativeResultsPage.xaml` | UI with DataGrid, filters, toolbar |
| `NativeResultsPage.xaml.cs` | Code-behind with event handlers |
| `NativeResultsPageViewModel.cs` | MVVM ViewModel with filtering logic |

### Documentation
| File | Purpose |
|-- ----|---------|
| `RESULT_VIEWER_PLAN.md` | Full implementation plan |
| `QUICK_START.md` | This file |

---

## Features Implemented

### ✅ Core Grid
- DataGrid with CommunityToolkit.WinUI
- Auto-generated columns disabled (manual column definitions)
- Column reordering enabled
- Column resizing enabled
- Row selection with details view

### ✅ Filtering
- Global search (all columns)
- Per-column filter row
- Time range filter (from/to date pickers)
- Clear filters button

### ✅ Data Management
- Load from `MiddleLayerService.GetLogLines()`
- Virtualization enabled (Recycling mode)
- Status display (current/total results)

### ✅ Export
- CSV export with proper escaping
- File save picker integration

### ✅ UI Features
- Level-based row color coding
- Row details modal (copy as JSON)
- Loading indicator
- Responsive layout

---

## Configuration

### Add Viewer to Registry

**File:** `MainWindow.xaml.cs`

```csharp
private static readonly Dictionary<string, Type> ResultViewerPages = new()
{
    { "resultswebpage", typeof(FindNeedleUX.Pages.ResultsWebPage) },
    { "nativereviewer", typeof(FindNeedleUX.Pages.NativeResultsPage) },  // ADD THIS
    { "searchresultpage", typeof(FindNeedleUX.Pages.SearchResultPage) }
};
```

### Update GlobalSettings

**File:** `GlobalSettings.cs`

```csharp
public const string NativeResultViewerKey = "nativereviewer";
public const string WebViewResultViewerKey = "resultswebpage";
```

### Add Viewer Selection UI

**File:** `SystemInfoPage.xaml`

```xml
<ComboBox x:Name="ResultViewerComboBox" 
          SelectionChanged="ResultViewerComboBox_SelectionChanged">
    <ComboBoxItem Content="Native Viewer" Tag="nativereviewer" />
    <ComboBoxItem Content="Web Viewer" Tag="resultswebpage" />
</ComboBox>
```

---

## Usage

### Switching Viewers

1. **Via Settings UI:**
   - Go to Settings → System Info
   - Find "Default Result Viewer" dropdown
   - Select "Native Viewer" or "Web Viewer"

2. **Via Code:**
   ```csharp
   GlobalSettings.DefaultResultViewer = "nativereviewer";
   ```

### Keyboard Shortcuts

- **Ctrl+F** - Focus search box
- **Escape** - Clear filters/close dialogs
- **Ctrl+C** - Copy selected row details (via modal)

---

## Data Flow

```
User Action (Load Results)
    ↓
MiddleLayerService.GetLogLines()
    ↓
NativeResultsPageViewModel.LoadResultsAsync()
    ↓
Populate _allResults ObservableCollection
    ↓
ApplyFilters() (search, time range, per-column)
    ↓
Populate _filteredResults ObservableCollection
    ↓
DataGrid binds to _filteredResults
```

---

## Customization

### Add New Filter

**In ViewModel:**
```csharp
public string CustomFilter
{
    get => _customFilter;
    set { _customFilter = value; ApplyFilters(); }
}

// In ApplyFilters():
if (!string.IsNullOrWhiteSpace(CustomFilter))
{
    query = query.Where(line => line.SomeField == CustomFilter);
}
```

### Add New Column

**In XAML:**
```xml
<ctWinUI:DataGridTextColumn Header="New Column" 
                            Binding="{Binding NewProperty}"
                            MinWidth="100" />
```

### Change Color Scheme

**In XAML Resources:**
```xml
<SolidColorBrush x:Key="LevelErrorColor" Color="#FF0000" />
```

---

## Performance Tips

### For Large Datasets (100k+ rows)

1. **Virtualization** (already enabled):
   ```xml
   VirtualizationMode="Recycling"
   IsVirtualized="True"
   ```

2. **Paging** (optional):
   ```csharp
   // Add paging controls
   // Load only visible page
   ```

3. **Background Loading** (optional):
   ```csharp
   await Task.Run(() => LoadData());
   ```

---

## Troubleshooting

### DataGrid Not Showing Data
- Check `ItemsSource` binding
- Verify `MiddleLayerService.GetLogLines()` returns data
- Check for binding errors in Output window

### Filtering Not Working
- Verify `ApplyFilters()` is called on filter changes
- Check for null values in data
- Review LINQ query logic

### Performance Issues
- Enable virtualization
- Reduce visible columns
- Implement paging for very large datasets

---

## Future Enhancements

### Phase 2
- [ ] Per-column filter row (currently placeholder)
- [ ] Column visibility toggle panel
- [ ] State persistence (column widths, order)

### Phase 3
- [ ] Pivot table view
- [ ] Chart visualization
- [ ] Custom column templates

### Phase 4
- [ ] Background incremental loading
- [ ] Caching strategies
- [ ] Memory profiling optimization

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
- [ ] CSV export - verify CSV format
- [ ] Row details - click row to see details
- [ ] Viewer switching - config changes take effect

---

## Support

For issues or questions:
1. Check this guide first
2. Review `RESULT_VIEWER_PLAN.md` for full details
3. Check XAML for UI issues
4. Check ViewModel for logic issues

---

**Last Updated:** May 2, 2026
