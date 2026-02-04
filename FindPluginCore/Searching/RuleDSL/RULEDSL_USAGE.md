# RuleDSL Integration - Usage Guide

## Overview

The RuleDSL (Rule Domain Specific Language) is now integrated into FindPluginCore's `SearchQuery` and `NuSearchQuery` classes. Rules are defined in JSON and processed at each step of the pipeline:

1. **Filter Rules** - Reduce noise by excluding unwanted results
2. **Enrichment Rules** - Add tags and classifications to results  
3. **UML Rules** - Define visualization flows through system participants

## Quick Start

### 1. Define Rules in JSON

Create a rules file (e.g., `my-rules.rules.json`):

```json
{
  "schemaVersion": "1.0",
  "version": "1.0",
  "title": "My Rules",
  "sections": [
    {
      "name": "NoiseFilter",
      "description": "Filter out debug logs",
      "purpose": "filter",
      "rules": [
        {
          "id": "exclude-debug",
          "field": "level",
          "match": "Debug|Verbose",
          "actions": [
            { "type": "exclude" }
          ]
        }
      ]
    },
    {
      "name": "ErrorClassifier",
      "description": "Tag critical errors",
      "purpose": "enrichment",
      "rules": [
        {
          "id": "tag-critical",
          "field": "level",
          "match": "Critical|Error",
          "actions": [
            { "type": "tag", "value": "Critical" }
          ]
        }
      ]
    }
  ]
}
```

### 2. Use Rules in Code

```csharp
var query = new SearchQuery();
query.Locations.Add(new FolderLocation(@"C:\Logs"));

// Add rule files
query.RulesConfigPaths.Add("path/to/my-rules.rules.json");

// Run - rules are automatically loaded and applied
query.RunThrough();

// Results are filtered and enriched per rules
```

## Available Actions

### Filter Actions
- `exclude` - Remove result from stream
- `include` - Keep result in stream

### Enrichment Actions
- `tag` - Add a tag/label to result (e.g., "Critical", "Security")
  ```json
  { "type": "tag", "value": "Critical" }
  ```

### Routing Actions (UML)
- `route` - Direct result to specific processor
- `message` - Define participant message flows

## Field Matching

Rules can match against specific result fields:

```json
{
  "field": "level",        // Match specific field
  "match": "Error|Warning",
  "actions": [...]
}
```

**Available fields:**
- `level` - Event severity (Error, Warning, Critical, Info, Verbose)
- `source` - Event log source (Application, System, Security)
- `machineName` - Computer name
- `username` - User account
- `taskName` - Task/event type name
- `message` - Event message text
- `searchabledata` - Full searchable text (default if no field specified)

## DateTime Filtering

Rules can filter by time range:

```json
{
  "id": "recent-only",
  "match": ".*",
  "dateRange": {
    "field": "logTime",
    "withinLast": "24h"  // Last 24 hours
  },
  "actions": [
    { "type": "include" }
  ]
}
```

**Relative time examples:**
- `"1h"` - Last hour
- `"24h"` - Last 24 hours
- `"7d"` - Last 7 days

**Absolute time examples:**
- `"2024-01-01T00:00:00Z"` - ISO 8601 format
- `"-30d"` - 30 days ago

## Pattern Matching

Match patterns use regex (case-insensitive):

```json
{
  "match": "error|exception|fatal",
  "unmatch": "expected|allowed"
}
```

## Multiple Rule Files

Load multiple rule files to compose behavior:

```csharp
query.RulesConfigPaths.Add("rules/filters.rules.json");
query.RulesConfigPaths.Add("rules/enrichment.rules.json");
query.RulesConfigPaths.Add("rules/uml.rules.json");

query.RunThrough(); // All rules applied in order
```

## Pipeline Integration

Rules are applied at these steps:

1. **Step 1 (Load)** - Locations loaded into memory
2. **Step 2 (Filter)** - Rule-based filtering applied
   - Filter sections execute here
   - Noise reduced before processing
3. **Step 3 (Process)** - Enrichment applied
   - Enrichment sections execute here
   - Tags added to results
4. **Step 4 (Output)** - Results sent to outputs
5. **Step 5 (Done)** - Completion

## Examples

See example rule files in `FindNeedleRuleDSL/Examples/`:
- `example-filter-only.rules.json` - Filtering patterns
- `example-enrichment-only.rules.json` - Tagging examples
- `example-filter-advanced.rules.json` - Advanced field-based filtering
- `example-combined-pipeline.rules.json` - Full pipeline example
