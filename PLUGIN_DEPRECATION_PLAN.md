# Plugin Deprecation Plan - Transition to RuleDSL

## Executive Summary

**Goal:** Deprecate plugins that are now redundant due to RuleDSL capabilities, keeping only essential data source and file processor plugins.

**Impact:** Simpler architecture, fewer dependencies, easier maintenance, single source of truth (RuleDSL).

---

## Current Plugin Landscape

### Plugin Categories

| Category | Interface | Current Count | Status |
|----------|-----------|---------------|--------|
| **Filters** | `ISearchFilter` | ~5-10 | ✅ DEPRECATE (RuleDSL replaces) |
| **Processors** | `IResultProcessor` | ~5-10 | ✅ DEPRECATE (RuleDSL replaces) |
| **Outputs** | `ISearchOutput` | ~3-5 | ✅ DEPRECATE (RuleDSL replaces) |
| **Locations** | `ISearchLocation` | 4-5 | ⚠️ KEEP (core data sources) |
| **File Processors** | `IFileExtensionProcessor` | 1-2 | ⚠️ KEEP (core parsers) |

---

## Plugins to Deprecate

### Phase 1: Immediate Deprecation (High Confidence)

#### 1.1 BasicFiltersPlugin
**Functionality:** Simple keyword filtering
**Replaced by:** RuleDSL filter sections
```json
// Old: Load BasicFiltersPlugin
// New: RuleDSL
{
  "sections": [{
    "name": "KeywordFilter",
    "purpose": "filter",
    "rules": [
      { "match": "ERROR|CRITICAL", "action": { "type": "include" } }
    ]
  }]
}
```

**Migration:** Auto-generate RuleDSL from existing filter configs

**Timeline:** Next release (1 month)

---

#### 1.2 BasicOutputsPlugin
**Functionality:** CSV, JSON, XML output generation
**Replaced by:** RuleDSL output sections
```json
// Old: Load BasicOutputsPlugin
// New: RuleDSL
{
  "sections": [{
    "name": "ExportCSV",
    "purpose": "output",
    "rules": [{
      "action": {
        "type": "output",
        "format": "csv",
        "path": "results.csv"
      }
    }]
  }]
}
```

**Migration:** Auto-generate RuleDSL from output configs

**Timeline:** Next release (1 month)

---

#### 1.3 SessionManagementProcessor
**Functionality:** Session tracking, user activity correlation
**Replaced by:** RuleDSL enrichment sections
```json
// Old: Load SessionManagementProcessor
// New: RuleDSL
{
  "sections": [{
    "name": "SessionTracking",
    "purpose": "enrichment",
    "rules": [
      { "match": "logged on", "action": { "type": "tag", "value": "SessionStart" } },
      { "match": "logged off", "action": { "type": "tag", "value": "SessionEnd" } }
    ]
  }]
}
```

**Migration:** Auto-generate RuleDSL from processor logic

**Timeline:** 2-3 months (more complex)

---

#### 1.4 WatsonCrashProcessor
**Functionality:** Crash detection and classification
**Replaced by:** RuleDSL enrichment sections
```json
// Old: Load WatsonCrashProcessor
// New: RuleDSL (see crash-detection.rules.json)
{
  "sections": [{
    "name": "CrashDetection",
    "purpose": "enrichment",
    "rules": [
      { "match": "A .NET application failed", "action": { "type": "tag", "value": "DotNetCrash" } },
      { "match": "OutOfMemoryException", "action": { "type": "tag", "value": "OOM" } }
    ]
  }]
}
```

**Migration:** Already have `crash-detection.rules.json` example!

**Timeline:** Next release (1 month)

---

### Phase 2: Gradual Deprecation (Lower Priority)

#### Any Custom ISearchFilter Implementations
**Approach:** Provide migration tool to convert to RuleDSL
**Timeline:** 3-6 months

#### Any Custom IResultProcessor Implementations  
**Approach:** Provide migration tool to convert to RuleDSL
**Timeline:** 3-6 months

#### Any Custom ISearchOutput Implementations
**Approach:** Provide migration tool to convert to RuleDSL
**Timeline:** 3-6 months

---

## Plugins to Keep (Core Functionality)

### Data Source Plugins (ISearchLocation)

These are **not** replaceable by RuleDSL - they provide data access.

#### 2.1 EventLogPlugin
**Functionality:** Windows Event Log access
**Status:** ⚠️ KEEP but consider moving to core
**Reason:** Core data source, not duplicated by RuleDSL

**Recommendation:** Move to `FindPluginCore.Implementations.Locations.EventLogLocation`
- Makes it non-optional
- Always available
- No plugin loading overhead

---

#### 2.2 ETWPlugin
**Functionality:** ETW trace file (.etl) parsing
**Status:** ⚠️ KEEP but consider moving to core
**Reason:** Core data source for Windows diagnostics

**Recommendation:** Move to `FindPluginCore.Implementations.Locations.ETWLocation`

---

#### 2.3 KustoPlugin
**Functionality:** Azure Data Explorer (Kusto) queries
**Status:** ⚠️ KEEP as plugin
**Reason:** Optional feature, requires Azure dependencies

