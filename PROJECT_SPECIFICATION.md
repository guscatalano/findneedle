# FindNeedle Project Specification

**Version:** 2.0  
**Last Updated:** May 3, 2026  
**Status:** Active Development

---

## Executive Summary

**FindNeedle** is a high-performance Windows log search utility that enables users to quickly search, filter, and analyze log files from various sources including plain text files, Windows Event Logs, Event Tracing for Windows (ETW), and ZIP archives. The application features a modern WinUI 3 interface with both web-based and native result viewers, a declarative rule engine for filtering and enrichment, and a plugin-based architecture for extensibility.

---

## 1. Core Features

### 1.1 Search Capabilities
- **Multi-source search**: Search across files, folders, event logs, ETW traces, and ZIP archives
- **Text search**: Fast text search with regex support
- **Real-time filtering**: Filter results as they're loaded
- **Time range filtering**: Filter results by date/time range
- **Column-based filtering**: Filter individual columns independently
- **Global search**: Search across all result fields

### 1.2 Result Viewing
- **Dual viewer architecture**:
  - **Web Viewer** (WebView2): HTML/JS-based table with DataTables.net
  - **Native Viewer** (WinUI 3): Native DataGrid with full WinUI integration
- **Result export**: Export to CSV format
- **Row details**: View full log entry details in modal
- **Level-based color coding**: Visual distinction for different log levels
- **Column management**: Reorder, resize, and toggle column visibility

### 1.3 Configuration
- **RuleDSL**: Declarative JSON-based rule engine for filtering, enrichment, and output
- **Plugin system**: Extensible plugin architecture for data sources and processors
- **System configuration**: Configure default result viewer, PlantUML path, and other settings

### 1.4 Data Sources
- **Folder**: Recursively search folders for log files
- **File**: Search individual log files
- **Event Log**: Read Windows Event Logs (Application, System, Security, etc.)
- **ETW**: Parse Event Tracing for Windows traces (.etl files)
- **ZIP**: Search log files within ZIP archives

---

## 2. Technology Stack

### 2.1 Core Framework
- **Target Framework**: .NET 8.0 (net8.0-windows10.0.19041.0)
- **Minimum OS Version**: Windows 10 version 17763 (Windows 10 1809)
- **UI Framework**: Windows App SDK (WinUI 3)
- **Build System**: MSBuild / .NET SDK-style projects

### 2.2 Key Libraries
| Library | Purpose | Version |
|-- ------|---------|---------|
| CommunityToolkit.WinUI.UI.Controls.DataGrid | DataGrid control | 7.1.2 |
| CommunityToolkit.Mvvm | MVVM pattern | 8.2.2 |
| SQLite | Database storage | 1.9.0 |
| Microsoft.Web.WebView2 | Web viewer | 1.0.2592 |

### 2.3 Build Tools
- **Visual Studio**: 2022 Community (MSBuild 18.5)
- **.NET SDK**: 8.0.x
- **NuGet**: Package manager

---

## 3. Project Structure

```
findneedle.sln                          # Main solution file
├── findneedle/                         # Main executable (UWP app)
├── FindNeedleUX/                       # UI layer (WinUI 3)
│   ├── Pages/
│   │   ├── NativeResultViewer/         # Native result viewer
│   │   │   ├── NativeResultsPage.xaml
│   │   │   ├── NativeResultsPage.xaml.cs
│   │   │   └── NativeResultsPageViewModel.cs
│   │   ├── ResultsWebPage.xaml         # Web-based result viewer
│   │   ├── ResultsWebPage.xaml.cs
│   │   ├── SearchRulesPage.xaml        # RuleDSL configuration
│   │   └── ...                         # Other pages
│   ├── WebContent/                     # Web viewer assets
│   │   ├── resultsweb.html
│   │   ├── resultsweb.js
│   │   └── datatables.*                # DataTables.net assets
│   └── Services/                       # UI services
├── FindPluginCore/                     # Core search logic
│   ├── GlobalConfiguration/            # Global settings
│   ├── Searching/                      # Search pipeline
│   │   └── RuleDSL/                    # RuleDSL integration
│   └── Storage/                        # Result storage
├── FindNeedlePluginLib/                # Plugin interfaces
│   └── Interfaces/                     # Interface definitions
├── FindNeedleCoreUtils/                # Utility functions
│   ├── FileI/O/                        # File I/O utilities
│   └── Storage/                        # Storage utilities
├── FindNeedleRuleDSL/                  # Rule engine
│   ├── UnifiedRuleModel.cs             # Rule model definitions
│   ├── UnifiedRuleProcessor.cs         # Rule evaluation
│   └── Examples/                       # Rule examples
├── FindNeedleUmlDsl/                   # UML diagram generation
├── FindNeedleToolInstallers/           # Dependency installers
├── Plugins/                            # Plugin implementations
│   └── Kusto/                          # Kusto query plugin
├── EventLogPlugin/                     # Windows Event Log provider
├── ETWPlugin/                          # ETW provider
├── BasicTextPlugin/                    # Plain text processor
├── ZipFilePlugin/                      # ZIP archive processor
├── CoreTests/                          # Core functionality tests
├── PerformanceTests/                   # Performance benchmarks
└── FindNeedleUX.UITests/               # UI integration tests (FlaUI)
```

