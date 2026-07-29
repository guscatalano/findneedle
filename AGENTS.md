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

**Symbol Resolver Plugins** (`ISymbolResolver` - KEEP):
- Consulted with a binary's PDB identity (`SymbolLookupRequest`: PdbFileName / Guid / Age / BinaryPath +
  the symbol-store `Key`) when the built-in WPP lookup can't find a PDB — a plugin locates it on an SMB
  share / symbol server / REST service and returns a PDB path. Hooked in `WppSymbolResolver.BuildTmfs`
  (the `if (!res.Found)` branch), enumerated via `PluginManager.GetAllPluginsInstancesOfAType<ISymbolResolver>()`.
  Consulted only on the build/extract path (never the offline diagnostic banner), so it may do network I/O.

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
| `SqliteStorage` | Large datasets | Persistent, disk-based, FTS5 trigram for global search |
| `HybridStorage` | 10k–50k row range | RAM-hot tier + SQLite spill, settles to disk at end of Step2 |

**Storage selection (Auto)**, picked by `NuSearchQuery.CreateStorage` based on the plugin's
`GetSearchPerformanceEstimate` row count:

| Estimated rows | Backend | Reason |
|---|---|---|
| `< 10k` | InMemoryStorage | No disk needed; fastest |
| `10k – 50k` | HybridStorage | RAM-hot, short settle phase |
| `> 50k` | SqliteStorage directly | Rows stream straight to disk during scan — no settle phase to block the viewer |

Configurable via `PluginConfig.json` (`SearchStorageType`), Settings → Results viewer, or RuleDSL
`systemConfig.storageType`. Options: `"Auto"`, `"InMemory"`, `"Hybrid"`, `"SqlLite"`.

**SQLite tuning** (`SqliteStorage`):
- `journal_mode = MEMORY`, `synchronous = OFF`, `temp_store = MEMORY`, `cache_size = 64 MB` — cache
  DB is wiped on every construction, so durability is worthless; trades fsync cost for throughput.
- **Single-row prepared INSERT, reused per row** with cached `SqliteParameter` handles (no per-row
  `AddWithValue` allocation). Counter-intuitively this benchmarks **~28× faster** than a 500-row
  multi-`VALUES` statement here — Microsoft.Data.Sqlite's per-parameter bind cost dominates a wide
  chunk (see `CoreTests/SqliteInsertBenchmark`). `InsertChunkRows` (500) is only the progress-callback
  cadence, NOT a SQL batch size.
- **`FastBulkIngest`** (static, default ON; `FINDNEEDLE_FAST_INGEST=0` or Settings → Loading
  performance to disable): (1) the `FilteredResults` secondary indexes (`Level`/`Source`/`LogTime`)
  are built once in a bulk pass at index-build time (`EnsureSecondaryIndexes`, from `BuildSearchIndex`)
  instead of being maintained per row, and (2) `Id` is a plain `INTEGER PRIMARY KEY` (no
  `AUTOINCREMENT`), skipping the per-row `sqlite_sequence` update. ~29% faster raw ingest on 1M rows
  (net "ready" gain smaller — cost relocates into the FTS build, which dominates large loads). Only
  affects a freshly built cache. Flag off = eager indexes + AUTOINCREMENT (original behaviour).
- **`UseNarrowInsertForPlainRows`** (static, default ON; `FINDNEEDLE_NARROW_INSERT=0` to disable): a row
  with no extended ETW/EventLog fields (plain-text / CSV / JSON logs) inserts via a 10-column statement
  instead of 21 — the per-row parameter-bind machinery is a large share of ingest, so this measured **~43%
  faster ingest on 1M plain-text rows**. Rows with extended fields still take the 21-column insert
  (lossless); omitted columns default to NULL and read back as "" (readers already guard with `IsDBNull`).
- **`BlankRedundantSearchableData`** (static, default ON; `FINDNEEDLE_BLANK_SEARCHABLE=0` to disable): at
  ingest, store NULL for `SearchableData` when it equals `Message` (the common case) instead of a duplicate
  copy; the reader reconstructs NULL→Message unconditionally. Measured ~12% faster ingest + ~40% smaller
  cache DB on realistic ~200-char lines (the win scales with message length). Lossless; a genuinely-empty
  `SearchableData` is stored as `""`, not NULL.
- **FTS5 trigram virtual table** (external-content) on `(Source, TaskName, Message, ResultSource,
  SearchableData, LogTime)` — substring search on million-row tables runs in milliseconds instead of
  the unindexable `LIKE '%term%'` full scan. **Built in one bulk pass after ingest** (`BuildSearchIndex`),
  ~2.8× faster than a per-row insert trigger; delete/update triggers keep it consistent on later edits.
  Sharded across N contentless FTS DBs above `FtsShardThreshold` (2M rows, ~5× on the trigram build).
  Column blanking cuts trigram volume (SearchableData==Message, single-distinct Source/ResultSource,
  LogTime unless `IndexLogTimeInFts`). Falls back to LIKE for queries < 3 chars and on FTS5 exceptions.
  Disable entirely with `FINDNEEDLE_DISABLE_FTS` (measurement). The trigram build is the dominant cost
  on large loads — the real lever for "open a huge log" speed.
