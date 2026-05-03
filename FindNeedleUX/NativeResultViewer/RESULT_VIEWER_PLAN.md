# Result Viewer Enhancement Plan

## Overview
Add a native WinUI 3 result viewer alongside the existing WebView2-based viewer, with configuration to switch between them.

---

## Current State

### Existing Viewers
1. **ResultsWebPage** (`resultswebpage`)
   - WebView2-based HTML/JS table (DataTables.net)
   - Features: Search, filtering, column reordering, CSV export, time range, color coding
   - Default viewer

2. **SearchResultPage** (`searchresultpage`)
   - CommunityToolkit DataGrid
   - Basic filtering, grouping, sorting
   - Not actively used

### Configuration
- **GlobalSettings.DefaultResultViewer** - String property (default: `"resultswebpage"`)
- **MainWindow.xaml.cs** - `ResultViewerPages` dictionary maps keys to page types
- **SystemInfoPage** - UI to change viewer via ComboBox

---

## Proposed Architecture

### 1. New Native Viewer: `NativeResultsPage`

**Location:** `FindNeedleUX/Pages/NativeResultsPage.xaml`

**Features to Implement:**

| Feature | Implementation | Priority |
|---------|---------------|----------|
| **Data Grid** | `Microsoft.UI.Xaml.Controls.DataGrid` | Core |
| **Search** | `TextBox` + LINQ filtering | Core |
| **Per-Column Filter** | Filter row in DataGrid header | Core |
| **Time Range Filter** | `DatePicker` + `TimePicker` | Core |
| **Column Reordering** | `CanUserReorderColumns="True"` | Core |
| **Column Resizing** | `CanUserResizeColumns="True"` | Core |
| **Row Color Coding** | `CellStyle` with level-based colors | Core |
| **CSV Export** | `FileSavePicker` + file I/O | Core |
| **Row Details Modal** | `ContentDialog` | Core |
| **State Persistence** | `ApplicationData.Current.LocalSettings` | Medium |
| **Keyboard Shortcuts** | `Window.ProcessKeyMessage` | Low |
| **Virtualization** | `VirtualizationMode="Recycling"` | High (for large datasets) |

---

### 2. Configuration Changes

#### A. Update `GlobalSettings.cs`
```csharp
// Add new viewer key
public const string NativeResultViewerKey = "nativereviewer";
public const string WebViewResultViewerKey = "resultswebpage";

// Update DefaultResultViewer to accept both values
```

#### B. Update `MainWindow.xaml.cs`
```csharp
private static readonly Dictionary<string, Type> ResultViewerPages = new()
{
    { "resultswebpage", typeof(FindNeedleUX.Pages.ResultsWebPage) },
    { "nativereviewer", typeof(FindNeedleUX.Pages.NativeResultsPage) },  // NEW
    { "searchresultpage", typeof(FindNeedleUX.Pages.SearchResultPage) }
};
```

#### C. Add Settings UI
- **SystemInfoPage.xaml** - Add ComboBox option for viewer selection
- **Settings page** - Dedicated settings UI (optional)

---

### 3. Data Flow

```
MiddleLayerService.GetLogLines()
    ↓
NativeResultsPageViewModel (MVVM)
    ↓
DataGrid (virtualized)
```

**ViewModel Responsibilities:**
- Load results from `MiddleLayerService`
- Apply search/filter criteria
- Manage column visibility state
- Handle export to CSV
- Persist user preferences

---

## Implementation Phases

### Phase 1: Core Grid (Week 1)
**Goal:** Basic working grid with search

**Tasks:**
1. Create `NativeResultsPage.xaml` with DataGrid
2. Bind to `MiddleLayerService.GetLogLines()`
3. Implement search box (filter all columns)
4. Add basic column definitions

**Deliverable:** Working grid displaying results

---

### Phase 2: Filtering (Week 2)
**Goal:** Full filtering capability

**Tasks:**
1. Per-column filter row
2. Time range filter (from/to date pickers)
3. Clear filters button
4. Filter state management

**Deliverable:** Complete filtering system

---

### Phase 3: Advanced Features (Week 3)
**Goal:** Polish and advanced features

**Tasks:**
1. Column reordering/resizing
2. Row color coding by level
3. CSV export functionality
4. Row details modal

**Deliverable:** Feature-complete native viewer

---

### Phase 4: Configuration & Testing (Week 4)
**Goal:** Integration and validation

**Tasks:**
1. Update `GlobalSettings` with new viewer key
2. Add viewer selection UI
3. Test with large datasets (100k+ rows)
4. Performance optimization (virtualization)
5. Compare with WebView2 viewer

**Deliverable:** Production-ready with config switch

---

## Technical Decisions

### 1. DataGrid Control
**Option A:** `Microsoft.UI.Xaml.Controls.DataGrid` (WinUI 3)
- ✅ Native WinUI 3
- ✅ Good performance
- ✅ Built-in sorting/filtering

**Option B:** `CommunityToolkit.WinUI.UI.Controls.DataGrid`
- ✅ More features
- ✅ Better column resizing
- ⚠️ Additional dependency

**Decision:** Start with WinUI 3 DataGrid, migrate to CommunityToolkit if needed

---

### 2. MVVM Pattern
**Approach:** Simple MVVM with ViewModel

```
NativeResultsPage.xaml
    ↓
NativeResultsPageViewModel.cs
    ↓
MiddleLayerService.GetLogLines()
```

**ViewModel Properties:**
- `ObservableCollection<LogLine> Results`
- `string SearchText`
- `DateTime? FromDate`
- `DateTime? ToDate`
- `Dictionary<string, bool> ColumnVisibility`

---

### 3. Virtualization Strategy
**For large datasets (100k+ rows):**

