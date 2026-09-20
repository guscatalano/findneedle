using System;
using System.Collections.Generic;
using System.Linq;
using FindNeedleUX.Services.PagedLogSource;

namespace FindNeedleUX.Services;

/// <summary>
/// Headless serializer for a filtered/sorted result set. Shared by the viewer's "Export…" command
/// (which writes the lines to a user-picked file) and the MCP <c>export</c> tool (which writes to a
/// path directly). Streams rows from the paged source via <see cref="IPagedLogSource.WalkAllFiltered"/>
/// so the whole set is never materialized at once.
/// </summary>
public static class ResultExporter
{
    public enum Format { Csv, Json, Xml }

    public static (string ext, string label) FormatInfo(Format format) => format switch
    {
        Format.Json => (".json", "JSON Files"),
        Format.Xml  => (".xml",  "XML Files"),
        _           => (".csv",  "CSV Files"),
    };

    /// <summary>
    /// Build the file's lines for the given filter/sort over the visible <paramref name="columns"/>.
    /// <paramref name="rowCount"/> returns the number of data rows emitted (excludes header/footer).
    /// </summary>
    public static List<string> BuildLines(IPagedLogSource source, FilterSpec filters, SortSpec sort,
        IReadOnlyList<string> columns, Format format, out int rowCount,
        IReadOnlyDictionary<long, (string Name, string Note)> tags = null)
    {
        var visible = columns?.Count > 0 ? columns : FindNeedleUX.Pages.NativeResultViewer.NativeResultsPageViewModel.DefaultColumnNames;
        // Tags are the user's own annotations - an export that drops them loses the only part of the
        // file they wrote themselves. When any row is tagged, two more columns ride along; when none
        // is, the export is byte-for-byte what it always was.
        tags ??= CurrentTags();
        bool withTags = tags is { Count: > 0 };
        if (withTags) visible = visible.Concat(new[] { TagColumn, TagNoteColumn }).ToList();
        var lines = new List<string>();
        int count = 0;

        switch (format)
        {
            case Format.Csv:
                lines.Add(string.Join(",", visible.Select(EscapeCsv)));
                source.WalkAllFiltered(filters, sort, line =>
                {
                    lines.Add(string.Join(",", visible.Select(name => EscapeCsv(Field(line, name, tags)))));
                    count++;
                });
                break;

            case Format.Json:
                lines.Add("[");
                bool first = true;
                var jsonOpts = new System.Text.Json.JsonSerializerOptions { WriteIndented = false };
                source.WalkAllFiltered(filters, sort, line =>
                {
                    var dict = new Dictionary<string, object>(visible.Count);
                    foreach (var name in visible) dict[name] = Field(line, name, tags) ?? "";
                    var entry = System.Text.Json.JsonSerializer.Serialize(dict, jsonOpts);
                    lines.Add(first ? "  " + entry : ", " + entry);
                    first = false;
                    count++;
                });
                lines.Add("]");
                break;

            case Format.Xml:
                lines.Add("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                lines.Add("<rows>");
                var sb = new System.Text.StringBuilder(256);
                source.WalkAllFiltered(filters, sort, line =>
                {
                    sb.Clear();
                    sb.Append("  <row>");
                    foreach (var name in visible)
                    {
                        var val = Field(line, name, tags) ?? "";
                        var tag = XmlName(name);
                        sb.Append('<').Append(tag).Append('>');
                        sb.Append(System.Security.SecurityElement.Escape(val));
                        sb.Append("</").Append(tag).Append('>');
                    }
                    sb.Append("</row>");
                    lines.Add(sb.ToString());
                    count++;
                });
                lines.Add("</rows>");
                break;
        }

        rowCount = count;
        return lines;
    }

    /// <summary>One tagged row, as the timeline needs it.</summary>
    public readonly record struct TaggedRow(DateTime Time, string Tag, string Note, string Level,
                                            string Provider, string TaskName, string Source, string Message);

    /// <summary>
    /// The tagged rows as a Markdown timeline: the rows in time order, each with how long after the
    /// first one it happened. This is the shape of a trace writeup - "at +00:02.140 the driver reset" -
    /// so a triage pass can be pasted into a bug or a review without retyping it from the grid.
    /// Pure: give it rows, get lines. Rows need not be sorted.
    /// </summary>
    public static List<string> BuildTagTimeline(IEnumerable<TaggedRow> rows, string title = null)
    {
        var ordered = (rows ?? Enumerable.Empty<TaggedRow>()).OrderBy(r => r.Time).ToList();
        var lines = new List<string> { "# " + (string.IsNullOrWhiteSpace(title) ? "Tagged rows" : title.Trim()), "" };
        if (ordered.Count == 0)
        {
            lines.Add("_No tagged rows._");
            return lines;
        }

        var first = ordered[0].Time;
        var span = ordered[^1].Time - first;
        var tagNames = ordered.Select(r => string.IsNullOrWhiteSpace(r.Tag) ? "(none)" : r.Tag)
                              .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
                              .OrderByDescending(g => g.Count())
                              .Select(g => $"{g.Key} x{g.Count()}");
        lines.Add($"{ordered.Count} tagged row{(ordered.Count == 1 ? "" : "s")} spanning {Humanize(span)}, "
                  + $"from {first:yyyy-MM-dd HH:mm:ss.fff} to {ordered[^1].Time:HH:mm:ss.fff}.");
        lines.Add("");
        lines.Add("Tags: " + string.Join(", ", tagNames));
        lines.Add("");

        foreach (var r in ordered)
        {
            var offset = r.Time - first;
            var tag = string.IsNullOrWhiteSpace(r.Tag) ? "(none)" : r.Tag;
            lines.Add($"## +{Offset(offset)} - {tag}");
            lines.Add("");
            if (!string.IsNullOrWhiteSpace(r.Note)) { lines.Add(r.Note.Trim()); lines.Add(""); }
            var where = string.Join(" / ", new[] { r.Provider, r.TaskName }.Where(x => !string.IsNullOrWhiteSpace(x)));
            var facts = new List<string> { r.Time.ToString("HH:mm:ss.fffffff") };
            if (!string.IsNullOrWhiteSpace(r.Level)) facts.Add(r.Level);
            if (!string.IsNullOrWhiteSpace(where)) facts.Add(where);
            if (!string.IsNullOrWhiteSpace(r.Source)) facts.Add(System.IO.Path.GetFileName(r.Source));
            lines.Add("`" + string.Join(" | ", facts) + "`");
            lines.Add("");
            foreach (var messageLine in SplitLines(r.Message))
                lines.Add("> " + messageLine);
            lines.Add("");
        }
        return lines;
    }

    private static IEnumerable<string> SplitLines(string text) =>
        (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    /// <summary>Elapsed-since-the-first-row, at the precision the gap deserves.</summary>
    private static string Offset(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss\.fff") : t.ToString(@"mm\:ss\.fff");

    private static string Humanize(TimeSpan t) =>
        t.TotalSeconds < 1 ? $"{t.TotalMilliseconds:0} ms"
        : t.TotalMinutes < 1 ? $"{t.TotalSeconds:0.0} s"
        : t.TotalHours < 1 ? $"{t.TotalMinutes:0.0} min"
        : $"{t.TotalHours:0.0} h";

    public const string TagColumn = "Tag";
    public const string TagNoteColumn = "Tag note";

    /// <summary>The viewer's live tags, when a viewer is open. The same seam the query language's
    /// <c>tag</c> field reads, so an export and a <c>tag == "Cause"</c> search always agree.</summary>
    internal static IReadOnlyDictionary<long, (string Name, string Note)> CurrentTags()
    {
        try { return FindPluginCore.Searching.Query.LogQuery.TagSnapshot?.Invoke(); }
        catch { return null; }
    }

    private static string Field(FindNeedleUX.LogLine line, string columnName,
                               IReadOnlyDictionary<long, (string Name, string Note)> tags)
    {
        if (columnName == TagColumn || columnName == TagNoteColumn)
        {
            if (tags == null || !tags.TryGetValue(line.RowId, out var t)) return "";
            return columnName == TagColumn ? t.Name ?? "" : t.Note ?? "";
        }
        return GetField(line, columnName);
    }

    /// <summary>XML element names can't contain a space, so fold it away in camel case
    /// ("Tag note" -> "TagNote").</summary>
    private static string XmlName(string columnName)
    {
        if (string.IsNullOrEmpty(columnName) || !columnName.Contains(' ')) return columnName;
        var parts = columnName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
    }

    public static string GetField(FindNeedleUX.LogLine line, string columnName) => columnName switch
    {
        "Index"    => line.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "Time"     => line.Time,
        "Provider" => line.Provider,
        "TaskName" => line.TaskName,
        "Message"  => line.Message,
        "Source"   => line.Source,
        "Level"    => line.Level,
        _          => ""
    };

    public static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