- Corruption recovery: if `ClearTables()` throws `SQLITE_CORRUPT` (file left over from a
  prior crash), the file is deleted and the constructor retries once with a fresh DB.
- Cache compatibility is gated by `CacheSchemaVersion` (currently 9); a mismatch forces a rescan.

### 4. Search Pipeline
```
1. Load Rules (RuleDSL)
2. Step1 — Load locations in memory
3. Step2 — Scan locations → AddRawBatch + filter → AddFilteredBatch (per location)
            + (conditional) consolidate all rows into _currentResultList for Step3/4
            + (if Hybrid) SettleToDisk on the search thread
4. Step3 — Apply rule enrichment + run processors
5. Step4 — Apply rule output + write standard outputs
6. Step5 — Done
```

**`_currentResultList` is now conditional.** If a search has no rules, no processors, and no
outputs (the common "Quick Open just to view a log" case), Step2 *skips* the
`GetFilteredResultsInBatches` re-materialisation — on 500k rows that saves ~30 s of pure
allocation. Downstream consumers of `MiddleLayerService.SearchResults` fall back to the
storage-backed path (`GetLogLines` lazy-materialises from storage when the in-memory list is
empty; status-bar count reads `storage.GetStatistics().filteredRecordCount` via
`MiddleLayerService.GetFilteredRowCount()`).

**HybridStorage settles at end of Step2**, not at viewer-open. Before this change, the UI
thread blocked for tens of seconds the first time the viewer asked
`PagedLogSourceFactory.Create` for a source. Now the settle runs on the search task with a
visible `"moving N results into the cache…"` progress message.

### 5. Result Viewer Architecture (`IPagedLogSource`)
The viewer never materialises the full result set in memory. Both viewers read through
`IPagedLogSource` (in `FindNeedleUX/Services/PagedLogSource/`):

| Source impl | Backed by | Notes |
|---|---|---|
| `InMemoryPagedSource` | `List<LogLine>` | Caches filter+sort result by (FilterSpec, SortSpec) tuple |
| `SqlitePagedSource`   | `SqliteStorage`  | Translates FilterSpec/SortSpec → SQL; streams CSV export via 5k-row pages |

`PagedLogSourceFactory.Create(storage, fallback)` picks the impl from the storage type;
`CreateStreaming(storage)` wires a SQLite source that subscribes to
`SqliteStorage.FilteredRowsAdded` so the viewer can refresh while a search is still producing.

**Streaming search** (`MiddleLayerService.RunSearchStreaming`):
- Forces SQLite storage (only safe backend for concurrent read+write).
- Pre-constructs the storage on the UI thread, hands the viewer a paged source with
  `IsLoading = true`.
- Kicks off the search on the threadpool. The viewer opens after a 150 ms grace period and
  shows partial results that grow as `RowsAvailable` fires.
- "Stop" button cancels via the shared `CancellationTokenSource`.

**Web viewer hybrid mode** (`ResultsWebPage`):
- Below `ResultsViewerSettings.WebViewerServerSideThreshold` (default 10k): client-side
  DataTables — entire result set streamed to the page as JSON, paginates in browser memory.
- Above the threshold: `serverSide: true` DataTables with a custom `ajax` that posts a
  `getPage` message to the host. C# translates the DataTables envelope to `FilterSpec` +
  `SortSpec`, returns one page at a time via `pageResult`. No row cap.
- `searchDelay: 300` debounces keystroke filters.
- Row colors driven by the user's Settings → Results viewer theme + per-level overrides,
  pushed live via `setLevelColors` message.

**Native viewer perf** (`NativeResultsPage` + `NativeResultsPageViewModel`):
- `NavigationCacheMode = Required` — page instance kept alive across navigations; first switch
  pays the WinUI/DataGrid construction cost (~200–500 ms), subsequent switches near-instant.
- Pre-warmed at app startup behind the welcome page (`new NativeResultsPage()` on a
  low-priority dispatcher tick) so the first user-initiated switch is shorter.
- `LoadResultsAsync` skips `MiddleLayerService.GetLogLines()` for SqliteStorage/HybridStorage
  backings — only used as fallback for InMemoryStorage. AutoHide sample comes from
  `_source.GetPage(empty, none, 0, 1000)`.

### 6. Perf Diagnostics (`FindPluginCore.Diagnostics.PerfLog`)
Append-only timing log at `%LocalAppData%\FindNeedle\perf-log.txt` (rotates at 1 MB). Records
structured `key=value` events with elapsed_ms scopes. Phases emitted: `search.run`,
`search.step1..4`, `location.start/end`, `consolidate.start/end` (or `consolidate.skipped`),
`rule_filter`, `search.settle`, `viewer.native.*`, `viewer.web.load`, `viewer.web.page`. Used
to diagnose where wall-clock time goes; never breaks the calling path (all I/O wrapped in
try/catch).

### 7. Performance Benchmark + Profiler (`FindPluginCore.Diagnostics.PerfBench`)
Two related tools under Diagnostics → **Performance benchmark** in the app; see
`docs/perf-benchmark-design.md`.