```xml
<DataGrid ItemsSource="{x:Bind ViewModel.Results, Mode=OneWay}"
          VirtualizationMode="Recycling"
          IsVirtualized="True"
          MaxItemsPerRow="100" />
```

**If virtualization isn't enough:**
- Implement paging (100 rows per page)
- Lazy loading (load chunks as user scrolls)

---

### 4. State Persistence
**What to save:**
- Column widths
- Column order
- Visible columns
- Last search text
- Last time range

**Where:** `ApplicationData.Current.LocalSettings`

```csharp
var settings = ApplicationData.Current.LocalSettings;
settings.Values["NativeViewer.ColumnWidths"] = columnWidthsJson;
```

---

## Migration Path

### Step 1: Create Native Viewer
```
FindNeedleUX/Pages/NativeResultsPage.xaml
FindNeedleUX/Pages/NativeResultsPage.xaml.cs
FindNeedleUX/ViewModels/NativeResultsPageViewModel.cs
```

### Step 2: Register Viewer
Update `MainWindow.xaml.cs` to include new viewer in dictionary

### Step 3: Add Configuration UI
Update `SystemInfoPage.xaml` with viewer selection ComboBox

### Step 4: Deprecate Old Viewer (Optional)
- Keep both viewers for a while
- Mark `ResultsWebPage` as deprecated in docs
- Remove in future major version

---

## Testing Strategy

### Unit Tests
- Filter logic (search, time range, per-column)
- CSV export formatting
- State persistence

### Integration Tests
- Load 10k rows
- Load 100k rows
- Switch between viewers
- Configuration persistence

### Performance Tests
- Cold start time
- Memory usage
- Scroll performance

---

## Success Criteria

✅ Native viewer displays all results correctly
✅ Search/filter works as well as WebView2 version
✅ Column reordering/resizing functional
✅ CSV export produces valid CSV
✅ State persists across sessions
✅ Performance comparable to WebView2 (or better)
✅ Configuration UI allows easy switching

---

## Future Enhancements

### Phase 5: Advanced Features
- [ ] Pivot table view
- [ ] Chart visualization (WinUI Chart controls)
- [ ] Custom column templates
- [ ] Bulk row operations
- [ ] Copy to clipboard (JSON/CSV)
- [ ] Print support

### Phase 6: Optimization
- [ ] Background loading
- [ ] Incremental updates
- [ ] Caching strategies
- [ ] Memory profiling and optimization

---

## Notes

- Keep the WebView2 viewer as an option (some users may prefer it)
- The configuration system is already in place, just need to add the new viewer key
- Use the existing `LogLine` class for data binding
- Leverage `MiddleLayerService` for data access

---

## Implementation Progress

### ✅ Completed
- [x] Plan document created
- [x] NativeResultsPage.xaml - Full UI with DataGrid, filters, toolbar
- [x] NativeResultsPageViewModel.cs - MVVM with filtering, export, state
- [x] NativeResultsPage.xaml.cs - Code-behind with event handlers

### 🔄 In Progress
- [ ] Update `MainWindow.xaml.cs` to register new viewer
- [ ] Update `GlobalSettings.cs` with viewer keys
- [ ] Add viewer selection UI to SystemInfoPage
- [ ] Test with actual data

### ⏳ Next Steps
- [ ] Phase 1: Test basic grid loading
- [ ] Phase 2: Implement per-column filters
- [ ] Phase 3: Add column visibility toggle
- [ ] Phase 4: Add state persistence
- [ ] Phase 5: Performance testing with large datasets

---

## Implementation Checklist

### Core Implementation
- [x] Create `NativeResultsPage.xaml` with DataGrid and UI elements
- [x] Create `NativeResultsPageViewModel.cs` with filtering logic
- [x] Create `NativeResultsPage.xaml.cs` with event handlers
- [x] Update `MainWindow.xaml.cs` to register new viewer key
- [x] Update `GlobalSettings.cs` with viewer constants
- [x] Add viewer selection to SystemInfoPage UI
- [x] Add FindPluginCore reference to FindNeedleUX.csproj
- [x] Implement level color editing (color picker for each level)
- [x] Implement column visibility toggle (show/hide columns)

### Build Status
- ✅ **Build successful** - MSBuild compiles without errors
- ✅ **Executable created** - `FindNeedleUX.exe` generated successfully

### Features Implemented
- [x] DataGrid with CommunityToolkit.WinUI
- [x] Search across all columns
- [x] Time range filter (from/to date pickers)
- [x] Per-column filter row
- [x] Column reordering (enabled)
- [x] Column resizing (enabled)
- [x] Level-based row color coding
- [x] **Level color editing** - Color picker for each level
- [x] **Column visibility toggle** - Show/hide columns
- [x] CSV export functionality
- [x] Row details modal (copy as JSON)
- [x] Status display (current/total results)

### Testing
- [ ] Test with small dataset (100 rows)
- [ ] Test with medium dataset (10k rows)
- [ ] Test with large dataset (100k+ rows)
- [ ] Test column reordering
- [ ] Test column resizing
- [ ] Test filtering (search, time range, per-column)
- [ ] Test level color editing
- [ ] Test column visibility toggle
- [ ] Test CSV export
- [ ] Test row details modal
- [ ] Test viewer switching via configuration

### Polish
- [ ] Add keyboard shortcuts (Ctrl+F, Escape)
- [ ] Implement state persistence (column widths, visible columns)
- [ ] Add loading indicator for large datasets
- [ ] Optimize virtualization for large datasets
- [ ] Add unit tests for filter logic
- [ ] Add integration tests for viewer switching

---

**Status:** Implementation Complete (Build Successful)  
**Last Updated:** May 2, 2026
