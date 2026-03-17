# Output File Verification Tests

## Overview

Added **real file output tests** that actually create TXT and CSV files and verify their contents. These tests demonstrate the complete end-to-end functionality of the `OutputRuleProcessor` for replacing the deprecated `OutputToPlainFile` plugin.

## Tests Added

### 1. `OutputToFile_PlainTextFormat_VerifyFileCreated` ✅

**What it tests:**
- Creates a TXT file with search results
- Verifies file creation
- Verifies file contents (all 3 lines present)
- Demonstrates plain text output format

**Sample Output:**
```
searchable=ERROR: Connection failed
searchable=ERROR: File not found
searchable=WARNING: Low memory
```

**Configuration Used:**
```csharp
var outputSection = new
{
    name = "ErrorOutput",
    purpose = "output",
    providers = new[] { "*" },
    rules = new[]
    {
        new
        {
            action = new
            {
                type = "output",
                format = "txt",
                path = outputPath,
                fields = new[] { "searchable" },
                includeHeaders = false
            }
        }
    }
};
```

---

### 2. `OutputToFile_CsvFormat_WithHeaders_VerifyFileCreated` ✅

**What it tests:**
- Creates a CSV file with headers and 3 data rows
- Verifies file creation
- Verifies CSV structure (header row + data rows)
- Verifies CSV contents (username, events)
- Demonstrates CSV output with custom fields

**Sample Output:**
```csv
timestamp,message,searchable
2024-01-15 10:30:00,User logged in,INFO: User john.doe logged in successfully
2024-01-15 11:00:00,File accessed,INFO: User john.doe accessed file config.xml
2024-01-15 17:00:00,User logged off,INFO: User john.doe logged off
```

**Configuration Used:**
```csharp
var outputSection = new
{
    name = "SessionCsvOutput",
    purpose = "output",
    providers = new[] { "*" },
    rules = new[]
    {
        new
        {
            action = new
            {
                type = "output",
                format = "csv",
                path = outputPath,
                includeHeaders = true,
                delimiter = ",",
                fields = new[] { "timestamp", "message", "searchable" }
            }
        }
    }
};
```

---

## Key Insights

### Direct OutputRuleProcessor Usage

Unlike the filter/enrichment tests that use `FindNeedleRuleDSLPlugin`, these tests **call `OutputRuleProcessor` directly** because:

1. **FindNeedleRuleDSLPlugin** is focused on tagging and result filtering
2. **OutputRuleProcessor** is the dedicated component for file output
3. This separation of concerns is intentional in the architecture

**Pattern:**
```csharp
// Create processor
var outputProcessor = new FindNeedleRuleDSL.OutputRuleProcessor();

// Create output section configuration
var outputSection = new { /* config */ };

// Process results and generate files
outputProcessor.ProcessOutputRules(results, new[] { outputSection });

// Verify file exists and contents
Assert.IsTrue(File.Exists(outputPath));
var contents = File.ReadAllText(outputPath);
```

### Supported Output Formats

The `OutputRuleProcessor` supports multiple formats:

| Format | Extension | Use Case |
|--------|-----------|----------|
| `txt` | `.txt` | Simple line-based output |
| `csv` | `.csv` | Structured data with headers |
| `json` | `.json` | Hierarchical data (see existing test) |
| `xml` | `.xml` | Structured markup (not yet tested) |

### Field Mapping

The processor maps field names to `ISearchResult` properties:

| Field Name | ISearchResult Property |
|-----------|----------------------|
| `timestamp`, `time`, `logtime` | `GetLogTime()` |
| `level` | `GetLevel()` |
| `source` | `GetSource()` |
| `message`, `msg` | `GetMessage()` |
| `searchable`, `data` | `GetSearchableData()` |
| `machine`, `machinename` | `GetMachineName()` |
| `username`, `user` | `GetUsername()` |
| `taskname`, `task` | `GetTaskName()` |
| `opcode` | `GetOpCode()` |
| `resultsource`, `file` | `GetResultSource()` |

