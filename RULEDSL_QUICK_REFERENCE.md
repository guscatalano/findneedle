# RuleDSL Integration - Quick Reference

## Files Created/Modified

### NEW FILES
- `FindPluginCore/Searching/RuleDSL/RuleLoader.cs` - JSON rule loading
- `FindPluginCore/Searching/RuleDSL/RuleEvaluationEngine.cs` - Rule evaluation
- `FindPluginCore/Searching/RuleDSL/RULEDSL_USAGE.md` - Usage guide
- `RULEDSL_INTEGRATION_SUMMARY.md` - Implementation details
- `FindNeedleRuleDSL/Examples/example-filter-only.rules.json` - Example
- `FindNeedleRuleDSL/Examples/example-enrichment-only.rules.json` - Example
- `FindNeedleRuleDSL/Examples/example-uml-only.rules.json` - Example
- `FindNeedleRuleDSL/Examples/example-combined-pipeline.rules.json` - Example
- `FindNeedleRuleDSL/Examples/example-filter-advanced.rules.json` - Example
- `FindNeedleRuleDSL/Examples/comprehensive-pipeline.rules.json` - Example

### MODIFIED FILES
- `FindNeedlePluginLib/Interfaces/ISearchQuery.cs` - Added RulesConfigPaths, LoadedRules
- `FindPluginCore/Searching/SearchQuery.cs` - Added rule integration
- `FindPluginCore/Searching/NuSearchQuery.cs` - Added properties
- `FindNeedlePluginLib/TestClasses/FakeSearchQuery.cs` - Added properties
- `FindNeedleRuleDSL/UnifiedRuleModel.cs` - Enhanced with field, dateRange, purpose

## How It Works

### 1. Define Rules
```json
{
  "schemaVersion": "1.0",
  "sections": [
    {
      "name": "MyRules",
      "purpose": "filter",
      "rules": [
        {
          "id": "exclude-verbose",
          "field": "level",
          "match": "Debug|Verbose",
          "unmatch": "error",
          "dateRange": { "withinLast": "24h" },
          "actions": [
            { "type": "exclude" }
          ]
        }
      ]
    }
  ]
}
```

### 2. Use in Code
```csharp
var query = new SearchQuery();
query.RulesConfigPaths.Add("my-rules.rules.json");
query.Locations.Add(new FolderLocation(@"C:\Logs"));
query.RunThrough(); // Rules automatically loaded and applied
```

### 3. Pipeline Execution
```
LoadRules()
  ?
GetFilteredResults() + ApplyRuleFiltering()
  ?
Step3_ResultsToProcessors() + ApplyRuleEnrichment()
  ?
ProcessAllResultsToOutput()
```

## Field Reference

| Field | Source Method | Example Match |
|-------|---------------|---------------|
| `level` | `GetLevel()` | "Error", "Warning", "Critical" |
| `source` | `GetSource()` | "Application", "System", "Security" |
| `machineName` | `GetMachineName()` | "prod-01", "dev-server" |
| `username` | `GetUsername()` | "SYSTEM", "admin", "domain\user" |
| `taskName` | `GetTaskName()` | "Logon", "Process Create" |
| `message` | `GetMessage()` | Event message text |
| `searchabledata` | `GetSearchableData()` | Full text content (default) |
| `logTime` | `GetLogTime()` | For datetime filtering |

## Action Types

| Action | Purpose | Example |
|--------|---------|---------|
| `exclude` | Remove result | `{"type":"exclude"}` |
| `include` | Keep result | `{"type":"include"}` |
| `tag` | Add label | `{"type":"tag","value":"Critical"}` |
| `route` | Send to processor | `{"type":"route","processor":"CrashProc"}` |
| `message` | UML flow | `{"type":"message","from":"A","to":"B","text":"msg"}` |

## Purpose Types

| Purpose | When It Runs | What It Does |
|---------|------------|--------------|
| `filter` | Step 2 | Include/exclude results |
| `enrichment` | Step 3 | Add tags to results |
| `uml` | Step 3+ | Define visualization flows |

## DateTime Formats

### Relative Time
```json
"withinLast": "1h"    // Last hour
"withinLast": "24h"   // Last 24 hours
"withinLast": "7d"    // Last 7 days
```

### Absolute Time
```json
"after": "2024-01-01T00:00:00Z"    // ISO 8601
"before": "-30d"                    // 30 days ago
```

## Pattern Matching

Patterns are regex-based, case-insensitive:

```json
"match": "error|exception|fatal"     // OR patterns
"unmatch": "expected|allowed"        // Exclude if matches
```

## Complete Example

```csharp
var query = new SearchQuery();
query.Locations.Add(new FolderLocation(@"C:\Windows\System32\winevt\Logs"));

// Add filters and enrichment
query.RulesConfigPaths.Add("filters.rules.json");
query.RulesConfigPaths.Add("enrichment.rules.json");

// Optional: Add traditional filters still work
query.AddFilter(new SimpleKeywordFilter("error"));

// Run with cancellation support
var cts = new CancellationTokenSource();
query.RunThrough(cts.Token);

// Access statistics
Console.WriteLine($"Processed: {query.stats.ResultCount}");
```

## Troubleshooting

### Rules not loading
- Check `RulesConfigPaths` is set before `RunThrough()`
- Verify JSON file is valid (use `jsonlint.com`)
- Check file path is absolute or relative to app directory

### Matching not working
- Use regex patterns (not simple wildcards)
- Test patterns at `regex101.com` with flag: IgnoreCase
- Check field name spelling and case

### No filtering happening
- Ensure `purpose: "filter"` is set in section
- Check if all results match the pattern (too broad?)
- Try simpler patterns first

### Performance issues
- Complex regex patterns can be slow
- Limit datetime ranges
- Use specific field matching instead of searchabledata
- Filter out noise early

## Integration Points

- **ISearchQuery Interface** - Defines RulesConfigPaths, LoadedRules
- **SearchQuery Pipeline** - Loads rules, applies at each step
- **RuleEvaluationEngine** - Core matching and action logic
- **RuleLoader** - JSON parsing and section filtering
- **UnifiedRuleModel** - Data structures for rules

## Backward Compatibility

? Existing code continues to work:
- Traditional `ISearchFilter` plugins still supported
- `Filters` property unchanged
- `Processors` property unchanged  
- All existing methods work as before

## Next Steps (Future Enhancements)

- [ ] Rule caching/compilation for performance
- [ ] Rule validation tool
- [ ] GUI rule builder
- [ ] Rule versioning/history
- [ ] Custom function support in patterns
- [ ] Event streaming mode
- [ ] Result transformation actions
