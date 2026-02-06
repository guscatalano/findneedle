# RuleDSL UX Integration - Proposal

## Architecture Overview

### Current Menu Structure
```
MainWindow (contentFrame)
?? WelcomePage
?? SearchLocationsPage      (File locations)
?? SearchFiltersPage         (Plugin-based filters)
?? SearchProcessorsPage      (Result processors)
?? PluginsPage               (Plugin management)
?? RunSearchPage              (Execute search)
?? SearchStatisticsPage       (Results stats)
?? ResultsWebPage / ResultsVCommunityPage (Visualize)
?? ...
```

## Proposed Addition: SearchRulesPage

### Location in Menu
**Add after SearchFiltersPage, before SearchProcessorsPage**

```
Search Configuration
?? SearchLocationsPage       (Where to search)
?? SearchFiltersPage         (Traditional plugin filters)
?? SearchRulesPage           (NEW!) ? RuleDSL configuration
?   ?? Rule file browser
?   ?? Available rules list
?   ?? Rule preview/editor
?   ?? Enable/disable rules
?? SearchProcessorsPage      (What to do with results)
?? PluginsPage               (Plugin management)
```

## UI Design Mockup

### SearchRulesPage Layout

```
???????????????????????????????????????????????????????????????
? RULE CONFIGURATION                                          ?
???????????????????????????????????????????????????????????????
?                                                             ?
?  ?? Rules Files ?????????????????????????????????????????? ?
?  ?                                                       ? ?
?  ?  [Browse...] [Add Rule File]  [Remove Selected]      ? ?
?  ?                                                       ? ?
?  ?  ? filters.rules.json                               ? ?
?  ?  ? enrichment.rules.json                            ? ?
?  ?  ? uml.rules.json                                   ? ?
?  ?                                                       ? ?
?  ????????????????????????????????????????????????????????? ?
?                                                             ?
?  ?? Available Rule Sections ??????????????????????????????? ?
?  ?                                                       ? ?
?  ?  Purpose: [All ?]                                    ? ?
?  ?                                                       ? ?
?  ?  ? NoiseFilter               [filters.rules.json]   ? ?
?  ?    Filter out debug logs                             ? ?
?  ?                                                       ? ?
?  ?  ? CriticalEventClassifier  [filters.rules.json]   ? ?
?  ?    Tag critical errors                               ? ?
?  ?                                                       ? ?
?  ?  ? SecurityEventEnrichment  [enrichment.rules.json]? ?
?  ?    Classify security events                         ? ?
?  ?                                                       ? ?
?  ?  ? CrashSequenceFlow        [uml.rules.json]       ? ?
?  ?    UML visualization                                ? ?
?  ?                                                       ? ?
?  ????????????????????????????????????????????????????????? ?
?                                                             ?
?  ?? Rule Preview ?????????????????????????????????????????? ?
?  ?                                                       ? ?
?  ? [Selected: NoiseFilter]                             ? ?
?  ?                                                       ? ?
?  ? Description: Filter out debug logs                  ? ?
?  ? Purpose: filter                                     ? ?
?  ? Rules: 3 enabled                                    ? ?
?  ?                                                       ? ?
?  ? Sections:                                           ? ?
?  ?   • exclude-debug-logs                              ? ?
?  ?   • exclude-info-verbose                            ? ?
?  ?   • exclude-heartbeat                               ? ?
?  ?                                                       ? ?
?  ? [View JSON] [Edit] [Disable]                        ? ?
?  ?                                                       ? ?
?  ????????????????????????????????????????????????????????? ?
?                                                             ?
?                          [Cancel] [Apply]                  ?
???????????????????????????????????????????????????????????????
```

## Implementation Files to Create

### 1. **FindNeedleUX/Pages/SearchRulesPage.xaml** (NEW)
   - XAML UI layout with:
     - ListBox for rule files (with browse button)
     - ComboBox to filter by purpose
     - DataGrid for available rule sections
     - Preview panel for selected rules
     - Enable/Disable checkboxes

### 2. **FindNeedleUX/Pages/SearchRulesPage.xaml.cs** (NEW)
   - Code-behind with:
     - Load/save rule file list
     - Display rule sections from JSON
     - Filter by purpose
     - Enable/disable individual sections
     - Bind to `NuSearchQuery.RulesConfigPaths`

### 3. **FindNeedleUX/ViewObjects/RuleFileItem.cs** (NEW)
   - ViewModel for rule file entries:
     ```csharp
     public class RuleFileItem
     {
         public string FilePath { get; set; }
         public string FileName { get; set; }
         public bool Enabled { get; set; }
         public List<RuleSectionItem> Sections { get; set; }
     }
     ```

### 4. **FindNeedleUX/ViewObjects/RuleSectionItem.cs** (NEW)
   - ViewModel for rule section:
     ```csharp
     public class RuleSectionItem
     {
         public string Name { get; set; }
         public string Description { get; set; }
         public string Purpose { get; set; } // filter/enrichment/uml
         public bool Enabled { get; set; }
         public int RuleCount { get; set; }
         public string SourceFile { get; set; }
     }
     ```

