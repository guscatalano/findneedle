using System;
using System.Collections.Generic;
using System.Linq;
using FindPluginCore.Searching.Query;

namespace FindNeedleUX.Pages.NativeResultViewer;

/// <summary>One completion offered under the search box: what the list shows, and the whole box text
/// it becomes when picked (the AutoSuggestBox writes <see cref="FullText"/> into the box).</summary>
public sealed record QuerySuggestion(string Display, string FullText)
{
    public override string ToString() => FullText;
}

/// <summary>
/// Completion for the search box's query language, computed from the text as typed (caret at the
/// end): a partial field name completes to a field, a field followed by a space offers the operators,
/// an operator on a known-value field (level, tag) offers its values, and a finished predicate offers
/// AND / OR / NOT. Pure, so it is unit-tested without the UI.
/// </summary>
public static class QuerySuggestions
{
    /// <summary>Field names offered for completion, with a one-line meaning. Canonical names first, then
    /// the short aliases people actually type.</summary>
    private static readonly (string Name, string Hint)[] FieldHints =
    {
        ("msg", "message text"), ("level", "Catastrophic … Verbose"), ("provider", "provider / source name"),
        ("taskname", "task / event name"), ("time", "timestamp; time ~ 12:34:56 ±2s"), ("pid", "process id"),
        ("tid", "thread id"), ("aid", "activity id"), ("raid", "related activity id"), ("eventid", "event id"),
        ("channel", "channel / log"), ("source", "file the row came from"), ("rawlevel", "level as the source recorded it"),
        ("tag", "your tag: Important, Question, Resolved, Note"), ("data.", "a structured-payload field, e.g. data.ProcessId"),
        ("machine", "machine name"), ("user", "user name"), ("opcode", "opcode"),
    };

    private static readonly string[] LevelValues = { "Catastrophic", "Error", "Warning", "Info", "Verbose" };
    private static readonly string[] TagValues = { "Important", "Question", "Resolved", "Note" };
    private static readonly string[] Keywords = { "AND", "OR", "NOT" };

    public const int Max = 8;

    /// <summary>Suggestions for <paramref name="text"/> as typed so far (caret at the end). Empty when
    /// there is nothing useful to offer - a plain substring search gets no popup.</summary>
    public static IReadOnlyList<QuerySuggestion> For(string text)
    {
        text ??= "";
        if (text.Length == 0) return Array.Empty<QuerySuggestion>();
        if (text.IndexOf('"') >= 0 && text.Count(c => c == '"') % 2 == 1) return Array.Empty<QuerySuggestion>(); // inside a string

        // Split into the settled part and the word being typed.
        int lastSpace = text.LastIndexOf(' ');
        string head = lastSpace < 0 ? "" : text.Substring(0, lastSpace + 1);
        string tail = lastSpace < 0 ? text : text.Substring(lastSpace + 1);
        var settled = head.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string prev = settled.Length > 0 ? settled[^1] : "";
        string prev2 = settled.Length > 1 ? settled[^2] : "";

        var list = new List<QuerySuggestion>();

        // After a field: the operators.
        if (tail.Length == 0 && IsFieldWord(prev) && !IsOperator(prev2))
        {
            foreach (var (op, meaning) in LogQuery.Operators)
                list.Add(new QuerySuggestion($"{op}   {meaning}", head + op + " "));
            return list;
        }

        // Typing an operator right after a field (e.g. "level =").
        if (tail.Length > 0 && IsOperatorPrefix(tail) && IsFieldWord(prev))
        {
            foreach (var (op, meaning) in LogQuery.Operators)
                if (op.StartsWith(tail, StringComparison.Ordinal))
                    list.Add(new QuerySuggestion($"{op}   {meaning}", head + op + " "));
            return Cap(list);
        }

        // After "field op": the values a known-value field takes.
        if (IsOperator(prev) && IsFieldWord(prev2))
        {
            var canonical = LogQuery.Canonical(prev2);
            string[] values = canonical == "level" ? LevelValues : canonical == "tag" ? TagValues : null;
            if (values != null)
            {
                foreach (var v in values)
                    if (v.StartsWith(tail, StringComparison.OrdinalIgnoreCase))
                        list.Add(new QuerySuggestion(v, head + v + " "));
            }
            return Cap(list);
        }

        // After a finished predicate (value just typed, then a space): the connectives.
        if (tail.Length == 0 && settled.Length >= 3 && IsOperator(settled[^2]) && !IsOperator(prev))
        {
            foreach (var kw in Keywords) list.Add(new QuerySuggestion(kw, head + kw + " "));
            return list;
        }

        // A partial word that is not a value: a field name (or a connective when one fits).
        if (tail.Length > 0 && !IsOperator(prev))
        {
            foreach (var (name, hint) in FieldHints)
                if (name.StartsWith(tail, StringComparison.OrdinalIgnoreCase) && !name.Equals(tail, StringComparison.OrdinalIgnoreCase))
                    list.Add(new QuerySuggestion($"{name}   {hint}", head + name + (name.EndsWith(".") ? "" : " ")));
            if (settled.Length > 0)
                foreach (var kw in Keywords)
                    if (kw.StartsWith(tail, StringComparison.OrdinalIgnoreCase) && !kw.Equals(tail, StringComparison.OrdinalIgnoreCase))
                        list.Add(new QuerySuggestion(kw, head + kw + " "));
        }
        return Cap(list);
    }

    private static IReadOnlyList<QuerySuggestion> Cap(List<QuerySuggestion> list)
        => list.Count <= Max ? list : list.GetRange(0, Max);

    private static bool IsFieldWord(string w) => !string.IsNullOrEmpty(w) && LogQuery.IsField(w.TrimStart('('));
    private static bool IsOperator(string w) => !string.IsNullOrEmpty(w) && LogQuery.Operators.Any(o => o.Op == w);
    private static bool IsOperatorPrefix(string w) => !string.IsNullOrEmpty(w) && LogQuery.Operators.Any(o => o.Op.StartsWith(w, StringComparison.Ordinal));
}
