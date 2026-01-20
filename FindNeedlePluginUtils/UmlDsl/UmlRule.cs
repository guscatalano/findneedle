using System.Text.Json.Serialization;

namespace FindNeedlePluginUtils.UmlDsl;

/// <summary>
/// Represents a UML generation rule defined in JSON.
/// </summary>
public class UmlRule
{
    /// <summary>
    /// Unique name/identifier for this rule.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Text pattern to match in the log message.
    /// </summary>
    [JsonPropertyName("match")]
    public string Match { get; set; } = string.Empty;

    /// <summary>
    /// Optional text that, if present, excludes the match.
    /// </summary>
    [JsonPropertyName("unmatch")]
    public string? Unmatch { get; set; }

    /// <summary>
    /// The UML action to generate when this rule matches.
    /// </summary>
    [JsonPropertyName("action")]
    public UmlAction Action { get; set; } = new();
}

/// <summary>
/// Represents a UML action (message, note, etc.)
/// </summary>
public class UmlAction
{
    /// <summary>
    /// Type of UML element: "message", "note", "activate", "deactivate", "group"
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "message";

    /// <summary>
    /// Source actor/participant.
    /// </summary>
    [JsonPropertyName("from")]
    public string? From { get; set; }

    /// <summary>
    /// Target actor/participant.
    /// </summary>
    [JsonPropertyName("to")]
    public string? To { get; set; }

    /// <summary>
    /// Message or note text. Supports placeholders:
    /// - {afterMatch} - Text after the matched pattern
    /// - {afterMatch:untilSpace} - Text after match until first space
    /// - {afterMatch:until:X} - Text after match until character X
    /// - {extract:pattern} - Regex extraction
    /// </summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Arrow style for messages: "solid", "dashed", "async"
    /// </summary>
    [JsonPropertyName("arrowStyle")]
    public string ArrowStyle { get; set; } = "solid";

    /// <summary>
    /// Position for notes: "left", "right", "over"
    /// </summary>
    [JsonPropertyName("notePosition")]
    public string? NotePosition { get; set; }
}

/// <summary>
/// Root object for a UML rule definition file.
/// </summary>
public class UmlRuleDefinition
{
    /// <summary>
    /// Title for the diagram.
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// List of participants/actors to declare.
    /// </summary>
    [JsonPropertyName("participants")]
    public List<UmlParticipant> Participants { get; set; } = new();

    /// <summary>
    /// List of rules for matching and generating UML.
    /// </summary>
    [JsonPropertyName("rules")]
    public List<UmlRule> Rules { get; set; } = new();
}

/// <summary>
/// Represents a participant/actor in the sequence diagram.
/// </summary>
public class UmlParticipant
{
    /// <summary>
    /// Internal identifier for the participant.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Display name for the participant.
    /// </summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Type of participant: "actor", "participant", "database", "queue"
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "participant";
}
