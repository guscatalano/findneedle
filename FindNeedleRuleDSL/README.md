# FindNeedleRuleDSL - Watson Plugin Replacement

This document explains how to use **FindNeedleRuleDSL** as a replacement for the **Watson Plugin**.

## Overview

The FindNeedleRuleDSL provides a flexible, JSON-based DSL (Domain Specific Language) for defining rules that process search results. Instead of hardcoding logic in a C# plugin, you can define rules in JSON and have them evaluated automatically.

### Advantages Over Watson Plugin

| Feature | Watson | DSL |
|---------|--------|-----|
| Adding new detection rules | Requires code change + rebuild | JSON edit |
| Reusing rules across projects | Copy plugin dll | JSON file |
| Provider filtering | Hardcoded | Configurable |
| Disable/enable rules | Recompile | Configuration toggle |
| Non-developers can modify | No | Yes |
| Complex conditions (unmatch) | Limited | Supported |

## Quick Start

### 1. Create a Rules File

```json
{
  "title": "Crash Detection Rules",
  "sections": [
    {
      "name": "DotNetCrashes",
      "providers": ["EventLog", "ETW"],
      "rules": [
        {
          "name": "DotNetApplicationCrash",
          "match": "A .NET application failed",
          "enabled": true,
          "action": {
            "type": "tag",
            "tag": "DotNetCrash"
          }
        },
        {
          "name": "ApplicationHang",
          "match": "Application Hang",
          "enabled": true,
          "action": {
            "type": "tag",
            "tag": "ApplicationHang"
          }
        }
      ]
    }
  ]
}
```

### 2. Use in Code

```csharp
// Create processor with rules file
var processor = new FindNeedleRuleDSLPlugin("EventLog", "path/to/crash-detection.rules.json");

// Process your search results
processor.ProcessResults(searchResults);

// Get results
foreach (var tag in processor.GetFoundTags())
{
    Console.WriteLine($"{tag}: {processor.GetTagCount(tag)}");
}

// Or get detailed results
var matches = processor.GetMatchedResults();
```

## Rule Syntax

### Rule Structure

```json
{
  "name": "RuleName",
  "match": "text to search for",
  "unmatch": "optional text that must NOT be present",
  "enabled": true,
  "action": {
    "type": "tag",
    "tag": "TagName"
  }
}
```

### Properties

- **name**: Unique identifier for the rule
- **match**: Required text that must be found (case-insensitive)
- **unmatch**: Optional text that disqualifies a match (case-insensitive)
- **enabled**: Boolean to enable/disable rule without deleting it
- **action**: What to do when rule matches
  - type: Currently supports "tag" (extensible for future types)
  - tag: The tag name to apply

### Examples

#### Basic Match
```json
{
  "name": "OutOfMemoryError",
  "match": "OutOfMemoryException",
  "action": { "type": "tag", "tag": "OutOfMemory" }
}
```

#### Match with Exclusion
```json
{
  "name": "AccessViolation",
  "match": "access violation",
  "unmatch": "allowed",
  "action": { "type": "tag", "tag": "AccessViolation" }
}
```
This will match "access violation" but NOT "access violation allowed".

#### Disable Rule
```json
{
  "name": "DisabledRule",
  "match": "some text",
  "enabled": false,
  "action": { "type": "tag", "tag": "SomeTag" }
}
```

## Section Organization

Sections allow you to organize rules and filter by provider:

```json
{
  "title": "Application Rules",
  "sections": [
    {
      "name": "DotNetCrashes",
      "providers": ["EventLog", "ETW"],
      "rules": [ /* rules that apply to EventLog and ETW */ ]
    },
    {
      "name": "LinuxLogs",
      "providers": ["Syslog"],
      "rules": [ /* rules for Linux logs */ ]
    }
  ]
}
```

**Providers**: Only rules in sections matching the processor's provider will be executed.

## Migration from Watson Plugin

### Watson Original Code
```csharp
public class WatsonCrashProcessor : IResultProcessor
{
    // ...
    public void ProcessResults(List<ISearchResult> results)
    {
        resultList.Clear();
        foreach (var result in results)
        {
            if(result.GetSearchableData().Contains("A .NET application failed."))
            {
                resultList.Add(result);
            }
            if (result.GetSearchableData().Contains("Application Hang"))
            {
                resultList.Add(result);
            }
        }
    }
}
```

### DSL Equivalent
**File: watson-replacement.rules.json**
```json
{
  "title": "Watson Crash Detection",
  "sections": [{
    "name": "WatsonRules",
    "providers": ["EventLog"],
    "rules": [
      {
        "name": "DotNetCrash",
        "match": "A .NET application failed",
        "action": { "type": "tag", "tag": "DotNetCrash" }
      },
      {
        "name": "ApplicationHang",
        "match": "Application Hang",
        "action": { "type": "tag", "tag": "ApplicationHang" }
      }
    ]
  }]
}
```

