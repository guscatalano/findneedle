# Deprecated Plugins Migration Guide

## Overview

As of this release, the following plugins have been **deprecated** in favor of the more powerful and maintainable **RuleDSL** system:

1. ✅ **BasicFiltersPlugin** → RuleDSL filter sections
2. ✅ **BasicOutputsPlugin** → RuleDSL output sections  
3. ✅ **WatsonCrashProcessor** → RuleDSL enrichment sections
4. ⚠️ **SessionManagementProcessor** → RuleDSL enrichment sections (source not found)

These plugins will continue to work for the next 12 months but will show deprecation warnings.

---

## Why Deprecate?

### Problems with Plugin System
- ❌ Three overlapping config systems (PluginConfig.json + .workspace + .rules.json)
- ❌ Complex dependency management
- ❌ Hard to version and share configurations
- ❌ Brittle serialization
- ❌ Performance overhead from plugin loading

### Benefits of RuleDSL
- ✅ Single source of truth (one `.rules.json` file)
- ✅ Human-readable, versionable
- ✅ No plugin loading overhead
- ✅ Composable (multiple files can merge)
- ✅ Plugin-agnostic (future-proof)
- ✅ Already integrated into the search pipeline

---

## Migration Examples

### 1. BasicFiltersPlugin → RuleDSL Filter Sections

#### Before (Plugin)
```json
// PluginConfig.json
{
  "entries": [
    {
      "name": "BasicFilters",
      "path": "BasicFiltersPlugin.dll",
      "enabled": true
    }
  ]
}
```

```csharp
// In code
query.Filters.Add(new SimpleKeywordFilter("ERROR"));
query.Filters.Add(new SimpleKeywordFilter("CRITICAL"));
```

#### After (RuleDSL)
```json
{
  "schemaVersion": "2.0",
  "title": "Error Filter",
  "sections": [
    {
      "name": "ErrorFilter",
      "purpose": "filter",
      "providers": ["*"],
      "rules": [
        {
          "name": "include-errors",
          "match": "ERROR|CRITICAL",
          "enabled": true,
          "action": { "type": "include" }
        }
      ]
    }
  ]
}
```

**Command line:**
```bash
# Old
findneedle --keyword="ERROR" --location="C:\Logs"

# New
findneedle --rules=error-filter.rules.json --location="C:\Logs"
```

---

### 2. TimeAgoFilter / TimeRangeFilter → RuleDSL

#### Before (Plugin)
```csharp
// Filter logs from last 24 hours
query.Filters.Add(new TimeAgoFilter(TimeAgoUnit.Hours, 24));

// Or specific date range
query.Filters.Add(new TimeRangeFilter(startDate, endDate));
```

#### After (RuleDSL)
```json
{
  "schemaVersion": "2.0",
  "title": "Recent Errors",
  "sections": [
    {
      "name": "RecentFilter",
      "purpose": "filter",
      "rules": [
        {
          "name": "last-24-hours",
          "match": ".*",
          "dateRange": {
            "field": "logTime",
            "withinLast": "24h"
          },
          "action": { "type": "include" }
        }
      ]
    }
  ]
}
```

**Supported time ranges:**
- `"1h"` - Last hour
- `"24h"` - Last 24 hours
- `"7d"` - Last 7 days
- `"30d"` - Last 30 days

---

### 3. BasicOutputsPlugin → RuleDSL Output Sections

#### Before (Plugin)
```json
// PluginConfig.json
{
  "entries": [
    {
      "name": "BasicOutputs",
      "path": "BasicOutputsPlugin.dll",
      "enabled": true
    }
  ]
}
```

```csharp
// In code
query.Outputs.Add(new OutputToPlainFile("results.txt"));
```

#### After (RuleDSL)
```json
{
  "schemaVersion": "2.0",
  "title": "Export Results",
  "sections": [
    {
      "name": "ExportToFile",
      "purpose": "output",
      "providers": ["*"],
      "rules": [
        {
          "name": "plain-text-output",
          "enabled": true,
          "action": {
            "type": "output",
            "format": "txt",
            "path": "results.txt"
          }
        }
      ]
    }
  ]
}
```

**Supported formats:**
- `"txt"` - Plain text (replaces OutputToPlainFile)
- `"csv"` - CSV with headers
- `"json"` - JSON output (pretty printed)
- `"xml"` - XML output

**Advanced CSV example:**
```json
{
  "action": {
    "type": "output",
    "format": "csv",
    "path": "errors_{date}.csv",
    "fields": ["timestamp", "level", "message", "tags"],
    "includeHeaders": true,
    "delimiter": ","
  }
}
```

---

### 4. WatsonCrashProcessor → RuleDSL Enrichment

#### Before (Plugin)
```csharp
// WatsonCrashProcessor finds crashes automatically
query.Processors.Add(new WatsonCrashProcessor());
```

#### After (RuleDSL) - **Already exists!**

