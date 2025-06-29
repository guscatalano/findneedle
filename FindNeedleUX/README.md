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
- Search result viewing and export
- System information and diagnostics
- PlantUML integration for diagram generation

---
For more information, see the main FindNeedle documentation or visit the [GitHub repository](https://github.com/guscatalano/findneedle).
