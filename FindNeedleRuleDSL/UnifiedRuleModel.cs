using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FindNeedleRuleDSL;

// System Configuration (replaces/extends PluginConfig)
public class SystemConfig
{
    /// <summary>
    /// If true, use global PluginConfig.json. If false, only use settings defined in this rules file.
    /// Default: true (backward compatible - always use global config unless explicitly disabled)
    /// </summary>
    [JsonPropertyName("useGlobalPluginConfig")]
    public bool UseGlobalPluginConfig { get; set; } = true;

    /// <summary>
    /// Plugin configuration - overrides global config when useGlobalPluginConfig=false
    /// or merges with global config when useGlobalPluginConfig=true
    /// </summary>
    [JsonPropertyName("plugins")]
    public PluginConfiguration? Plugins { get; set; }

    /// <summary>
    /// System-wide search settings
    /// </summary>
    [JsonPropertyName("search")]
    public SearchConfiguration? Search { get; set; }

    /// <summary>
    /// External tool paths (PlantUML, etc.)
    /// </summary>
    [JsonPropertyName("tools")]
    public ToolConfiguration? Tools { get; set; }
}

public class PluginConfiguration
{
    /// <summary>
    /// List of plugins to load (name + path + enabled flag)
    /// </summary>
    [JsonPropertyName("entries")]
    public List<PluginEntry>? Entries { get; set; }

    /// <summary>
    /// Path to fake load plugin executable
    /// </summary>
    [JsonPropertyName("fakeLoadPluginPath")]
    public string? FakeLoadPluginPath { get; set; }

    /// <summary>
    /// Which ISearchQuery implementation to use (e.g., "NuSearchQuery", "SearchQuery")
    /// </summary>
    [JsonPropertyName("searchQueryClass")]
    public string? SearchQueryClass { get; set; }

    /// <summary>
    /// Registry key for user-installed plugins (e.g., "Software\\FindNeedle\\Plugins")
    /// </summary>
    [JsonPropertyName("userRegistryPluginKey")]
    public string? UserRegistryPluginKey { get; set; }

    /// <summary>
    /// Enable/disable loading plugins from registry
    /// </summary>
    [JsonPropertyName("userRegistryPluginKeyEnabled")]
    public bool UserRegistryPluginKeyEnabled { get; set; } = false;
}

public class PluginEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("disabledReason")]
    public string? DisabledReason { get; set; }
}

public class SearchConfiguration
{
    /// <summary>
    /// Storage type: "InMemory", "SqlLite", or "Auto"
    /// </summary>
    [JsonPropertyName("storageType")]
    public string? StorageType { get; set; }

    /// <summary>
    /// Use synchronous search (blocking) vs asynchronous (background)
    /// </summary>
    [JsonPropertyName("useSynchronousSearch")]
    public bool UseSynchronousSearch { get; set; } = false;

    /// <summary>
    /// Default search depth for all locations: "Shallow", "Intermediate", "Deep"
    /// </summary>
    [JsonPropertyName("defaultDepth")]
    public string? DefaultDepth { get; set; }

    /// <summary>
    /// Search query name/description
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class ToolConfiguration
{
    /// <summary>
    /// Path to PlantUML JAR file
    /// </summary>
    [JsonPropertyName("plantUmlPath")]
    public string? PlantUmlPath { get; set; }

    /// <summary>
    /// Path to Mermaid CLI (mmdc) executable
    /// </summary>
    [JsonPropertyName("mermaidCliPath")]
    public string? MermaidCliPath { get; set; }
}

public class UnifiedRuleSet
{
    [JsonPropertyName("schemaVersion")]
    public string? SchemaVersion { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("systemConfig")]
    public SystemConfig? SystemConfig { get; set; }

    [JsonPropertyName("inputs")]
    public List<InputLocation>? Inputs { get; set; }

    [JsonPropertyName("sections")]
    public List<UnifiedRuleSection> Sections { get; set; } = new();
}

public class UnifiedRuleSection
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("providers")]
    public List<string> Providers { get; set; } = new();

    [JsonPropertyName("participants")]
    public List<UnifiedParticipant>? Participants { get; set; }

    [JsonPropertyName("rules")]
    public List<UnifiedRule> Rules { get; set; } = new();
}

public class UnifiedParticipant
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "participant"; // "participant" or "actor"
}

public class UnifiedRule
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("match")]
    public string Match { get; set; } = string.Empty;

    [JsonPropertyName("unmatch")]
    public string? Unmatch { get; set; }

    [JsonPropertyName("action")]
    public UnifiedRuleAction Action { get; set; } = new();
    
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}

public class UnifiedRuleAction
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // "message", "tag", "exclude", "include", "route", "output", etc.

    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("to")]
    public string? To { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("tag")]
    public string? Tag { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; } // For "tag" action: the tag value

    [JsonPropertyName("pattern")]
    public string? Pattern { get; set; } // For "extract" action: regex with named groups, run against the rule's Field (default message)

    [JsonPropertyName("set")]
    public Dictionary<string, string>? Set { get; set; } // For "extract": target field -> template ("{group}" capture refs and/or literal text)

    [JsonPropertyName("replacement")]
    public string? Replacement { get; set; } // For "redact" action: the mask text (default "[REDACTED]")

    [JsonPropertyName("processor")]
    public string? Processor { get; set; } // For "route" action: target processor name

    // Output action properties
    [JsonPropertyName("format")]
    public string? Format { get; set; } // "csv", "json", "xml", "txt"

    [JsonPropertyName("path")]
    public string? Path { get; set; } // Output file path (supports {date}, {time} placeholders)

    [JsonPropertyName("fields")]
    public List<string>? Fields { get; set; } // Fields to include in output

    [JsonPropertyName("includeHeaders")]
    public bool IncludeHeaders { get; set; } = true; // For CSV: include header row

    [JsonPropertyName("delimiter")]
    public string? Delimiter { get; set; } // For CSV: custom delimiter (default: comma)

    [JsonPropertyName("pretty")]
    public bool Pretty { get; set; } = true; // For JSON/XML: pretty print
}

// Input Location Configuration
public class InputLocation
{
    /// <summary>
    /// Type of input location: "folder", "file", "eventlog", "etw", "zip"
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Path to the location (file path, folder path, etc.)
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Search depth: "Shallow", "Intermediate", "Deep", "Crush"
    /// </summary>
    [JsonPropertyName("depth")]
    public string? Depth { get; set; }

    /// <summary>
    /// Optional name/description for this input
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Enable/disable this input
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}
