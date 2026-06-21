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

    [JsonPropertyName("useRegex")]
    public bool? UseRegex { get; set; }

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

    /// <summary>
    /// Collapse repeated identical interactions into one. When a log replays the same flow many times
    /// (e.g. a DISM log with thousands of sessions), the diagram would otherwise emit the same steps
    /// over and over and blow past the renderer's size limit. With dedupe on, each unique element
    /// (type + from + to + text + note/arrow) is emitted once, yielding a single generic flow.
    /// </summary>
    [JsonPropertyName("dedupe")]
    public bool Dedupe { get; set; }

    /// <summary>
    /// Hard cap on the number of body elements emitted (0 = unlimited). A safety net so no diagram can
    /// exceed the renderer's maximum text size; when hit, generation stops and appends a truncation note.
    /// </summary>
    [JsonPropertyName("maxElements")]
    public int MaxElements { get; set; }

    [JsonPropertyName("participants")]
    public List<UmlParticipant> Participants { get; set; } = new();

    [JsonPropertyName("rules")]
    public List<UmlRule> Rules { get; set; } = new();
}

/// <summary>How many times a single UML rule matched during the last diagram generation.</summary>
public class UmlRuleUsage
{
    public string Name { get; set; } = string.Empty;
    public string Match { get; set; } = string.Empty;
    public int Count { get; set; }
    /// <summary>The actual lines this rule matched (row id + content), in order.</summary>
    public List<UmlMatchedLine> Lines { get; set; } = new();
}

/// <summary>One log line a UML rule matched: its stable row id and content.</summary>
public class UmlMatchedLine
{
    public long RowId { get; set; } = -1;
    public string Content { get; set; } = string.Empty;
}

/// <summary>A source row that matched a UML rule, with the rule that matched it.</summary>
public class UmlRowMatch
{
    public long RowId { get; set; }
    public string RuleName { get; set; } = string.Empty;
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