**Default:** Falls back to `GetSearchableData()` for unknown fields

---

## Test Results

```
Test Run Successful.
Total tests: 3 (OutputToFile tests)
     Passed: 3 ✅
     Failed: 0
Duration: ~0.7s
```

**All DeprecatedPluginReplacementTests:**
```
Test Run Successful.
Total tests: 17 (all plugin replacement tests)
     Passed: 17 ✅
     Failed: 0
Duration: ~0.7s
```

---

## Migration Example: OutputToPlainFile → RuleDSL

### Old Plugin (Deprecated)
```csharp
// Old approach with OutputToPlainFile plugin
var output = new OutputToPlainFile();
output.SetOutputPath("errors.txt");
output.ProcessResults(results);
```

### New RuleDSL
```csharp
// New approach with OutputRuleProcessor + RuleDSL config
var outputProcessor = new OutputRuleProcessor();
var config = new
{
    name = "Export",
    purpose = "output",
    providers = new[] { "*" },
    rules = new[]
    {
        new
        {
            action = new
            {
                type = "output",
                format = "txt",
                path = "errors.txt",
                fields = new[] { "searchable" }
            }
        }
    }
};

outputProcessor.ProcessOutputRules(results, new[] { config });
```

**Or using JSON file:**
```json
{
  "sections": [
    {
      "name": "Export",
      "purpose": "output",
      "providers": [ "*" ],
      "rules": [
        {
          "action": {
            "type": "output",
            "format": "txt",
            "path": "errors.txt",
            "fields": [ "searchable" ]
          }
        }
      ]
    }
  ]
}
```

---

## Advanced Features Demonstrated

### 1. Custom Fields Selection
```csharp
fields = new[] { "timestamp", "message", "searchable" }
```
Only includes specified fields in output.

### 2. Header Control
```csharp
includeHeaders = true  // CSV with header row
includeHeaders = false // No header row
```

### 3. Custom Delimiters
```csharp
delimiter = ","  // Comma-separated
delimiter = "\t" // Tab-separated
delimiter = "|"  // Pipe-separated
```

### 4. Output Path Control
Files are created in specified directory with auto-creation:
```csharp
var outputPath = Path.Combine(_testOutputDir, "errors.txt");
```

---

## Running the Tests

```bash
# Run all output tests
dotnet test FindNeedleRuleDSLTests/FindNeedleRuleDSLTests.csproj --filter "FullyQualifiedName~OutputToFile"

# Run specific test
dotnet test --filter "OutputToFile_PlainTextFormat_VerifyFileCreated"

# Run with verbose logging
dotnet test --filter "FullyQualifiedName~OutputToFile" --logger "console;verbosity=detailed"
```

---

## Benefits

✅ **Real file verification** - Actually creates and checks files  
✅ **Complete examples** - Shows exact configuration needed  
✅ **Copy-paste ready** - Can use test config directly  
✅ **Multiple formats** - Demonstrates TXT and CSV  
✅ **Comprehensive coverage** - Tests headers, fields, content  

---

## Future Enhancements

Possible additions:
- [ ] XML output format test
- [ ] JSON output format test (with content verification)
- [ ] Custom delimiter tests (tab, pipe)
- [ ] Path placeholder tests (`{date}`, `{time}`)
- [ ] Large file performance test
- [ ] Multi-section output test (multiple files)
- [ ] Error handling tests (invalid paths, permissions)

---

## Related Documentation

- **DEPRECATED_PLUGINS_MIGRATION.md** - Complete migration guide
- **DEPRECATED_PLUGIN_TEST_COVERAGE.md** - All test documentation
- **DEPRECATED_PLUGIN_TESTS_SUMMARY.md** - Implementation summary
- **FindNeedleRuleDSL/OutputRuleProcessor.cs** - Output processor implementation

---

**Status:** ✅ Complete and verified. Real file output working correctly for TXT and CSV formats!