---

## 4. Architecture

### 4.1 Search Pipeline

```
1. User Configuration
   ↓
2. Load Rules (RuleDSL)
   ↓
3. Get Data Sources (ISearchLocation)
   ↓
4. Search (ISearchLocation.Search)
   ↓
5. Apply Rule Filtering (RuleDSL filter rules)
   ↓
6. Apply Rule Enrichment (RuleDSL enrichment rules)
   ↓
7. Process All Results to Output (RuleDSL output rules)
   ↓
8. Display Results (Web Viewer or Native Viewer)
```

### 4.2 Storage System

| Storage | Use Case | Features |
|-- ------|----- ----|----------|
| `InMemoryStorage` | Fast access, small datasets | Pure in-memory, fastest |
| `SqliteStorage` | Large datasets | Persistent, disk-based |
| `HybridStorage` | Mixed workloads | Auto-spills to disk, LRU promotion |

### 4.3 Result Viewer Architecture

**Web Viewer (ResultsWebPage)**
- WebView2 hosting HTML/JS
- DataTables.net for table functionality
- JSON messaging between C# and JS
- Theme synchronization

**Native Viewer (NativeResultsPage)**
- WinUI 3 DataGrid
- MVVM pattern with ViewModel
- Level-based row coloring
- Column visibility toggle
- Color picker for level colors

---

## 5. Configuration

### 5.1 Global Settings

**File**: `FindPluginCore/GlobalConfiguration/GlobalSettings.cs`

| Setting | Type | Default | Description |
|-- ------|----- |---------|-------------|
| `Debug` | bool | false | Enable debug mode |
| `DefaultResultViewer` | string | "resultswebpage" | Default result viewer |
| `NativeResultViewerKey` | const | "nativereviewer" | Native viewer identifier |
| `WebViewResultViewerKey` | const | "resultswebpage" | Web viewer identifier |

### 5.2 RuleDSL Configuration

**File**: `FindNeedleRuleDSL/UnifiedRuleModel.cs`

**Rule Types**:
1. **Filter Rules** (`purpose: "filter"`): Include/exclude results
2. **Enrichment Rules** (`purpose: "enrichment"`): Add tags/classifications
3. **UML Rules** (`purpose: "uml"`): Define visualization flows

**Example**:
```json
{
  "schemaVersion": "2.0",
  "title": "Error Detection",
  "sections": [
    {
      "name": "ErrorFilter",
      "purpose": "filter",
      "rules": [
        {
          "field": "level",
          "match": "ERROR|CRITICAL",
          "actions": [{ "type": "include" }]
        }
      ]
    }
  ]
}
```

### 5.3 System Configuration

**File**: `PluginConfig.json` (in `findneedle/` directory)

| Setting | Type | Description |
|-- ------|----- |-------------|
| `entries` | array | Plugin entries |
| `PathToFakeLoadPlugin` | string | FakeLoadPlugin path |
| `SearchQueryClass` | string | Search query class name |
| `PlantUMLPath` | string | PlantUML JAR path |
| `UserRegistryPluginKey` | string | Registry key for plugins |
| `UserRegistryPluginKeyEnabled` | bool | Enable registry plugin loading |

---

## 6. Interfaces

### 6.1 Data Source Plugins

**Interface**: `ISearchLocation`

