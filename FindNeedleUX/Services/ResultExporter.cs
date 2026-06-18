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
        IReadOnlyList<string> columns, Format format, out int rowCount)
    {
        var visible = columns?.Count > 0 ? columns : FindNeedleUX.Pages.NativeResultViewer.NativeResultsPageViewModel.DefaultColumnNames;
        var lines = new List<string>();
        int count = 0;

        switch (format)
        {
            case Format.Csv:
                lines.Add(string.Join(",", visible.Select(EscapeCsv)));
                source.WalkAllFiltered(filters, sort, line =>
                {
                    lines.Add(string.Join(",", visible.Select(name => EscapeCsv(GetField(line, name)))));
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
                    foreach (var name in visible) dict[name] = GetField(line, name) ?? "";
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
                        var val = GetField(line, name) ?? "";
                        sb.Append('<').Append(name).Append('>');
                        sb.Append(System.Security.SecurityElement.Escape(val));
                        sb.Append("</").Append(name).Append('>');
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
