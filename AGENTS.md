# FindNeedle Agent Guide

This document provides guidance for AI coding agents (GitHub Copilot, etc.) working with the FindNeedle codebase.

## Project Overview

**FindNeedle** is a Windows log search utility with a plugin-based architecture. It enables fast text search across large log files through a UWP-based UI and command-line interface.

### Key Technologies
- **UI Framework**: Windows App SDK (WinUI 3) - UWP-style UI
- **Core Language**: C# 12 (.NET 8.0)
- **Storage**: In-memory, SQLite, and Hybrid (InMemory + SQLite with LRU spilling)
- **Build System**: MSBuild / .NET SDK-style projects
- **Target Platform**: Windows 10/11 (min version: 10.0.17763.0)

### Project Structure
```
findneedle.sln                    # Main solution file
findneedle/                       # Main executable (UWP app)
FindNeedleUX/                     # UI layer (WinUI 3)
FindPluginCore/                   # Core search logic & plugin system
FindNeedlePluginLib/              # Plugin interfaces & base types
FindNeedleCoreUtils/              # Utility functions (File I/O, storage)
FindNeedleRuleDSL/                # Declarative rule engine (JSON-based)
FindNeedleUmlDsl/                 # UML diagram generation (PlantUML/Mermaid)
FindNeedleToolInstallers/         # Dependency installers (PlantUML, Mermaid)
Plugins/                          # Plugin implementations (Kusto, etc.)
BasicFiltersPlugin/               # Deprecated filter plugins
BasicOutputsPlugin/               # Deprecated output plugins
BasicTextPlugin/                  # Plain text file processor
ETWPlugin/                        # Event Tracing for Windows provider
EventLogPlugin/                   # Windows Event Log reader
ZipFilePlugin/                    # ZIP archive processor
FakeLoadPlugin/                   # Plugin loading test utility
CoreTests/                        # Core functionality tests
PerformanceTests/                 # Performance benchmarking
FindNeedleUX.UITests/             # UI integration tests
```

---

## Architecture Principles

### 1. Plugin System (Deprecated but Active)
The project uses a plugin architecture with three main interface types:

**Data Source Plugins** (`ISearchLocation` - KEEP):
- `FolderLocation`, `EventLogPlugin`, `ETWPlugin`, `ZipFilePlugin`
- Responsible for acquiring data (files, logs, archives)

**File Processor Plugins** (`IFileExtensionProcessor` - KEEP):
- `PlainTextProcessor`
- Responsible for parsing specific file formats

**Deprecated Plugin Types** (REPLACED BY RuleDSL):
- `ISearchFilter` → Replaced by RuleDSL filter rules
- `IResultProcessor` → Replaced by RuleDSL enrichment rules
- `ISearchOutput` → Replaced by RuleDSL output rules

**Plugin Loading**:
- Configured via `PluginConfig.json` (in `findneedle/` directory)
- Supports registry-based plugin discovery (HKCU\Software\FindNeedle\Plugins)
- Uses `FakeLoadPlugin.exe` for dynamic plugin loading

### 2. RuleDSL System (PRIMARY CONFIGURATION)
The **RuleDSL** (Rule Domain Specific Language) is the modern configuration system that replaces deprecated plugins:

**Key Files**:
- `FindNeedleRuleDSL/UnifiedRuleModel.cs` - Rule model definitions
- `FindNeedleRuleDSL/UnifiedRuleProcessor.cs` - Rule evaluation engine
- `FindNeedleRuleDSL/OutputRuleProcessor.cs` - Output generation
- `FindPluginCore/Searching/RuleDSL/` - Rule integration in search pipeline

**Rule Types**:
1. **Filter Rules** (`purpose: "filter"`): Include/exclude results
2. **Enrichment Rules** (`purpose: "enrichment"`): Add tags/classifications
3. **UML Rules** (`purpose: "uml"`): Define visualization flows

**Example Rules File**:
```json
{
  "schemaVersion": "1.0",
  "version": "1.0",
  "title": "My Pipeline",
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
    },
    {
      "name": "TagCritical",
      "purpose": "enrichment",
      "rules": [
        {
          "field": "level",
          "match": "Critical",
          "actions": [{ "type": "tag", "value": "Critical" }]
        }
      ]
    }
  ]
}
```

**Usage**:
```csharp
var query = new SearchQuery();
query.RulesConfigPaths.Add("my-rules.rules.json");
query.Locations.Add(new FolderLocation(@"C:\Logs"));
query.RunThrough(); // Rules automatically applied
```

### 3. Storage System
Three storage implementations for search results:

| Storage | Use Case | Features |
|---------|----------|----------|
| `InMemoryStorage` | Fast access, small datasets | Pure in-memory, fastest |
| `SqliteStorage` | Large datasets | Persistent, disk-based |
| `HybridStorage` | Mixed workloads | Auto-spills to disk, LRU promotion |

