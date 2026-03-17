# RuleDSL System Configuration - Single Source of Truth

## Overview

The RuleDSL now supports **complete system and plugin configuration**, making it possible to use a single `.rules.json` file as the **one source of truth** for your entire FindNeedle search configuration.

## Problem Solved

**Before:** Three overlapping configuration systems
- `PluginConfig.json` - Plugin loading and system settings
- `.workspace` files - Search locations, filters, depth
- `.rules.json` - Filter/enrichment/output rules

**After:** One unified configuration system
- `.rules.json` - Everything in one place (plugins, search settings, rules, outputs)

## Configuration Modes

### Mode 1: Complete Standalone Configuration (`useGlobalPluginConfig: false`)

Define **everything** in your rules file. No dependency on global `PluginConfig.json`.

```json
{
  "schemaVersion": "2.0",
  "version": "1.0",
  "title": "Production Error Analysis",
  
  "systemConfig": {
    "useGlobalPluginConfig": false,
    
    "plugins": {
      "searchQueryClass": "NuSearchQuery",
      "entries": [
        { "name": "BasicText", "path": "BasicTextPlugin.dll", "enabled": true },
        { "name": "ETW", "path": "ETWPlugin.dll", "enabled": true }
      ]
    },
    
    "search": {
      "name": "Error Analysis",
      "storageType": "Auto",
      "defaultDepth": "Intermediate"
    },
    
    "tools": {
      "plantUmlPath": "",
      "mermaidCliPath": ""
    }
  },
  
  "sections": [ /* your filter/enrichment/output rules */ ]
}
```

**Use when:**
- You want a completely self-contained configuration
- Different searches need different plugin sets
- You're distributing a search configuration to others

---

### Mode 2: Hybrid Configuration (`useGlobalPluginConfig: true`)

Inherit from global `PluginConfig.json` but override specific settings.

```json
{
  "schemaVersion": "2.0",
  "version": "1.0",
  "title": "Quick Error Scan",
  
  "systemConfig": {
    "useGlobalPluginConfig": true,
    
    "search": {
      "name": "Quick Scan",
      "storageType": "InMemory",
      "defaultDepth": "Shallow"
    }
  },
  
  "sections": [ /* your rules */ ]
}
```

**Use when:**
- You have standard plugins loaded globally
- You only need to override search behavior
- You want to keep plugin configuration centralized

---

### Mode 3: Rules-Only (Backward Compatible)

No `systemConfig` section - uses global `PluginConfig.json` entirely.

```json
{
  "schemaVersion": "2.0",
  "version": "1.0",
  "title": "Error Filter",
  
  "sections": [
    {
      "name": "ErrorFilter",
      "purpose": "filter",
      "rules": [
        { "match": "ERROR", "action": { "type": "include" } }
      ]
    }
  ]
}
```

**Use when:**
- You only care about rules, not system config
- Existing rule files (backward compatible)
- Simple filtering/enrichment scenarios

---

## SystemConfig Structure

### Top-Level Properties

```json
{
  "systemConfig": {
    "useGlobalPluginConfig": true,  // true = merge with global, false = standalone
    "plugins": { /* PluginConfiguration */ },
    "search": { /* SearchConfiguration */ },
    "tools": { /* ToolConfiguration */ }
  }
}
```

### PluginConfiguration

```json
{
  "plugins": {
    "searchQueryClass": "NuSearchQuery",           // Which ISearchQuery to use
    "fakeLoadPluginPath": "FakeLoadPlugin.exe",    // Path to fake load plugin
    "userRegistryPluginKey": "Software\\FindNeedle\\Plugins",
    "userRegistryPluginKeyEnabled": false,
    "entries": [
      {
        "name": "BasicText",
        "path": "BasicTextPlugin.dll",
        "enabled": true,
        "disabledReason": ""
      },
      {
        "name": "ETW",
        "path": "ETWPlugin.dll",
        "enabled": false,
        "disabledReason": "Not needed for this search"
      }
    ]
  }
}
```

**Plugin Entry Properties:**
- `name` - Display name for the plugin
- `path` - Relative or absolute path to DLL
- `enabled` - Load this plugin (true) or skip it (false)
- `disabledReason` - Optional explanation for why disabled

---

### SearchConfiguration

```json
{
  "search": {
    "name": "My Search",                  // Search query name
    "storageType": "Auto",                // "InMemory", "SqlLite", or "Auto"
    "useSynchronousSearch": false,        // Blocking (true) or async (false)
    "defaultDepth": "Intermediate"        // "Shallow", "Intermediate", or "Deep"
  }
}
```

**Storage Types:**
- `InMemory` - Fast, in-memory storage (good for <10K results)
- `SqlLite` - Disk-based storage (good for large result sets)
- `Auto` - Automatically choose based on result size estimate

**Search Depths:**
- `Shallow` - Quick scan, minimal processing
- `Intermediate` - Balanced (default)
- `Deep` - Full analysis, all metadata

---

### ToolConfiguration

```json
{
  "tools": {
    "plantUmlPath": "C:\\Tools\\plantuml.jar",
    "mermaidCliPath": "C:\\Tools\\mmdc.exe"
  }
}
```

Paths to external tools for UML diagram generation.

---