**Recommendation:** Keep as plugin (cloud integration is optional)

---

#### 2.4 ZipFilePlugin
**Functionality:** Read files from ZIP archives
**Status:** ⚠️ KEEP but consider moving to core
**Reason:** Common scenario (compressed logs)

**Recommendation:** Move to `FindPluginCore.Implementations.Locations.ZipLocation`

---

### File Processor Plugins (IFileExtensionProcessor)

#### 2.5 BasicTextPlugin (PlainTextProcessor)
**Functionality:** Parse .txt, .log, .csv files
**Status:** ⚠️ MOVE TO CORE (critical)
**Reason:** Most common file type

**Recommendation:** Move to `FindPluginCore.Implementations.FileProcessors.PlainTextProcessor`
- Built-in, always available
- No plugin dependency

---

## Architecture After Deprecation

### Before (Current)
```
User Config:
├── PluginConfig.json (plugin loading)
├── .workspace file (locations + filters)
└── .rules.json (some filters/outputs)

Plugins Loaded:
├── BasicFilters (ISearchFilter)
├── BasicOutputs (ISearchOutput)
├── SessionManagementProcessor (IResultProcessor)
├── WatsonCrashProcessor (IResultProcessor)
├── EventLogPlugin (ISearchLocation)
├── ETWPlugin (ISearchLocation)
└── BasicTextPlugin (IFileExtensionProcessor)
```

### After (Proposed)
```
User Config:
└── .rules.json (SINGLE SOURCE OF TRUTH)
    ├── systemConfig (plugin settings)
    ├── sections (filters, enrichment, outputs)
    └── ... (future: inputs section)

Core Components (built-in):
├── EventLogLocation (ISearchLocation)
├── ETWLocation (ISearchLocation)
├── ZipLocation (ISearchLocation)
└── PlainTextProcessor (IFileExtensionProcessor)

Optional Plugins (kept):
└── KustoPlugin (ISearchLocation - cloud integration)
```

---

## Migration Path

### Step 1: Provide Migration Tools

#### 1.1 Plugin Config to RuleDSL Converter
```csharp
// Tool: ConvertPluginToRuleDSL
// Input: PluginConfig.json with BasicFilters enabled
// Output: filters.rules.json
```

**Example conversion:**
```
BasicFilters config → filter section in RuleDSL
BasicOutputs config → output section in RuleDSL
SessionManagementProcessor → enrichment section in RuleDSL
```

---

#### 1.2 Auto-Generate RuleDSL on First Run
```csharp
// In Program.cs or SearchQuery initialization:
if (HasLegacyPlugins())
{
    Console.WriteLine("Detected legacy plugins. Auto-generating RuleDSL config...");
    var rulesPath = GenerateRuleDSLFromPlugins();
    Console.WriteLine($"Generated: {rulesPath}");
    Console.WriteLine("Review and update your config to use RuleDSL instead of plugins.");
}
```

---

### Step 2: Mark Plugins as Deprecated

#### 2.1 Add Obsolete Attributes
```csharp
// In plugin code
[Obsolete("BasicFiltersPlugin is deprecated. Use RuleDSL filter sections instead. See MIGRATION_GUIDE.md")]
public class BasicFiltersPlugin : ISearchFilter
{
    // ... existing code
}
```

#### 2.2 Console Warnings
```csharp
// In PluginManager
if (pluginImplements<ISearchFilter>())
{
    Logger.Instance.Log("WARNING: ISearchFilter plugins are deprecated. Migrate to RuleDSL.");
    Console.WriteLine($"WARNING: Plugin '{pluginName}' is deprecated. See MIGRATION_GUIDE.md");
}
```

---

### Step 3: Move Core Plugins to Built-in

#### 3.1 Move EventLogPlugin
```
Before:
EventLogPlugin\EventLogPlugin.csproj (plugin project)
  └── LocalEventLogLocation.cs

After:
FindPluginCore\Implementations\Locations\EventLogLocation.cs (built-in)
```

#### 3.2 Update References
```csharp
// Before: Plugin dynamically loaded
var plugin = PluginManager.Load("EventLogPlugin.dll");

// After: Built-in, always available
var location = new EventLogLocation("Application");
query.Locations.Add(location);
```

---

### Step 4: Update Documentation

#### 4.1 Deprecation Notice
```markdown
# DEPRECATION NOTICE

The following plugin types are deprecated as of version X.Y:
- ISearchFilter → Use RuleDSL filter sections
- IResultProcessor → Use RuleDSL enrichment sections  
- ISearchOutput → Use RuleDSL output sections

See MIGRATION_GUIDE.md for migration instructions.
```

#### 4.2 Migration Guide
Create comprehensive guide showing before/after for each deprecated plugin.

---

## Implementation Phases

### Phase 1: Preparation (1-2 months)
- [ ] Create migration tool (PluginConfig → RuleDSL)
- [ ] Add deprecation warnings
- [ ] Update documentation
- [ ] Create example RuleDSL files for common plugins