**Storage Selection**:
- Configured via `PluginConfig.json` or RuleDSL `systemConfig.storageType`
- Options: `"InMemory"`, `"SqlLite"`, `"Auto"` (default)

### 4. Search Pipeline
```
1. Load Rules (RuleDSL)
2. Get Filtered Results (ISearchLocation.Search)
3. Apply Rule Filtering (RuleDSL filter rules)
4. Apply Rule Enrichment (RuleDSL enrichment rules)
5. Process All Results to Output (RuleDSL output rules)
```

---

## Key Classes & Interfaces

### Core Interfaces (`FindNeedlePluginLib/Interfaces/`)

**ISearchLocation** (Data Sources):
```csharp
List<ISearchResult> Search(ISearchQuery? searchQuery = null);
void LoadInMemory();
void SetSearchDepth(SearchLocationDepth depth);
```

**ISearchResult** (Search Results):
```csharp
DateTime GetLogTime();
string GetMachineName();
Level GetLevel();
string GetUsername();
string GetSource();
string GetMessage();
string GetSearchableData();
```

**ISearchQuery** (Search Configuration):
```csharp
List<ISearchLocation> Locations { get; }
List<ISearchFilter> Filters { get; }
List<IResultProcessor> Processors { get; }
List<ISearchOutput> Outputs { get; }
```

### Core Implementations (`FindPluginCore/`)

**SearchQuery** / **NuSearchQuery**:
- Main search query implementations
- Support RuleDSL integration via `RulesConfigPaths` property
- Execute search pipeline via `RunThrough()`

**PluginManager**:
- Singleton plugin loader
- Reads `PluginConfig.json`
- Manages plugin lifecycle

**GlobalSettings**:
- Global configuration (debug mode, default result viewer)

### UI Layer (`FindNeedleUX/`)

**MainWindow**:
- Main application window
- Navigation between pages (Search, Results, Plugins, etc.)
- Integration with `MiddleLayerService`

**Pages**:
- `SearchLocationsPage`: Configure data sources
- `SearchFiltersPage`: Configure filters (legacy)
- `SearchRulesPage`: Configure RuleDSL rules (modern)
- `RunSearchPage`: Execute search
- `ResultsWebPage`: View results in web view
- `PluginsPage`: Manage plugins

---

## Development Guidelines

### Adding a New Plugin

1. **Reference Required Projects**:
```xml
<ProjectReference Include="..\FindNeedlePluginLib\FindNeedlePluginLib.csproj" />
<ProjectReference Include="..\FindNeedleCoreUtils\FindNeedleCoreUtils.csproj" />
```

2. **Implement Interface**:
```csharp
public class MyPlugin : ISearchLocation
{
    public List<ISearchResult> Search(ISearchQuery? searchQuery = null)
    {
        // Implementation
    }
    
    public string GetName() => "MyPlugin";
    public string GetDescription() => "My plugin description";
    // ... other required methods
}
```

3. **Register in PluginConfig.json**:
```json
{
  "entries": [
    { "name": "MyPlugin", "path": "MyPlugin.dll", "enabled": true }
  ]
}
```

### Modifying RuleDSL

**RuleDSL is the primary configuration system**. When adding features:

1. Update `UnifiedRuleModel.cs` for data structures
2. Update `UnifiedRuleProcessor.cs` for rule evaluation
3. Update `RuleEvaluationEngine.cs` for rule matching logic
4. Add examples in `FindNeedleRuleDSL/Examples/`

### Storage Implementation

When implementing new storage types:

1. Implement `ISearchStorage` interface
2. Add factory method in `CachedStorage.GetStorage()`
3. Update `StorageTests.cs` to include new storage type
4. Update documentation in `HybridStorage.README.md`

---

## Testing Strategy

### Test Projects
| Project | Purpose |
|---------|---------|
| `CoreTests` | Core functionality (storage, search, utilities) |
| `PerformanceTests` | Performance benchmarks (adaptive stress tests) |
| `FindNeedleUX.UITests` | UI integration tests |
| `FindNeedleRuleDSLTests` | Rule DSL parsing/evaluation |
| `FindNeedleUmlDslTests` | UML diagram generation |
| `FindNeedleToolInstallerTests` | Dependency installer tests |

### Running Tests
```bash
# All tests
dotnet test

# Specific test project
dotnet test CoreTests/CoreTests.csproj

# Performance tests only
dotnet test PerformanceTests/PerformanceTests.csproj --filter "TestCategory=Performance"

# Filter by test category
dotnet test --filter "TestCategory=Storage"
```

