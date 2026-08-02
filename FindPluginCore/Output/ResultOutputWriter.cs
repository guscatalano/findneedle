using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FindNeedlePluginLib;
using FindNeedlePluginLib.Interfaces;

namespace FindPluginCore.Output;

/// <summary>
/// Serializes decoded search results to a chosen text format for the CLI's opt-in <c>--out=&lt;format&gt;</c>
/// option: <c>csv</c>, <c>json</c>, <c>txt</c> (tracefmt-style lines), or <c>html</c> (a styled table).
/// Standalone — no RuleDSL rules needed — so <c>findneedle &lt;etl&gt; --out=json</c> just works. All formats
/// share one column set (the trace-oriented fields most rows carry) so they stay consistent.
/// </summary>
public static class ResultOutputWriter
{
    public static readonly IReadOnlyList<string> SupportedFormats = new[] { "csv", "json", "txt", "html" };

    // (header, value selector). Shared by every format. Message stays last (it's the long free-text field).
    private static readonly (string name, Func<ISearchResult, string> get)[] Columns =
    {
        ("Time",    r => r.GetLogTime().ToString("o")),
        ("Level",   r => r.GetLevel().ToString()),
        ("PID",     r => r.GetProcessId()),
        ("TID",     r => r.GetThreadId()),
        ("Source",  r => r.GetSource()),
        ("Task",    r => r.GetTaskName()),
        ("OpCode",  r => r.GetOpCode()),
        ("Message", r => r.GetMessage()),
    };

    private static string Norm(string f) => (f ?? string.Empty).Trim().ToLowerInvariant();
    private static string S(string v) => v ?? string.Empty;

    public static bool IsSupported(string format) => SupportedFormats.Contains(Norm(format));

    public static string ExtensionFor(string format) => "." + Norm(format);

    /// <summary>Serialize <paramref name="rows"/> to <paramref name="format"/>. Throws on an unknown format.</summary>
    public static string Serialize(IReadOnlyList<ISearchResult> rows, string format) => Norm(format) switch
    {
        "csv" => ToCsv(rows),
        "json" => ToJson(rows),
        "txt" => ToTxt(rows),
        "html" => ToHtml(rows),
        _ => throw new ArgumentException(
            $"Unsupported --out format '{format}'. Use one of: {string.Join(", ", SupportedFormats)}"),
    };

    /// <summary>Serialize and write to <paramref name="path"/> (creating parent dirs). Returns the path.</summary>
    public static string WriteToFile(IReadOnlyList<ISearchResult> rows, string format, string path)
    {
        var content = Serialize(rows, format);
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    public static string ToCsv(IReadOnlyList<ISearchResult> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", Columns.Select(c => EscapeCsv(c.name))));
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", Columns.Select(c => EscapeCsv(S(c.get(r))))));
        return sb.ToString();
    }

    public static string ToJson(IReadOnlyList<ISearchResult> rows)
    {
        var items = rows.Select(r =>
        {
            var d = new Dictionary<string, string>(Columns.Length);
            foreach (var c in Columns) d[c.name] = S(c.get(r));
            return d;
        }).ToList();
        return JsonSerializer.Serialize(items, new JsonSerializerOptions
        {
            WriteIndented = true,
            // Keep CJK / punctuation readable rather than \uXXXX-escaped.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    public static string ToTxt(IReadOnlyList<ISearchResult> rows)
    {
        var sb = new StringBuilder();
        foreach (var r in rows)
        {
            var pid = S(r.GetProcessId());
            var tid = S(r.GetThreadId());
            var ids = (pid.Length > 0 || tid.Length > 0) ? $"[{pid}.{tid}]  " : "";
            sb.AppendLine($"{r.GetLogTime():yyyy-MM-dd HH:mm:ss.fff}  {r.GetLevel(),-8}  {ids}{S(r.GetSource())}  {S(r.GetMessage())}");
        }
        return sb.ToString();
    }

    public static string ToHtml(IReadOnlyList<ISearchResult> rows)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("<title>FindNeedle results</title><style>");
        sb.Append(":root{color-scheme:light dark}");
        sb.Append("body{font:13px/1.4 -apple-system,Segoe UI,Roboto,sans-serif;margin:1rem;background:#fff;color:#111}");
        sb.Append("@media(prefers-color-scheme:dark){body{background:#0e0a1c;color:#e6e6ea}}");
        sb.Append("h1{font-size:1.1rem;font-weight:600}");
        sb.Append("table{border-collapse:collapse;width:100%;font-size:12px}");
        sb.Append("th,td{border-bottom:1px solid #8883;padding:3px 8px;text-align:left;vertical-align:top}");
        sb.Append("th{position:sticky;top:0;background:#f4f4f7;font-weight:600}");
        sb.Append("@media(prefers-color-scheme:dark){th{background:#1c1830}}");
        sb.Append("td:last-child{font-family:ui-monospace,Consolas,monospace;white-space:pre-wrap;word-break:break-word}");
        sb.Append("tr.Error td,tr.Catastrophic td{color:#d33}tr.Warning td{color:#c80}");
        sb.Append("</style></head><body>");
        sb.Append($"<h1>FindNeedle — {rows.Count:N0} rows</h1>");
        sb.Append("<table><thead><tr>");
        foreach (var c in Columns) sb.Append($"<th>{HtmlEnc(c.name)}</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var r in rows)
        {
            sb.Append($"<tr class=\"{HtmlEnc(r.GetLevel().ToString())}\">");
            foreach (var c in Columns) sb.Append($"<td>{HtmlEnc(S(c.get(r)))}</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table></body></html>");
        return sb.ToString();
    }

    private static string EscapeCsv(string field)
    {
        field ??= string.Empty;
        if (field.IndexOfAny(new[] { '"', ',', '\n', '\r' }) >= 0)
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        return field;
    }

    private static string HtmlEnc(string s) => System.Net.WebUtility.HtmlEncode(s ?? string.Empty);
}