## Configuration Merging

When `useGlobalPluginConfig: true`, configurations merge as follows:

### Merge Priority (Last Wins)
1. Global `PluginConfig.json` (base)
2. Rule file `systemConfig` (overrides)

### Merge Behavior by Section

**Plugins:**
- Plugin entries with same `name` → rule file wins
- New plugin entries → added to list
- Other settings (searchQueryClass, etc.) → rule file overrides global

**Search:**
- All non-null values from rule file override global

**Tools:**
- All non-null paths from rule file override global

### Example Merge

**Global PluginConfig.json:**
```json
{
  "SearchQueryClass": "SearchQuery",
  "SearchStorageType": "InMemory",
  "entries": [
    { "name": "BasicText", "path": "BasicTextPlugin.dll", "enabled": true },
    { "name": "ETW", "path": "ETWPlugin.dll", "enabled": true }
  ]
}
```

**Rule File:**
```json
{
  "systemConfig": {
    "useGlobalPluginConfig": true,
    "plugins": {
      "searchQueryClass": "NuSearchQuery",
      "entries": [
        { "name": "EventLog", "path": "EventLogPlugin.dll", "enabled": true }
      ]
    },
    "search": {
      "storageType": "Auto"
    }
  }
}
```

**Merged Result:**
- SearchQueryClass = `"NuSearchQuery"` (rule overrides)
- StorageType = `"Auto"` (rule overrides)
- Plugins loaded:
  - BasicText (from global)
  - ETW (from global)
  - EventLog (from rule file)

---

## Migration from PluginConfig.json

### Option 1: Keep Global Config (Recommended for Most Users)

1. Keep existing `PluginConfig.json` unchanged
2. Add `systemConfig` to rule files only when you need overrides
3. Set `useGlobalPluginConfig: true` (or omit - it's the default)

**No breaking changes** - everything works as before.

---

### Option 2: Consolidate into Rules File (Advanced)

1. Copy settings from `PluginConfig.json` into rule file's `systemConfig`
2. Set `useGlobalPluginConfig: false`
3. Delete or ignore `PluginConfig.json`

**Benefits:**
- Single file contains entire configuration
- Easy to version control and distribute
- Self-documenting

**Drawbacks:**
- Larger rule files
- Duplicate config if you have multiple rule files

---

## Command-Line Usage

### Using Complete Config Rule File

```bash
findneedle --rules=complete-config.rules.json --location="C:\Logs"
```

The rule file contains plugin config, so no need for separate PluginConfig.json.

### Using Hybrid Config

```bash
findneedle --rules=hybrid-config.rules.json --location="C:\Logs"
```

Merges with global PluginConfig.json automatically.

### Multiple Rule Files (Advanced)

```bash
findneedle --rules=plugins.rules.json --rules=filters.rules.json --location="C:\Logs"
```

**Merge behavior:**
- SystemConfig from both files merges (last wins)
- Sections from both files concatenate
- Allows composition of configurations

---

## Examples

See `FindNeedleRuleDSL\Examples\`:
- `complete-config.rules.json` - Standalone config (no global dependency)
- `hybrid-config.rules.json` - Merges with global config
- `minimal-rules-only.rules.json` - No system config (backward compatible)

---

## Best Practices

### ✅ DO

1. **Use `useGlobalPluginConfig: true` by default**
   - Keeps plugin configuration centralized
   - Only override when needed

2. **Put search-specific settings in rule files**
   - Storage type, depth, search name
   - Filters, enrichment, outputs

3. **Put global settings in PluginConfig.json**
   - Plugin DLL paths
   - Registry keys
   - Tool paths

4. **Version control your rule files**
   - Self-documenting configurations
   - Easy to share and reproduce

### ❌ DON'T

1. **Don't duplicate plugin lists across many rule files**
   - Use global config for common plugins
   - Only override when truly needed

2. **Don't use `useGlobalPluginConfig: false` unless necessary**
   - Creates dependency on rule file for everything
   - Harder to maintain

3. **Don't mix configuration methods**
   - Pick one: global config + rule overrides OR standalone rule files
   - Don't confuse users with both

---

## Schema Version

**Current:** `2.0`

Version `2.0` introduces `systemConfig`. Files without `systemConfig` are treated as version `1.0` (backward compatible).

---

## Backward Compatibility

✅ All existing rule files continue to work without modification
✅ Existing `PluginConfig.json` files continue to work
✅ No breaking changes to existing workflows

New features are **opt-in**.

---

## Future Enhancements (Roadmap)

- [ ] Input locations in RuleDSL (replace `.workspace` files entirely)
- [ ] Environment variable substitution (`${ENV_VAR}`)
- [ ] Include/import other rule files
- [ ] Schema validation
- [ ] UI wizard for generating system config

---

## Summary

**One Source of Truth:**
```
Old: PluginConfig.json + .workspace + .rules.json
New: .rules.json (with optional systemConfig)
```

**Three Modes:**
1. **Standalone** (`useGlobalPluginConfig: false`) - Everything in rules file
2. **Hybrid** (`useGlobalPluginConfig: true`) - Merge with global config
3. **Rules-Only** (no systemConfig) - Use global config entirely

**Result:** Simpler, more maintainable, easier to version control and share.
