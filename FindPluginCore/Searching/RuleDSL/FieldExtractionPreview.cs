using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FindNeedlePluginLib;
using findneedle.RuleDSL;

namespace FindPluginCore.Searching.RuleDSL;

/// <summary>One previewed row: the original message, the message after strip rules, and the columns
/// the extract rules populated.</summary>
public sealed class FieldExtractionPreviewRow
{
    public string Before { get; set; } = string.Empty;
    public string After { get; set; } = string.Empty;
    public Dictionary<string, string> Fields { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Runs a field-extraction (enrichment) rule file against sample messages so the UI can show, live,
/// exactly what an "extract" rule does: which columns it fills and how it rewrites the message. Uses
/// the real <see cref="RuleEvaluationEngine"/> + the same strip logic as the scan, so the preview
/// matches what a search would produce.
/// </summary>
public static class FieldExtractionPreview
{
    /// <summary>Preview using rule JSON text (e.g. an editor buffer).</summary>
    public static List<FieldExtractionPreviewRow> Run(string ruleJson, IEnumerable<string> sampleMessages)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"fnfields_{Guid.NewGuid():N}.rules.json");
        File.WriteAllText(tmp, ruleJson ?? string.Empty);
        try { return RunFromFile(tmp, sampleMessages); }
        finally { try { File.Delete(tmp); } catch { /* ignore */ } }
    }

    /// <summary>Preview using a rule file on disk.</summary>
    public static List<FieldExtractionPreviewRow> RunFromFile(string rulePath, IEnumerable<string> sampleMessages)
    {
        var rows = new List<FieldExtractionPreviewRow>();
        var loader = new RuleLoader();
        // LoadRulesFromPaths normalizes to the { sections: [...] } shape GetSectionsByPurpose reads
        // (the same path the live scan uses); LoadRulesFromFile returns a raw dict it can't traverse.
        dynamic? ruleSet = loader.LoadRulesFromPaths(new[] { rulePath });
        var sections = loader.GetSectionsByPurpose(ruleSet, "enrichment");
        var engine = new RuleEvaluationEngine();

        foreach (var msg in sampleMessages ?? Enumerable.Empty<string>())
        {
            var result = new PreviewResult(msg ?? string.Empty);
            var row = new FieldExtractionPreviewRow { Before = msg ?? string.Empty };
            var strips = new List<(int Start, int Length)>();
            foreach (var section in sections)
            {
                var eval = engine.EvaluateRules(result, section);
                foreach (var kv in eval.Fields) row.Fields[kv.Key] = kv.Value;
                strips.AddRange(eval.MessageStrips);
            }
            row.After = ApplyStrips(row.Before, strips);
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>Remove the matched spans (merging overlaps) — mirrors NuSearchQuery.ApplyMessageStrips.</summary>
    private static string ApplyStrips(string message, List<(int Start, int Length)> strips)
    {
        if (string.IsNullOrEmpty(message) || strips.Count == 0) return message;
        var valid = strips
            .Where(s => s.Start >= 0 && s.Length > 0 && s.Start + s.Length <= message.Length)
            .OrderBy(s => s.Start).ToList();
        if (valid.Count == 0) return message;

        var merged = new List<(int Start, int End)>();
        foreach (var (st, len) in valid)
        {
            int end = st + len;
            if (merged.Count > 0 && st <= merged[^1].End)
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, end));
            else
                merged.Add((st, end));
        }

        var sb = new StringBuilder(message.Length);
        int pos = 0;
        foreach (var (st, end) in merged)
        {
            if (st > pos) sb.Append(message, pos, st - pos);
            pos = end;
        }
        if (pos < message.Length) sb.Append(message, pos, message.Length - pos);
        // Collapse the whitespace left where text was removed, like the viewer does on display.
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s{2,}", " ").Trim();
    }

    /// <summary>Minimal result whose only meaningful field is the message under test.</summary>
    private sealed class PreviewResult : ISearchResult
    {
        private readonly string _msg;
        public PreviewResult(string msg) { _msg = msg; }
        public string GetMessage() => _msg;
        public string GetSearchableData() => _msg;
        public DateTime GetLogTime() => DateTime.MinValue;
        public Level GetLevel() => Level.Info;
        public string GetMachineName() => string.Empty;
        public string GetUsername() => string.Empty;
        public string GetTaskName() => string.Empty;
        public string GetOpCode() => string.Empty;
        public string GetSource() => string.Empty;
        public string GetResultSource() => string.Empty;
        public void WriteToConsole() { }
    }
}