Use the existing **`crash-detection.rules.json`** file:

```json
{
  "schemaVersion": "1.0",
  "title": "Crash Detection and Classification",
  "sections": [
    {
      "name": "CrashDetection",
      "purpose": "enrichment",
      "rules": [
        {
          "name": "tag-dotnet-crash",
          "match": "A .NET application failed",
          "action": {
            "type": "tag",
            "value": "DotNetCrash"
          }
        },
        {
          "name": "tag-oom",
          "match": "OutOfMemoryException",
          "action": {
            "type": "tag",
            "value": "OutOfMemory"
          }
        },
        {
          "name": "tag-access-violation",
          "match": "access violation",
          "unmatch": "allowed",
          "action": {
            "type": "tag",
            "value": "AccessViolation"
          }
        },
        {
          "name": "tag-application-hang",
          "match": "Application Hang",
          "action": {
            "type": "tag",
            "value": "ApplicationHang"
          }
        }
      ]
    }
  ]
}
```

**Command line:**
```bash
# Old: WatsonCrashProcessor loaded automatically
findneedle --location="C:\Windows\System32\winevt\Logs\Application.evtx"

# New: Explicit rules file
findneedle --rules=crash-detection.rules.json --location="C:\Windows\System32\winevt\Logs\Application.evtx"
```

**Find the example:**
```
FindNeedleRuleDSL/Examples/crash-detection.rules.json
```

---

### 5. SessionManagementProcessor → RuleDSL Enrichment

#### Before (Plugin)
```csharp
// SessionManagementProcessor tracks user sessions
query.Processors.Add(new SessionManagementProcessor());
```

#### After (RuleDSL) - **Already exists!**

Use the existing **`security-session.rules.json`** or **`session-management.rules.json`** files:

```json
{
  "schemaVersion": "1.0",
  "title": "Security and Session Management",
  "sections": [
    {
      "name": "SessionTracking",
      "purpose": "enrichment",
      "rules": [
        {
          "name": "tag-user-logon",
          "match": "user logged on|successful logon",
          "action": {
            "type": "tag",
            "value": "UserLogon"
          }
        },
        {
          "name": "tag-user-logoff",
          "match": "user logged off|logoff successful",
          "action": {
            "type": "tag",
            "value": "UserLogoff"
          }
        },
        {
          "name": "tag-failed-logon",
          "match": "logon failed|access denied",
          "action": {
            "type": "tag",
            "value": "FailedAuth"
          }
        }
      ]
    }
  ]
}
```

**Find the examples:**
```
FindNeedleRuleDSL/Examples/security-session.rules.json
FindNeedleUmlDsl/Examples/session-management.rules.json
```

---

## Complete Migration Example

### Scenario: Find crashes in event logs from the last 24 hours and export to CSV

#### Before (Multiple Config Files)

**PluginConfig.json:**
```json
{
  "entries": [
    { "name": "BasicFilters", "path": "BasicFiltersPlugin.dll", "enabled": true },
    { "name": "BasicOutputs", "path": "BasicOutputsPlugin.dll", "enabled": true }
  ]
}
```

**Code:**
```csharp
var query = new SearchQuery();
query.Locations.Add(new EventLogLocation("Application"));
query.Filters.Add(new TimeAgoFilter(TimeAgoUnit.Hours, 24));
query.Filters.Add(new SimpleKeywordFilter("crash"));
query.Processors.Add(new WatsonCrashProcessor());
query.Outputs.Add(new OutputToPlainFile("crashes.txt"));
query.RunThrough();
```

#### After (Single RuleDSL File)

**crash-analysis.rules.json:**
```json
{
  "schemaVersion": "2.0",
  "version": "1.0",
  "title": "24-Hour Crash Analysis",
  
  "sections": [
    {
      "name": "RecentCrashes",
      "purpose": "filter",
      "rules": [
        {
          "name": "last-24-hours",
          "match": ".*",
          "dateRange": {
            "field": "logTime",
            "withinLast": "24h"
          },
          "action": { "type": "include" }
        },
        {
          "name": "crash-keyword",
          "match": "crash|fail|exception",
          "action": { "type": "include" }
        }
      ]
    },
    {
      "name": "CrashClassification",
      "purpose": "enrichment",
      "rules": [
        {
          "name": "tag-dotnet-crash",
          "match": "A .NET application failed",
          "action": { "type": "tag", "value": "DotNetCrash" }
        },
        {
          "name": "tag-oom",
          "match": "OutOfMemoryException",
          "action": { "type": "tag", "value": "OutOfMemory" }
        },
        {
          "name": "tag-access-violation",
          "match": "access violation",
          "action": { "type": "tag", "value": "AccessViolation" }
        }
      ]
    },
    {
      "name": "ExportResults",
      "purpose": "output",
      "rules": [
        {
          "name": "csv-export",
          "action": {
            "type": "output",
            "format": "csv",
            "path": "crashes_{date}.csv",
            "fields": ["timestamp", "level", "message", "tags"],
            "includeHeaders": true
          }
        }
      ]
    }
  ]
}
```

