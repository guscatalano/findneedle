using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FindNeedleUmlDsl;

/// <summary>
/// Builds a Mermaid <c>sequenceDiagram</c> directly from an ordered set of log events — WITHOUT a
/// hand-written RuleDSL file. Powers the viewer's "Diagram selected rows" action: the user selects rows,
/// and the causal chain they'd reconstruct by hand (A→B at X, B→C at Y, …) falls straight out of the
/// event order. Each event is one actor doing something at a time; consecutive events become arrows
/// between their actors, so the diagram reads top-to-bottom in time.
/// </summary>
public static class SequenceDiagramBuilder
{
    /// <summary>One selected event: who (participant/actor), what (label), and when.</summary>
    public sealed record Interaction(string Participant, string Label, DateTime Time);

    /// <summary>
    /// Produce Mermaid sequence-diagram text from the events. Events are time-ordered; the first becomes a
    /// note on its actor, each subsequent event an arrow from the previous actor to this one (a self-arrow
    /// when the actor repeats). Consecutive identical events collapse with an <c>(×N)</c> count. Capped at
    /// <paramref name="maxArrows"/> to stay within renderer limits (a truncation note is added).
    /// Returns null when there's nothing to draw.
    /// </summary>
    public static string BuildMermaid(IEnumerable<Interaction> events, int maxArrows = 200)
    {
        if (events == null) return null;
        var ordered = events
            .Where(e => e != null && !string.IsNullOrWhiteSpace(e.Participant))
            .OrderBy(e => e.Time)
            .ToList();
        if (ordered.Count == 0) return null;

        // Stable alias per participant (Mermaid needs an id for names with spaces/special chars),
        // assigned in first-appearance order so the lifelines line up left→right with time.
        var alias = new Dictionary<string, string>(StringComparer.Ordinal);
        var displayOrder = new List<string>();
        foreach (var e in ordered)
        {
            var name = SanitizeName(e.Participant);
            if (!alias.ContainsKey(name)) { alias[name] = "p" + alias.Count; displayOrder.Add(name); }
        }

        var sb = new StringBuilder();
        sb.AppendLine("sequenceDiagram");
        foreach (var name in displayOrder)
            sb.AppendLine($"    participant {alias[name]} as {name}");

        int arrows = 0;
        string prevAlias = null;
        // Coalesce runs of the identical (participant,label) into one line with a count.
        for (int i = 0; i < ordered.Count;)
        {
            var e = ordered[i];
            var name = SanitizeName(e.Participant);
            var a = alias[name];
            var label = SanitizeLabel(e.Label);
            var time = e.Time.ToString("HH:mm:ss.fff");

            int run = 1;
            while (i + run < ordered.Count
                   && SanitizeName(ordered[i + run].Participant) == name
                   && SanitizeLabel(ordered[i + run].Label) == label)
                run++;
            var countSuffix = run > 1 ? $" (×{run})" : "";
            var text = $"{time} {label}{countSuffix}";

            if (prevAlias == null)
                sb.AppendLine($"    Note over {a}: {text}");
            else
                sb.AppendLine($"    {prevAlias}->>{a}: {text}");

            prevAlias = a;
            i += run;
            if (++arrows >= maxArrows && i < ordered.Count)
            {
                sb.AppendLine($"    Note over {a}: … {ordered.Count - i:N0} more events (truncated)");
                break;
            }
        }
        return sb.ToString();
    }

    // Participant display name: keep it readable but strip anything that would break Mermaid's parser
    // (newlines, statement separators). Names like ".NET Runtime" / "Microsoft-Windows-Kernel" survive.
    private static string SanitizeName(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "unknown";
        var cleaned = new string(s.Select(c =>
            char.IsLetterOrDigit(c) || " ._/-()#@".IndexOf(c) >= 0 ? c : ' ').ToArray());
        cleaned = string.Join(" ", cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length == 0) return "unknown";
        return cleaned.Length > 40 ? cleaned.Substring(0, 39) + "…" : cleaned;
    }

    // Arrow/note label: single line, no separators, trimmed to a readable length.
    private static string SanitizeLabel(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "(event)";
        var oneLine = s.Replace('\r', ' ').Replace('\n', ' ').Replace(';', ',').Replace('#', ' ');
        oneLine = string.Join(" ", oneLine.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (oneLine.Length == 0) return "(event)";
        return oneLine.Length > 80 ? oneLine.Substring(0, 79) + "…" : oneLine;
    }
}
