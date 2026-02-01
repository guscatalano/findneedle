using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace FindNeedleUmlDsl;

/// <summary>
/// Represents a UML generation rule defined in JSON.
/// </summary>
public class UmlRule
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("match")]
    public string Match { get; set; } = string.Empty;

    [JsonPropertyName("unmatch")]
    public string? Unmatch { get; set; }

    [JsonPropertyName("action")]
    public UmlAction Action { get; set; } = new();
}

public class UmlAction
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "message";

    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("to")]
    public string? To { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("arrowStyle")]
    public string ArrowStyle { get; set; } = "solid";

    [JsonPropertyName("notePosition")]
    public string? NotePosition { get; set; }
}

public class UmlRuleDefinition
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("participants")]
    public List<UmlParticipant> Participants { get; set; } = new();

    [JsonPropertyName("rules")]
    public List<UmlRule> Rules { get; set; } = new();
}

public class UmlParticipant
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "participant";
}
