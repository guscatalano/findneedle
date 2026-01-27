using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace FindNeedlePluginUtils.UmlDsl;

public class UnifiedRuleSet
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("sections")]
    public List<UnifiedRuleSection> Sections { get; set; } = new();
}

public class UnifiedRuleSection
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("providers")]
    public List<string> Providers { get; set; } = new();

    [JsonPropertyName("rules")]
    public List<UnifiedRule> Rules { get; set; } = new();
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
    public string Type { get; set; } = string.Empty; // "message", "tag", etc.

    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("to")]
    public string? To { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("tag")]
    public string? Tag { get; set; }
}