### Phase 2: Soft Deprecation (3-6 months)
- [ ] Mark plugins as `[Obsolete]`
- [ ] Show warnings on startup
- [ ] Auto-generate RuleDSL from plugins
- [ ] User testing and feedback

### Phase 3: Move Core Plugins (6-9 months)
- [ ] Move EventLogPlugin to core
- [ ] Move ETWPlugin to core
- [ ] Move BasicTextPlugin to core
- [ ] Move ZipFilePlugin to core
- [ ] Update all references

### Phase 4: Remove Deprecated Plugins (12+ months)
- [ ] Remove ISearchFilter plugin support
- [ ] Remove IResultProcessor plugin support
- [ ] Remove ISearchOutput plugin support
- [ ] Keep ISearchLocation plugin support (for extensibility)

---

## Breaking Changes & Compatibility

### Breaking Changes
1. **PluginConfig.json structure changes**
   - Remove deprecated plugin entries
   - Merge into RuleDSL systemConfig

2. **Plugin interfaces removed**
   - ISearchFilter (replaced by RuleDSL)
   - IResultProcessor (replaced by RuleDSL)
   - ISearchOutput (replaced by RuleDSL)

3. **API changes**
   - `SearchQuery.Filters` list may become read-only or removed
   - `SearchQuery.Processors` list may become read-only or removed
   - `SearchQuery.Outputs` list may become read-only or removed

### Backward Compatibility Strategy

#### Option 1: Gradual Migration (Recommended)
- Keep plugin support for 12 months
- Show deprecation warnings
- Auto-convert to RuleDSL in background
- Users can opt-in to RuleDSL-only mode

#### Option 2: Dual Support
- Support both plugins AND RuleDSL indefinitely
- Plugins internally converted to RuleDSL
- Simpler for users but more maintenance

---

## Plugin Interfaces to Keep

### Keep: ISearchLocation
**Reason:** Extensibility for custom data sources
**Use cases:**
- Custom database connectors
- Cloud service integrations (AWS CloudWatch, GCP Logging)
- Network data sources (Syslog, SNMP)
- Custom file formats

**Recommendation:** Keep this as the ONLY plugin type

---

### Keep: IFileExtensionProcessor
**Reason:** Extensibility for custom file formats
**Use cases:**
- Binary log formats
- Custom structured logs (Protobuf, MessagePack)
- Encrypted log files

**Recommendation:** Keep but move common processors (txt, csv) to core

---

## Plugin Interfaces to Remove

### Remove: ISearchFilter
**Reason:** 100% replaced by RuleDSL filter sections
**Timeline:** 12 months

### Remove: IResultProcessor  
**Reason:** 100% replaced by RuleDSL enrichment sections
**Timeline:** 12 months

### Remove: ISearchOutput
**Reason:** 100% replaced by RuleDSL output sections
**Timeline:** 12 months

---

## Benefits of Deprecation

### 1. Simpler Architecture
- One config system instead of three (PluginConfig + Workspace + RuleDSL)
- Fewer projects to maintain
- Clearer separation of concerns

### 2. Better Performance
- No plugin loading overhead
- No reflection costs
- Faster startup

### 3. Easier Testing
- Fewer integration points
- No plugin versioning issues
- Simpler mocking

### 4. Better User Experience
- Single configuration file (.rules.json)
- Human-readable, versionable
- No "plugin not found" errors

### 5. Easier Distribution
- Fewer DLLs to package
- Smaller download size
- Simpler deployment

---

## Risks & Mitigation

### Risk 1: Breaking Existing Workflows
**Impact:** High - users may have complex plugin configurations
**Mitigation:**
- 12-month deprecation period
- Auto-migration tool
- Extensive documentation
- Backward compatibility mode

### Risk 2: Loss of Functionality
**Impact:** Medium - some plugins may have features not in RuleDSL
**Mitigation:**
- Audit all plugins before deprecation
- Extend RuleDSL if needed
- Keep plugin support for edge cases

### Risk 3: User Resistance
**Impact:** Medium - users may not want to migrate
**Mitigation:**
- Show clear benefits (simpler, faster, more maintainable)
- Provide migration tools
- Support both during transition

---

## Success Metrics

1. **Adoption Rate:** >80% of users using RuleDSL after 6 months
2. **Plugin Usage:** <20% using deprecated plugins after 12 months
3. **Support Tickets:** No increase in plugin-related issues
4. **Performance:** 10-20% faster startup time
5. **Codebase:** 30-40% reduction in plugin-related code

---

## Recommendation

**Start with Phase 1 immediately:**
1. Mark BasicFilters, BasicOutputs, SessionManagementProcessor as deprecated
2. Create migration tool (PluginConfig → RuleDSL)
3. Update documentation with migration guide
4. Release with warnings and auto-conversion

**Long-term vision:**
- Only ISearchLocation and IFileExtensionProcessor plugin interfaces remain
- Everything else (filters, processors, outputs) done via RuleDSL
- Core data sources (EventLog, ETW, PlainText) built-in
- Cloud/custom integrations remain as plugins

**Timeline:** 12-18 months for complete transition
