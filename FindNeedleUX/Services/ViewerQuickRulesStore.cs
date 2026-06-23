using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace FindNeedleUX.Services;

/// <summary>
/// A session-only "quick rule" created by right-clicking a column header: a regex that pulls a value
/// out of the Message into a column and/or strips the match from the Message. Applied at display time
/// to each <see cref="FindNeedleUX.LogLine"/> as it's built — no re-scan, no persistence (cleared on
/// restart or workspace clear).
/// </summary>
public sealed class ViewerQuickRule
{
    public required Regex Pattern { get; init; }
    public string CaptureGroup { get; init; } = "v";
    /// <summary>Enrichment field name to set ("TaskName", "Source" for the Provider column, "ProcessId"…),
    /// or null to only strip.</summary>
    public string TargetField { get; init; }
    public bool Strip { get; init; }
    public string ColumnLabel { get; init; } = "";

    /// <summary>Evaluate against a message: did it match, the captured value (if extracting), and the
    /// message after stripping. Used by both the live preview and the apply path so they agree.</summary>
    public (bool matched, string captured, string after) Evaluate(string message)
    {
        if (string.IsNullOrEmpty(message)) return (false, null, message);
        Match m;
        try { m = Pattern.Match(message); } catch { return (false, null, message); }
        if (!m.Success) return (false, null, message);
        string captured = TargetField != null ? m.Groups[CaptureGroup].Value : null;
        string after = message;
        if (Strip)
            after = Regex.Replace(message.Remove(m.Index, m.Length), @"\s{2,}", " ").Trim();
        return (true, captured, after);
    }

    public void Apply(FindNeedleUX.LogLine line)
    {
        var (matched, captured, after) = Evaluate(line.Message);
        if (!matched) return;
        if (TargetField != null && !string.IsNullOrEmpty(captured)) SetField(line, TargetField, captured);
        if (Strip) line.Message = after;
    }

    private static void SetField(FindNeedleUX.LogLine line, string field, string value)
    {
        switch (field)
        {
            case "TaskName":    line.TaskName = value; break;
            case "Source":      line.Provider = value; break; // Provider column is fed by Source
            case "ProcessId":   line.ProcessId = value; break;
            case "ProcessName": line.ProcessName = value; break;
            case "ThreadId":    line.ThreadId = value; break;
            case "EventId":     line.EventId = value; break;
            case "ActivityId":  line.ActivityId = value; break;
            case "Channel":     line.Channel = value; break;
            case "OpCode":      line.OpCode = value; break;
        }
    }
}

/// <summary>In-memory list of active viewer quick rules (session-only).</summary>
public static class ViewerQuickRulesStore
{
    private static readonly List<ViewerQuickRule> _rules = new();

    public static IReadOnlyList<ViewerQuickRule> Rules => _rules;
    public static bool Any => _rules.Count > 0;

    public static void Add(ViewerQuickRule rule) { if (rule != null) _rules.Add(rule); }
    public static void Clear() => _rules.Clear();

    /// <summary>Apply every active rule to a freshly-built row (in display order).</summary>
    public static void Apply(FindNeedleUX.LogLine line)
    {
        if (_rules.Count == 0) return;
        foreach (var r in _rules) r.Apply(line);
    }
}