| Method | Description |
|-- ----|-------------|
| `Search(ISearchQuery? searchQuery)` | Execute search |
| `LoadInMemory()` | Load all data into memory |
| `SetSearchDepth(SearchLocationDepth depth)` | Set search depth |
| `GetName()` | Get plugin name |
| `GetDescription()` | Get plugin description |

**Implementations**:
- `FolderLocation`: Search folders recursively
- `EventLogPlugin`: Read Windows Event Logs
- `ETWPlugin`: Parse ETW traces
- `ZipFilePlugin`: Search ZIP archives

### 6.2 File Processors

**Interface**: `IFileExtensionProcessor`

| Method | Description |
|-- ----|-------------|
| `ProcessFile(string filePath)` | Process a file |
| `GetSupportedExtensions()` | Get supported extensions |
| `GetName()` | Get processor name |

**Implementations**:
- `PlainTextProcessor`: Process plain text files

### 6.3 Result Storage

**Interface**: `ISearchStorage`

| Method | Description |
|-- ----|-------------|
| `AddResult(ISearchResult result)` | Add a result |
| `GetResults()` | Get all results |
| `Clear()` | Clear all results |
| `GetCount()` | Get result count |

**Implementations**:
- `InMemoryStorage`: In-memory storage
- `SqliteStorage`: SQLite database
- `HybridStorage`: Auto-spilling storage

---

## 7. Testing

### 7.1 Test Projects

| Project | Purpose | Tests |
|-- ------|---------|-------|
| `CoreTests` | Core functionality | Storage, search, utilities |
| `PerformanceTests` | Performance benchmarks | Adaptive stress tests |
| `FindNeedleUX.UITests` | UI integration | FlaUI tests |
| `FindNeedleRuleDSLTests` | Rule DSL | Parsing, evaluation |
| `FindNeedleUmlDslTests` | UML generation | Diagram generation |

### 7.2 Test Categories

| Category | Description |
|-- ------|-------------|
| `Storage` | Storage implementation tests |
| `Performance` | Performance benchmarks |
| `UITests` | UI integration tests (FlaUI) |
| `Installation` | Installer functionality |

### 7.3 Running Tests

```bash
# All tests
dotnet test

# Specific project
dotnet test CoreTests/CoreTests.csproj

# Performance tests only
dotnet test PerformanceTests/PerformanceTests.csproj --filter "TestCategory=Performance"

# Filter by category
dotnet test --filter "TestCategory=Storage"
```

---

## 8. Build & Deployment

### 8.1 Build Commands

```bash
# Build solution
dotnet build findneedle.sln

# Build with MSBuild
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" `
  "FindNeedleUX\FindNeedleUX.csproj" `
  -t:Build `
  -p:Configuration=Debug `
  -p:Platform=x64 `
  -v:minimal

# Run application
dotnet run --project findneedle/findneedle.csproj
```

### 8.2 Output Structure

```
FindNeedleUX/bin/x64/Debug/net8.0-windows10.0.19041.0/win-x64/
├── FindNeedleUX.exe          # Main executable
├── FindNeedleUX.deps.json    # Dependencies
├── FindNeedleUX.runtimeconfig.json
└── ...                       # Other assets
```

---

## 9. Future Enhancements

### 9.1 Planned Features
- [ ] Advanced filtering UI
- [ ] Search history
- [ ] Preset search templates
- [ ] Real-time search preview
- [ ] Batch operations on results
- [ ] Custom column templates
- [ ] Pivot table view
- [ ] Chart visualization

### 9.2 Technical Improvements
- [ ] State persistence (column widths, visible columns)
- [ ] Background incremental loading
- [ ] Memory profiling and optimization
- [ ] Unit tests for all components
- [ ] Integration tests for UI

---

## 10. Version History

| Version | Date | Changes |
|-- ------|------|---------|
| 2.0 | May 3, 2026 | Added native result viewer, updated spec |
| 1.0 | - | Initial release with web viewer |

---

## 11. Support & Documentation

- **Main Documentation**: `README.md`
- **Agent Guide**: `AGENTS.md`
- **RuleDSL Guide**: `FindNeedleRuleDSL/README.md`
- **Architecture Analysis**: `ARCHITECTURE_ANALYSIS_WORKSPACE_VS_RULEDSL.md`
- **Repository**: https://github.com/guscatalano/findneedle

---

**Document Status**: Complete  
**Next Review**: June 1, 2026
