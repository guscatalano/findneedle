using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FindNeedleRuleDSL;

public class UnifiedRuleSet
{
    [JsonPropertyName("schemaVersion")]
    public string? SchemaVersion { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

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
    public string Type { get; set; } = string.Empty; // "message", "tag", "exclude", "include", "route", etc.

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

    [JsonPropertyName("processor")]
    public string? Processor { get; set; } // For "route" action: target processor name
}