### Test Categories
- `Storage`: InMemory, Sqlite, Hybrid storage tests
- `Performance`: Adaptive performance benchmarks
- `Installation`: Installer functionality tests
- `UML`: UML diagram generation tests

---

## Configuration Files

### PluginConfig.json (Legacy)
Location: `findneedle/PluginConfig.json`

```json
{
  "entries": [],
  "PathToFakeLoadPlugin": "FakeLoadPlugin.exe",
  "SearchQueryClass": "NuSearchQuery",
  "UserRegistryPluginKey": "Software\\FindNeedle\\Plugins",
  "UserRegistryPluginKeyEnabled": true,
  "PlantUMLPath": "",
  "_comment": "All plugins deprecated - use RuleDSL instead"
}
```

### RuleDSL SystemConfig (Modern)
Location: RuleDSL rules files or embedded in RuleDSL

```json
{
  "systemConfig": {
    "useGlobalPluginConfig": true,
    "plugins": { ... },
    "search": {
      "storageType": "Auto",
      "useSynchronousSearch": false,
      "defaultDepth": "Intermediate"
    },
    "tools": {
      "plantUmlPath": "C:\\path\\to\\plantuml.jar",
      "mermaidCliPath": "C:\\path\\to\\mmdc.exe"
    }
  }
}
```

---

## Common Tasks

### Add a New RuleDSL Field
1. Update `UnifiedRuleModel.cs` - add field to rule model
2. Update `UnifiedRuleProcessor.cs` - add field extraction logic
3. Update `RuleEvaluationEngine.cs` - add field matching
4. Update documentation in `RULEDSL_QUICK_REFERENCE.md`

### Modify Search Pipeline
1. Update `SearchQuery.RunThrough()` or `NuSearchQuery.RunThrough()`
2. Add RuleDSL section processing if needed
3. Update `RuleDSL_USAGE.md` with new pipeline steps

### Add New Storage Type
1. Implement `ISearchStorage` interface
2. Add to `CachedStorage.GetStorage()` factory
3. Update `StorageTests.cs` with new storage tests
4. Update `HybridStorage.README.md` documentation

---

## Important Notes

### What to Keep
- **ISearchLocation** implementations (data sources)
- **IFileExtensionProcessor** implementations (file parsers)
- **RuleDSL system** (primary configuration)
- **HybridStorage** (smart storage with LRU)

### What to Replace
- **ISearchFilter** → RuleDSL filter rules
- **IResultProcessor** → RuleDSL enrichment rules
- **ISearchOutput** → RuleDSL output rules
- Direct plugin configuration → RuleDSL SystemConfig

### Breaking Changes
- RuleDSL is **backward compatible** with PluginConfig
- RuleDSL can **override** global PluginConfig settings
- RuleDSL **extends** PluginConfig (doesn't fully replace yet)

---

## Quick Reference

### Key Commands
```bash
# Build solution
dotnet build findneedle.sln

# Run main application
dotnet run --project findneedle/findneedle.csproj

# Run tests
dotnet test

# Run with verbose logging
dotnet run --project findneedle/findneedle.csproj -- --verbose
```

### Important Paths
- `findneedle/PluginConfig.json` - Plugin configuration
- `FindNeedleRuleDSL/Examples/` - RuleDSL examples
- `CoreTests/StorageTests.cs` - Storage implementation tests
- `PerformanceTests/AdaptivePerformanceTests.cs` - Performance benchmarks

### Environment Variables
- None required (uses AppData for temp files)

---

## Troubleshooting

### Plugin Not Loading
1. Check `PluginConfig.json` path is correct
2. Verify plugin DLL exists at specified path
3. Check `FakeLoadPlugin.exe` is accessible
4. Review logs in `%APPDATA%\FindNeedlePlugin\findneedle_log.txt`

### RuleDSL Not Applied
1. Verify `RulesConfigPaths` is populated
2. Check JSON syntax in rules file
3. Ensure `purpose` field matches expected value
4. Review RuleDSL logs in application output

### Storage Issues
1. Check `storageType` in PluginConfig or RuleDSL
2. Verify SQLite DLLs are deployed (if using SqliteStorage)
3. Check temp directory has write permissions
4. Review `HybridStorage.README.md` for hybrid storage issues

---

## Related Documentation

- `README.md` - Project overview
- `RULEDSL_QUICK_REFERENCE.md` - RuleDSL quick reference
- `RULEDSL_USAGE.md` - RuleDSL detailed usage guide
- `ARCHITECTURE_ANALYSIS_WORKSPACE_VS_RULEDSL.md` - Architecture analysis
- `DEPRECATED_PLUGINS_MIGRATION.md` - Plugin migration guide
- `HybridStorage.README.md` - Hybrid storage documentation
- `CoreTests/StorageTests.README.md` - Storage testing guide

---

*Last updated: April 22, 2026*