- **Benchmark** (`PerfBenchRunner`): runs the search engine over a **deterministic** synthetic log
  (`SyntheticLogGenerator`, byte-identical run-to-run) and produces a `PerfBenchResult` → a
  self-contained HTML report (`PerfBenchReport`, general-audience) + a privacy-safe JSON (hardware +
  timings only, no log content) meant for community submission. Scenarios: engine (ingest / FTS-build /
  selective-vs-worst search / first-page + paging latency) and a storage-tier comparison
  (in-memory / hybrid / SQLite → load-time vs RAM vs disk). Ratios (ftsVsScan, µs/row) are the
  cross-machine numbers; ms are per-machine. `tools/perfbench/aggregate.ps1` merges submissions.
- **Profiler** (`WorkloadProfiler`): separate mode — CPU-samples the process via **in-proc EventPipe**
  (deps `Microsoft.Diagnostics.NETCore.Client` + TraceEvent, in FindPluginCore) while a workload runs
  on a dedicated thread, parses the trace, and attributes samples per-thread (keyed on a workload
  marker frame) → a "Where the time goes" hot-method list with plain-language categories
  (SQLite / ETW decode / FindNeedle / .NET runtime). Kept separate because sampling perturbs timing.
  `ProfileAction(workload, threadMarker)` profiles any code path (see `ETWPluginTests/DecodeProfileTests`
  for the ETL-decode diagnostic).

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
- None required (uses AppData for temp files). Optional tuning/diagnostic switches on `SqliteStorage`:
  - `FINDNEEDLE_FAST_INGEST=0` — disable `FastBulkIngest` (defer-indexes + no-AUTOINCREMENT), default on.
  - `FINDNEEDLE_BLANK_SEARCHABLE=0` — disable storing NULL for a SearchableData that dups Message, default on.
  - `FINDNEEDLE_NARROW_INSERT=0` — disable the 10-column insert for plain (no-extended-fields) rows, default on.
  - `FINDNEEDLE_DISABLE_FTS` — skip the FTS trigram build (measure ingest without it).
  - `FINDNEEDLE_PARALLEL_INGEST=0` — disable the fan-out streaming ingest.
  - `FINDNEEDLE_FTS_SHARD_THRESHOLD`, `FINDNEEDLE_FTS_INDEX_LOGTIME`, `FINDNEEDLE_PARALLEL_INGEST_MIN`.
  - `FINDNEEDLE_ETL` — points the large-ETL / decode-profile tests at a specific `.etl`.

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
4. `SQLITE_CORRUPT` ("database disk image is malformed") is auto-recovered: the constructor
   deletes the cache `.db` + `-wal` / `-shm` / `-journal` sidecars and retries once. If it
   throws on the retry, the file at `CachedStorage.GetCacheFilePath(searchedFilePath, ".db")`
   couldn't be created — check disk space / AV interception.
5. Review `HybridStorage.README.md` for hybrid storage issues

### Viewer Hangs / "Loading viewer…" Sluggish
1. Check `%LocalAppData%\FindNeedle\perf-log.txt` for the most recent session. The
   `search.run.end elapsed_ms` and `viewer.*.load.end elapsed_ms` lines tell you where the
   wait is.
2. A long `consolidate.end` means the search materialised the full result set — only happens
   if rules / processors / outputs are configured. With none configured you should see
   `consolidate.skipped`.
3. A long `viewer.web.load.end` or `viewer.native.load.end` with HybridStorage usually
   indicates `SettleToDisk` ran on the UI thread (it shouldn't post-fix; the settle happens
   at end of Step2).
4. WebView2 first-construction cost (~1–2 s) is unavoidable. `NativeResultsPage` is
   pre-warmed at app start; `ResultsWebPage` currently isn't (could add).

---

## Related Documentation

- `README.md` - Project overview
- `RULEDSL_QUICK_REFERENCE.md` - RuleDSL quick reference
- `RULEDSL_USAGE.md` - RuleDSL detailed usage guide
- `ARCHITECTURE_ANALYSIS_WORKSPACE_VS_RULEDSL.md` - Architecture analysis
- `DEPRECATED_PLUGINS_MIGRATION.md` - Plugin migration guide
- `HybridStorage.README.md` - Hybrid storage documentation
- `CoreTests/StorageTests.README.md` - Storage testing guide
- `FindNeedleUX/NativeResultViewer/NATIVE_VIEWER_SPEC.md` - Native viewer design
- `docs/perf-benchmark-design.md` - Performance benchmark + report design
- `docs/decode-scoping-plan.md` - Deferred plan: provider/time decode scoping (grounded in CPU profiles)

---

*Last updated: 2026-07-26 — FastBulkIngest (defer secondary indexes + no AUTOINCREMENT); corrected the
SQLite insert note (single-row-reused is ~28× faster than multi-VALUES here) and the FTS note (bulk
post-pass build, sharded); added the PerfBench benchmark + WorkloadProfiler. Prior: storage tier
recalibration (50k), FTS5 trigram search, IPagedLogSource + streaming search, PerfLog diagnostics.*
