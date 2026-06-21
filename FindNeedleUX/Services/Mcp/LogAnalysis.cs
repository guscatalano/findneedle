using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FindNeedleUX.Services.PagedLogSource;

namespace FindNeedleUX.Services.Mcp;

/// <summary>
/// Read-only aggregations over a result set for the MCP <c>facets</c> and <c>top_patterns</c> tools —
/// the things an agent needs to summarize a huge log without paging through it. Backend-agnostic: it
/// reads the source in bounded batches (a sample cap) so it never materializes the whole set and stays
/// responsive on million-row logs. Counts beyond the cap are approximate, which the result flags.
/// </summary>
public static class LogAnalysis
{
    public sealed record Facet(string Value, int Count);
    public sealed record FacetResult(string Field, int Scanned, int Total, bool Truncated, List<Facet> Values);

    public sealed record Pattern(string Template, int Count, string Example);
    public sealed record PatternResult(int Scanned, int Total, bool Truncated, List<Pattern> Patterns);

    /// <summary>Which row field a facet groups by. Maps the MCP field name to a LogLine accessor.</summary>
    private static Func<LogLine, string> FieldGetter(string field) => (field ?? "").Trim().ToLowerInvariant() switch
    {
        "provider"    => l => l.Provider,
        "source"      => l => l.Source,
        "level"       => l => l.Level,
        "taskname"    => l => l.TaskName,
        "task"        => l => l.TaskName,
        "processname" => l => l.ProcessName,
        "processid"   => l => l.ProcessId,
        "pid"         => l => l.ProcessId,
        "channel"     => l => l.Channel,
        "eventid"     => l => l.EventId,
        "machinename" => l => l.MachineName,
        "username"    => l => l.Username,
        _             => null,
    };

    public static readonly string[] FacetableFields =
        { "provider", "source", "level", "taskName", "processName", "processId", "channel", "eventId", "machineName", "username" };

    /// <summary>Top distinct values (with counts) of one field over the filtered set, most-common first.</summary>
    public static FacetResult Facets(IPagedLogSource source, FilterSpec filters, string field, int limit, int sampleCap)
    {
        if (source == null) return new FacetResult(field, 0, 0, false, new());
        var get = FieldGetter(field);
        if (get == null)
            throw new ArgumentException($"Unknown facet field '{field}'. Try one of: {string.Join(", ", FacetableFields)}.");
        if (limit <= 0) limit = 20;
        if (sampleCap <= 0) sampleCap = 500_000;

        int total = source.GetFilteredCount(filters);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int scanned = Scan(source, filters, sampleCap, line =>
        {
            var v = get(line);
            v = string.IsNullOrEmpty(v) ? "(none)" : v;
            counts.TryGetValue(v, out var c);
            counts[v] = c + 1;
        });

        var values = counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Take(limit).Select(kv => new Facet(kv.Key, kv.Value)).ToList();
        return new FacetResult(field, scanned, total, scanned < total, values);
    }

    /// <summary>
    /// Cluster messages into templates and return the most common ones — the generic "what is this log
    /// mostly saying?" view. Each message is normalized (numbers, GUIDs, hex, paths, quoted strings,
    /// addresses → placeholders) so near-identical lines collapse into one template.
    /// </summary>
    public static PatternResult TopPatterns(IPagedLogSource source, FilterSpec filters, int limit, int sampleCap)
    {
        if (source == null) return new PatternResult(0, 0, false, new());
        if (limit <= 0) limit = 20;
        if (sampleCap <= 0) sampleCap = 200_000;

        int total = source.GetFilteredCount(filters);
        var counts = new Dictionary<string, int>();
        var examples = new Dictionary<string, string>();
        int scanned = Scan(source, filters, sampleCap, line =>
        {
            var msg = line.Message;
            if (string.IsNullOrWhiteSpace(msg)) return;
            var template = Normalize(msg);
            counts.TryGetValue(template, out var c);
            counts[template] = c + 1;
            if (c == 0) examples[template] = msg.Length > 300 ? msg.Substring(0, 300) + "…" : msg;
        });

        var patterns = counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(limit)
            .Select(kv => new Pattern(kv.Key, kv.Value, examples.TryGetValue(kv.Key, out var ex) ? ex : ""))
            .ToList();
        return new PatternResult(scanned, total, scanned < total, patterns);
    }

    /// <summary>Read the filtered set in batches up to <paramref name="cap"/>, invoking <paramref name="onRow"/>.
    /// Returns the number of rows actually scanned.</summary>
    private static int Scan(IPagedLogSource source, FilterSpec filters, int cap, Action<LogLine> onRow)
    {
        const int Batch = 5000;
        int scanned = 0;
        while (scanned < cap)
        {
            int want = Math.Min(Batch, cap - scanned);
            var rows = source.GetPage(filters, SortSpec.None, scanned, want);
            if (rows.Count == 0) break;
            foreach (var r in rows) onRow(r);
            scanned += rows.Count;
            if (rows.Count < want) break;
        }
        return scanned;
    }

    // ----- message normalization -----

    private static readonly Regex Guid = new(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", RegexOptions.Compiled);
    private static readonly Regex Hex = new(@"\b0x[0-9a-fA-F]+\b|\b[0-9a-fA-F]{16,}\b", RegexOptions.Compiled);
    private static readonly Regex WinPath = new(@"[A-Za-z]:\\[^\s""']+", RegexOptions.Compiled);
    private static readonly Regex Quoted = new("\"[^\"]*\"|'[^']*'", RegexOptions.Compiled);
    private static readonly Regex Number = new(@"\b\d+\b", RegexOptions.Compiled);
    private static readonly Regex Ws = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Collapse the variable parts of a message so structurally-identical lines share a template.</summary>
    public static string Normalize(string message)
    {
        var s = message;
        s = Guid.Replace(s, "{guid}");
        s = WinPath.Replace(s, "{path}");
        s = Quoted.Replace(s, "{str}");
        s = Hex.Replace(s, "{hex}");
        s = Number.Replace(s, "{n}");
        s = Ws.Replace(s, " ").Trim();
        if (s.Length > 200) s = s.Substring(0, 200) + "…";
        return s;
    }
}
