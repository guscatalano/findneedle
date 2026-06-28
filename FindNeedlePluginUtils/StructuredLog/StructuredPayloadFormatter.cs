using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace FindNeedlePluginUtils.StructuredLog;

/// <summary>How a structured event's payload (name→value fields, e.g. an ETW TraceLogging event) is
/// rendered into the displayed message. The payload itself is kept as JSON in StructuredData, so this
/// is purely a rendering choice and can be switched at display time without re-parsing.</summary>
public enum PayloadFormat
{
    /// <summary>PerfView style: <c>name="value" name2="value2"</c> (the de-facto ETW standard).</summary>
    KeyValueQuoted,
    /// <summary>logfmt: <c>name=value name2="value with space"</c> (quoted only when needed).</summary>
    KeyValue,
    /// <summary>Compact JSON object: <c>{"name":"value",...}</c>.</summary>
    Json,
    /// <summary>Legacy FindNeedle style: <c>name: value | name2: value2 | </c>.</summary>
    Pipe,
    /// <summary>User template applied per field with <c>{name}</c>/<c>{value}</c> tokens, concatenated
    /// (include any separator in the template, e.g. <c>{name}="{value}" </c>).</summary>
    Custom,
}

/// <summary>Renders a payload (a JSON object of name→value pairs, as stored in StructuredData) into a
/// message string in the selected <see cref="PayloadFormat"/>. Shared by the parse path and the viewer
/// so the same field set can be displayed in different formats.</summary>
public static class StructuredPayloadFormatter
{
    public const string DefaultCustomTemplate = "{name}=\"{value}\" ";

    /// <summary>Render the fields from <paramref name="structuredJson"/> (a JSON object) in the given
    /// format. Returns "" when the input isn't a non-empty JSON object.</summary>
    public static string Render(string? structuredJson, PayloadFormat format, string? customTemplate = null)
    {
        var fields = Parse(structuredJson);
        if (fields.Count == 0) return string.Empty;
        return Render(fields, format, customTemplate);
    }

    /// <summary>Re-render the payload portion of a message that was originally rendered with
    /// <paramref name="from"/>, into <paramref name="to"/>. Only rewrites when the message actually
    /// ends with the <paramref name="from"/>-render of <paramref name="structuredJson"/> — so a row
    /// without that payload (or rendered some other way) is returned unchanged. Lets the viewer switch
    /// ETW payload formats at display time without re-parsing the file.</summary>
    public static string Reformat(string message, string? structuredJson, PayloadFormat from, PayloadFormat to, string? customTemplate = null)
    {
        if (from == to || string.IsNullOrEmpty(message) || string.IsNullOrWhiteSpace(structuredJson)) return message;
        var fromRender = Render(structuredJson, from);
        if (string.IsNullOrEmpty(fromRender) || !message.EndsWith(fromRender, StringComparison.Ordinal)) return message;
        var toRender = Render(structuredJson, to, customTemplate);
        return message.Substring(0, message.Length - fromRender.Length) + toRender;
    }

    public static string Render(IEnumerable<KeyValuePair<string, string>> fields, PayloadFormat format, string? customTemplate = null)
    {
        switch (format)
        {
            case PayloadFormat.Json:
                return ToJson(fields);
            case PayloadFormat.Pipe:
            {
                var sb = new StringBuilder();
                foreach (var f in fields) sb.Append(f.Key).Append(": ").Append(f.Value).Append(" | ");
                return sb.ToString();
            }
            case PayloadFormat.KeyValue:
            {
                var sb = new StringBuilder();
                foreach (var f in fields)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(f.Key).Append('=').Append(NeedsQuote(f.Value) ? Quote(f.Value) : f.Value);
                }
                return sb.ToString();
            }
            case PayloadFormat.Custom:
            {
                var tmpl = string.IsNullOrEmpty(customTemplate) ? DefaultCustomTemplate : customTemplate!;
                var sb = new StringBuilder();
                foreach (var f in fields)
                    sb.Append(tmpl.Replace("{name}", f.Key).Replace("{value}", f.Value));
                return sb.ToString();
            }
            case PayloadFormat.KeyValueQuoted:
            default:
            {
                var sb = new StringBuilder();
                foreach (var f in fields)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(f.Key).Append('=').Append(Quote(f.Value));
                }
                return sb.ToString();
            }
        }
    }

    private static List<KeyValuePair<string, string>> Parse(string? json)
    {
        var list = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return list;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                var v = p.Value.ValueKind == JsonValueKind.String
                    ? p.Value.GetString() ?? string.Empty
                    : p.Value.GetRawText();
                list.Add(new KeyValuePair<string, string>(p.Name, v));
            }
        }
        catch (JsonException) { /* not a JSON object → empty */ }
        return list;
    }

    private static bool NeedsQuote(string v) =>
        string.IsNullOrEmpty(v) || v.IndexOfAny(new[] { ' ', '\t', '"', '=', '|' }) >= 0;

    private static string Quote(string v) => "\"" + v.Replace("\"", "\\\"") + "\"";

    private static string ToJson(IEnumerable<KeyValuePair<string, string>> fields)
    {
        var sb = new StringBuilder("{");
        bool first = true;
        foreach (var f in fields)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(JsonStr(f.Key)).Append(':').Append(JsonStr(f.Value));
        }
        return sb.Append('}').ToString();
    }

    private static string JsonStr(string s)
    {
        var sb = new StringBuilder(s.Length + 2).Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}