### 5. **FindNeedleUX/Windows/Rules/RuleFileDialog.xaml** (NEW)
   - File picker dialog:
     - Filter for `*.rules.json` files
     - Show rule file preview
     - Validate JSON before adding

### 6. **Update FindNeedleUX/MainWindow.xaml.cs**
   - Add menu item for "search_rules"
   - Navigate to new SearchRulesPage

### 7. **Update FindNeedleUX/Services/MiddleLayerService.cs**
   - Add methods:
     - `LoadRulesFromQuery()` - Get current rules from NuSearchQuery
     - `SaveRulesToQuery()` - Apply rule changes
     - `DiscoverRuleFiles()` - Find available rules
     - `ValidateRuleFile()` - Check JSON validity

## Data Flow

```
SearchRulesPage
    ?
RuleFileItem[]  ? Load from NuSearchQuery.RulesConfigPaths
    ?
Bind to UI (DataGrid, ListBox)
    ? (User edits)
    ?
MiddleLayerService.SaveRulesToQuery()
    ?
NuSearchQuery.RulesConfigPaths (updated)
    ?
RunSearchPage
    ?
SearchQuery.RunThrough() ? Rules auto-loaded and applied
```

## Integration with Existing Code

### ISearchQuery Interface
```csharp
public interface ISearchQuery
{
    List<string> RulesConfigPaths { get; set; }  ? UX binds here
    object? LoadedRules { get; set; }
    // ... existing members
}
```

### NuSearchQuery (already has properties)
```csharp
public class NuSearchQuery : ISearchQuery
{
    public List<string> RulesConfigPaths { get; set; } = new();
    public object? LoadedRules { get; set; }
    // ... rest of implementation
}
```

## User Workflow

```
1. User navigates to "Search Rules" page
   ?
2. User clicks "Browse..." to select rule files
   ? File dialog opens (filters: *.rules.json)
   ?
3. User selects 1+ rule files
   ? Files added to list with checkboxes
   ?
4. User sees rule sections grouped by file
   ? Can enable/disable individual sections
   ? Can filter by purpose (filter/enrichment/uml)
   ?
5. User clicks "Apply"
   ? Rules added to NuSearchQuery.RulesConfigPaths
   ?
6. User navigates to "Run Search"
   ? Rules automatically loaded and applied during search
```

## Key Features to Include

### File Management
- [ ] Browse file system for `.rules.json` files
- [ ] Add/remove rule files
- [ ] Validate JSON before adding
- [ ] Show file path and file size
- [ ] Drag-drop to reorder (optional)

### Rule Preview
- [ ] Show rule file location
- [ ] Display rule sections with:
  - Name and description
  - Purpose (filter/enrichment/uml)
  - Number of rules in section
  - Enable/disable checkbox
- [ ] Filter sections by purpose
- [ ] Preview raw JSON (read-only)

### Status Indicators
- [ ] Rule file valid/invalid
- [ ] Total rules enabled
- [ ] Number of each purpose type
- [ ] Memory usage of loaded rules

### Integration Points
- [ ] Serialize/deserialize with workspace
- [ ] Pass rules to RunSearchPage
- [ ] Show rules in statistics after search
- [ ] Option to edit rules in external editor

## Files Structure After Implementation

```
FindNeedleUX/
?? Pages/
?  ?? SearchFiltersPage.xaml(.cs)
?  ?? SearchRulesPage.xaml(.cs)          ? NEW
?  ?? SearchProcessorsPage.xaml(.cs)
?  ?? ...
?? ViewObjects/
?  ?? FilterListItem.cs
?  ?? RuleFileItem.cs                     ? NEW
?  ?? RuleSectionItem.cs                  ? NEW
?  ?? ...
?? Windows/
?  ?? Rules/
?     ?? RuleFileDialog.xaml(.cs)         ? NEW
?? Services/
?  ?? MiddleLayerService.cs              ? MODIFY
?? MainWindow.xaml.cs                    ? MODIFY
```

## Step-by-Step Implementation Plan

1. **Create data models** (RuleFileItem, RuleSectionItem)
2. **Create SearchRulesPage XAML** with layout
3. **Implement SearchRulesPage.xaml.cs** with binding logic
4. **Create RuleFileDialog** for file browsing
5. **Update MiddleLayerService** with rule helpers
6. **Update MainWindow** to add menu item
7. **Test with example rule files**
8. **Add validation and error handling**
9. **Document in user guide**

---

## Questions for You

1. **Rule File Discovery**: Should we auto-discover rules in a `./Rules` directory on startup?
2. **Rule Editing**: Should users be able to edit rules in-app, or only browse/enable existing files?
3. **Rule Validation**: Should we show validation errors if a rule file is invalid?
4. **Rule Templates**: Should we provide a "Create New Rule" wizard?
5. **UI Complexity**: Prefer simpler list-based UI or more detailed preview panel?

---

**Ready to implement when you approve the design!**