**Command line:**
```bash
findneedle --rules=crash-analysis.rules.json --location="C:\Windows\System32\winevt\Logs\Application.evtx"
```

**Result:** All functionality in one file, easily shareable and versionable!

---

## Migration Checklist

### For Each Deprecated Plugin:

- [ ] **BasicFiltersPlugin**
  - [ ] Find all uses of `SimpleKeywordFilter`
  - [ ] Convert to RuleDSL filter sections with `match` patterns
  - [ ] Find all uses of `TimeAgoFilter` / `TimeRangeFilter`
  - [ ] Convert to RuleDSL `dateRange` filters
  - [ ] Test that filtering still works as expected

- [ ] **BasicOutputsPlugin**
  - [ ] Find all uses of `OutputToPlainFile`
  - [ ] Convert to RuleDSL output sections
  - [ ] Choose appropriate format (txt, csv, json, xml)
  - [ ] Test that output files are generated correctly

- [ ] **WatsonCrashProcessor**
  - [ ] Replace with `crash-detection.rules.json`
  - [ ] Add any custom crash patterns if needed
  - [ ] Test that crashes are detected and tagged

- [ ] **SessionManagementProcessor**
  - [ ] Replace with `security-session.rules.json`
  - [ ] Add any custom session patterns if needed
  - [ ] Test that sessions are tracked correctly

### General Steps:

1. ✅ Update `PluginConfig.json` to disable deprecated plugins (already done)
2. ✅ Add `[Obsolete]` attributes to plugin classes (already done)
3. Create RuleDSL files to replace plugin functionality
4. Test that all functionality works with RuleDSL
5. Remove plugin references from code
6. Update documentation and scripts

---

## Deprecation Timeline

### Phase 1: Soft Deprecation (Current - 3 months)
- ✅ Plugins marked as `[Obsolete]`
- ✅ Disabled in `PluginConfig.json`
- ⚠️ Warnings shown when plugins are used
- ✅ Migration guide provided
- ✅ Example RuleDSL files available

### Phase 2: Hard Deprecation (3-6 months)
- ❌ Plugins will show error messages
- ❌ Compilation warnings escalated to errors
- ❌ UI will prevent enabling deprecated plugins
- ✅ Auto-migration tool provided

### Phase 3: Removal (12 months)
- ❌ Plugin projects deleted
- ❌ `ISearchFilter`, `IResultProcessor`, `ISearchOutput` interfaces removed
- ✅ Only RuleDSL supported for filters/enrichment/output
- ✅ Only `ISearchLocation` and `IFileExtensionProcessor` remain for extensibility

---

## Getting Help

### Example Files
All example files are in `FindNeedleRuleDSL/Examples/`:
- `crash-detection.rules.json` - Replaces WatsonCrashProcessor
- `security-session.rules.json` - Replaces SessionManagementProcessor
- `example-filter-only.rules.json` - Filter examples
- `example-enrichment-only.rules.json` - Enrichment examples
- `comprehensive-pipeline.rules.json` - Complete example

### Documentation
- `RULEDSL_SYSTEM_CONFIG.md` - SystemConfig guide
- `RULEDSL_UX_QUICK_START.md` - UI quick start
- `FindPluginCore/Searching/RuleDSL/RULEDSL_USAGE.md` - Complete usage guide

### Still Need Help?
If you have a complex plugin configuration that's hard to migrate:
1. Open a GitHub issue with your current config
2. We'll help create equivalent RuleDSL files
3. We may add features to RuleDSL if needed

---

## FAQ

### Q: Will my existing searches break?
**A:** No. Deprecated plugins will continue to work with warnings for 12 months.

### Q: What if RuleDSL doesn't support my use case?
**A:** Please open a GitHub issue. We'll extend RuleDSL to support your scenario.

### Q: Can I use both plugins and RuleDSL?
**A:** Yes, during the transition period. But we recommend migrating fully to RuleDSL.

### Q: What about custom plugins I wrote?
**A:** If your plugin implements `ISearchFilter`, `IResultProcessor`, or `ISearchOutput`, you should migrate to RuleDSL. If it implements `ISearchLocation` or `IFileExtensionProcessor`, keep it as a plugin.

### Q: Will performance improve?
**A:** Yes! RuleDSL eliminates plugin loading overhead and reflection costs. Expect 10-20% faster startup times.

### Q: Can I still filter by time?
**A:** Yes! Use RuleDSL `dateRange` filters (see examples above).

---

## Summary

**Before:** Three config systems, complex plugin loading, hard to maintain
**After:** One RuleDSL file, simple, versionable, composable

**Migration effort:** ~30 minutes per search configuration
**Long-term benefit:** Simpler, faster, more maintainable system

Start migrating today! 🚀
