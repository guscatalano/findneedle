# FindNeedleUX

FindNeedleUX is the main user experience (UI) project for the FindNeedle application. It provides the graphical interface for configuring, running, and viewing results from FindNeedle's powerful search and plugin system.

## Configuration Settings

FindNeedleUX reads its configuration from `PluginConfig.json` (typically found in the `findneedle` directory). Below are the key settings you can use to customize your experience:

### Plugin Loading
- **entries**: List of plugin objects to load. Each entry has:
  - `name`: The display name of the plugin.
  - `path`: The DLL file path or name for the plugin.
  - `enabled`: (optional) Boolean to enable/disable the plugin.

- **UserRegistryPluginKey**: (string) A registry key path (in HKCU) that, if enabled, is read for additional plugins to load. The value at this key should be a semicolon-separated list of plugin DLL paths or names.
- **UserRegistryPluginKeyEnabled**: (bool) If true, enables reading additional plugins from the registry key specified by `UserRegistryPluginKey`.

### PlantUML Integration
- **PlantUMLPath**: (string) Path to the PlantUML JAR file, or a registry key reference. This can be:
  - A direct file path, e.g. `C:\Users\crimson\Desktop\plantuml-mit-1.2025.2.jar`
  - A registry key reference, e.g. `reg:HKEY_CURRENT_USER\Software\FindNeedle\PlantUMLPath` (the value at this key will be used as the path)

### Other Settings
- **PathToFakeLoadPlugin**: (string) Path to the FakeLoadPlugin executable used for plugin loading.
- **SearchQueryClass**: (string) The class name to use for search queries (e.g. `NuSearchQuery`).

## Example PluginConfig.json
```json
{
  "entries": [
    { "name": "BasicFilters", "path": "BasicFiltersPlugin.dll", "enabled": true },
    { "name": "BasicOutputs", "path": "BasicOutputsPlugin.dll" },
    { "name": "EventLogPlugin", "path": "EventLogPlugin.dll" },
    { "name": "ETWPlugin", "path": "ETWPlugin.dll", "enabled": true },
    { "name": "SessionManagementProcessor", "path": "SessionManagementProcessor.dll", "enabled": true },
    { "name": "KustoPlugin", "path": "KustoPlugin.dll", "enabled": true }
  ],
  "PathToFakeLoadPlugin": "FakeLoadPlugin.exe",
  "SearchQueryClass": "NuSearchQuery",
  "PlantUMLPath": "C:\\Users\\crimson\\Desktop\\plantuml-mit-1.2025.2.jar",
  "UserRegistryPluginKey": "Software\\FindNeedle\\Plugins",
  "UserRegistryPluginKeyEnabled": true
}
```

## Notes
- If `UserRegistryPluginKeyEnabled` is true, the application will read the registry key specified by `UserRegistryPluginKey` in the current user hive (HKCU) for additional plugins to load.
- If `PlantUMLPath` starts with `reg:`, the application will read the PlantUML path from the specified registry key.

## UI Features
- Plugin management and configuration
- Search result viewing and export — two viewers (native WinUI DataGrid + web DataTables)
- System information and diagnostics
- PlantUML integration for diagram generation

## Result Viewer

Two viewers backed by a shared `IPagedLogSource` abstraction so neither ever holds the whole
result set in memory.

- **Native viewer** (`Pages/NativeResultsPage`) — WinUI 3 `DataGrid`, kept alive across
  navigations via `NavigationCacheMode.Required`, pre-warmed at app start. Used for streaming
  searches (rows tick in as the search produces them, "Stop" button visible while loading).
- **Web viewer** (`Pages/ResultsWebPage`) — WebView2 + DataTables.js. **Hybrid mode**: under
  the threshold in *Settings → Results viewer* (default 10,000 rows) it loads client-side
  with the full result set; over the threshold it flips to `serverSide: true` DataTables and
  asks the host for one page per ajax call.

### Settings → Results viewer
Persisted at `%LocalAppData%\FindNeedle\viewer-settings.json`. Covers:
- Time format
- Color theme + per-level row colors (applied to both viewers; live-updates the web viewer)
- Default columns + default page size
- Default viewer to open (Native / Web)
- Storage backend (Auto / InMemory / Hybrid / SqlLite)
- Web viewer paging cutover (client-side ↔ server-side threshold)

### Search storage (Auto)
- `< 10,000 rows` → InMemoryStorage
- `10k – 50k rows` → HybridStorage (RAM hot, settles to SQLite at end of search)
- `> 50,000 rows` → SqliteStorage directly (rows stream to disk during scan — no settle phase)

### Diagnostics
- Append-only timing log at `%LocalAppData%\FindNeedle\perf-log.txt`. Records every search
  phase, viewer-open phase, and per-page web viewer query with `elapsed_ms`. Useful when a
  viewer feels slow — read the file and check which phase took time.

---
For more information, see the main FindNeedle documentation or visit the [GitHub repository](https://github.com/guscatalano/findneedle).