**Code:**
```csharp
var processor = new FindNeedleRuleDSLPlugin("EventLog", "watson-replacement.rules.json");
processor.ProcessResults(results);

var dotNetCrashes = processor.GetTagCount("DotNetCrash");
var hangs = processor.GetTagCount("ApplicationHang");
```

## API Reference

### FindNeedleRuleDSLPlugin

```csharp
// Constructor
public FindNeedleRuleDSLPlugin(string provider = "EventLog", string? rulesFilePath = null)

// IResultProcessor interface
public void ProcessResults(List<ISearchResult> results)
public string GetOutputText()
public string GetDescription()

// Additional methods
public int GetTagCount(string tag)
public IEnumerable<string> GetFoundTags()
public IEnumerable<ISearchResult> GetMatchedResults()
```

## Advanced Examples

### Session Management Rules
```json
{
  "title": "Session Management Tracking",
  "sections": [{
    "name": "SessionTracking",
    "providers": ["EventLog"],
    "rules": [
      {
        "name": "LogonEvent",
        "match": "user logged on",
        "action": { "type": "tag", "tag": "UserLogon" }
      },
      {
        "name": "LogoffEvent",
        "match": "user logged off",
        "action": { "type": "tag", "tag": "UserLogoff" }
      },
      {
        "name": "FailedLogon",
        "match": "logon failed",
        "action": { "type": "tag", "tag": "FailedLogon" }
      }
    ]
  }]
}
```

### Security Rules
```json
{
  "title": "Security Event Detection",
  "sections": [{
    "name": "SecurityEvents",
    "providers": ["EventLog", "ETW"],
    "rules": [
      {
        "name": "PrivilegeEscalation",
        "match": "privilege escalation",
        "action": { "type": "tag", "tag": "PrivilegeEscalation" }
      },
      {
        "name": "UnauthorizedAccess",
        "match": "access denied",
        "unmatch": "expected",
        "action": { "type": "tag", "tag": "UnauthorizedAccess" }
      },
      {
        "name": "SuspiciousActivity",
        "match": "suspicious",
        "action": { "type": "tag", "tag": "Suspicious" }
      }
    ]
  }]
}
```

### Performance Analysis Rules
```json
{
  "title": "Performance Issues",
  "sections": [{
    "name": "PerformanceEvents",
    "providers": ["ETW"],
    "rules": [
      {
        "name": "HighMemoryUsage",
        "match": "memory usage exceeded",
        "action": { "type": "tag", "tag": "HighMemory" }
      },
      {
        "name": "HighCPU",
        "match": "CPU utilization",
        "action": { "type": "tag", "tag": "HighCPU" }
      },
      {
        "name": "DeadlockDetected",
        "match": "deadlock",
        "action": { "type": "tag", "tag": "Deadlock" }
      }
    ]
  }]
}
```

## Testing

Comprehensive tests are provided in `FindNeedleRuleDSLPluginTests.cs`:

```csharp
[TestMethod]
public void ProcessResults_BasicCrashDetection_FindsSingleMatch()
{
    var processor = new FindNeedleRuleDSLPlugin("EventLog", rulesFile);
    processor.ProcessResults(results);
    Assert.AreEqual(1, processor.GetTagCount("DotNetCrash"));
}
```

Run all tests to verify rule processing:
```bash
dotnet test FindNeedleRuleDSL
```

## Configuration

### Default Rules Location
If no rules file is specified, the plugin looks for:
```
[AppDirectory]\rules\default.rules.json
```

### Custom Rules Location
```csharp
var processor = new FindNeedleRuleDSLPlugin(
    provider: "EventLog",
    rulesFilePath: @"C:\config\my-rules.json"
);
```

## Best Practices

1. **Organize by concern**: Use separate sections for different types of rules
2. **Make rules specific**: More specific matches = fewer false positives
3. **Use unmatch for exclusions**: Rather than complex matches
4. **Version your rules**: Include date or version in filename
5. **Test thoroughly**: Use the test suite to validate rule behavior
6. **Document rules**: Add comments explaining the business logic
7. **Disable instead of delete**: Keep disabled rules for reference

## Comparison Matrix

| Scenario | Watson | DSL |
|----------|--------|-----|
| Add crash detection | Edit .cs, rebuild, deploy | Edit .json, no rebuild |
| Share rules between projects | Copy plugin | Copy rules file |
| Track which rule matched | No | Yes (via tags) |
| Disable a rule temporarily | Remove code, rebuild | Set `"enabled": false` |
| Prevent false positives | Hardcoded logic | Use `unmatch` property |
| Performance | Compiled | Interpreted (slight overhead) |

## Next Steps

1. Copy `crash-detection.rules.json` from Examples
2. Integrate `FindNeedleRuleDSLPlugin` into your result processor pipeline
3. Run tests to verify behavior
4. Customize rules for your specific use cases
5. Remove WatsonPlugin dependency

## Support

For issues or feature requests, refer to the test suite for usage examples or the model classes for extensibility.
