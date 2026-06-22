using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FindNeedlePluginLib;

namespace FindNeedlePluginUtils.StructuredLog;

/// <summary>
/// Maps a structured log row (an ordered set of name→value pairs from a CSV column set or a JSON
/// object) onto a <see cref="StructuredLogResult"/>. Well-known field names (case-insensitive, with
/// common aliases incl. Serilog CLEF "@t"/"@l"/"@m") become real columns; everything else is kept in
/// the structured-data JSON and the searchable text. Shared by the CSV and JSON plugins.
/// </summary>
public static class StructuredLogFieldMapper
{
    // alias (lowercased) → canonical field. First matching alias wins for a given canonical field.
    private static readonly Dictionary<string, string> Alias = new(StringComparer.OrdinalIgnoreCase)
    {
        ["time"] = "time", ["timestamp"] = "time", ["datetime"] = "time", ["date"] = "time",
        ["ts"] = "time", ["@t"] = "time", ["eventtime"] = "time", ["_time"] = "time",
        ["level"] = "level", ["severity"] = "level", ["loglevel"] = "level", ["lvl"] = "level", ["@l"] = "level",
        ["message"] = "message", ["msg"] = "message", ["text"] = "message", ["@m"] = "message",
        ["@mt"] = "message", ["messagetemplate"] = "message", ["description"] = "message",
        ["provider"] = "provider", ["logger"] = "provider", ["source"] = "provider",
        ["category"] = "provider", ["sourcecontext"] = "provider", ["component"] = "provider",
        ["taskname"] = "task", ["task"] = "task", ["operation"] = "task", ["activity"] = "task",
        ["processid"] = "pid", ["pid"] = "pid", ["process_id"] = "pid",
        ["threadid"] = "tid", ["tid"] = "tid", ["thread"] = "tid", ["thread_id"] = "tid",
        ["eventid"] = "eventid", ["event_id"] = "eventid", ["id"] = "eventid",
        ["machine"] = "machine", ["machinename"] = "machine", ["host"] = "machine",
        ["hostname"] = "machine", ["computer"] = "machine", ["computername"] = "machine",
        ["user"] = "user", ["username"] = "user", ["userid"] = "user",
        ["activityid"] = "activityid", ["correlationid"] = "activityid", ["traceid"] = "activityid",
    };

    public static StructuredLogResult Map(
        IReadOnlyList<KeyValuePair<string, string>> fields, string sourceFile, int lineNumber)
    {
        var r = new StructuredLogResult { SourceFile = sourceFile, LineNumber = lineNumber };
        var searchable = new StringBuilder();

        foreach (var kv in fields)
        {
            var value = kv.Value ?? string.Empty;
            if (searchable.Length > 0) searchable.Append(' ');
            searchable.Append(value);

            if (!Alias.TryGetValue(kv.Key ?? string.Empty, out var canon)) continue;
            switch (canon)
            {
                case "time":    if (r.LogTime == DateTime.MinValue) r.LogTime = ParseTime(value); break;
                case "level":   r.Level = ParseLevel(value); break;
                case "message": if (string.IsNullOrEmpty(r.Message)) r.Message = value; break;
                case "provider":if (string.IsNullOrEmpty(r.Provider)) r.Provider = value; break;
                case "task":    if (string.IsNullOrEmpty(r.TaskName)) r.TaskName = value; break;
                case "pid":     if (string.IsNullOrEmpty(r.ProcessId)) r.ProcessId = value; break;
                case "tid":     if (string.IsNullOrEmpty(r.ThreadId)) r.ThreadId = value; break;
                case "eventid": if (string.IsNullOrEmpty(r.EventId)) r.EventId = value; break;
                case "machine": if (string.IsNullOrEmpty(r.MachineName)) r.MachineName = value; break;
                case "user":    if (string.IsNullOrEmpty(r.Username)) r.Username = value; break;
                case "activityid": if (string.IsNullOrEmpty(r.ActivityId)) r.ActivityId = value; break;
            }
        }

        // No recognized message column → use the whole row text so the row isn't blank.
        if (string.IsNullOrEmpty(r.Message)) r.Message = searchable.ToString();
        r.SearchableData = searchable.ToString();
        r.StructuredDataJson = ToJson(fields);
        return r;
    }

    public static Level ParseLevel(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return Level.Info;
        s = s.Trim();
        // Serilog single-letter / numeric syslog levels handled alongside word forms.
        if (Eq(s, "Fatal") || Eq(s, "Critical") || Eq(s, "Crit") || Eq(s, "F") || s == "1" || s == "2")
            return Level.Catastrophic;
        if (Eq(s, "Error") || Eq(s, "Err") || Eq(s, "E") || s == "3") return Level.Error;
        if (Eq(s, "Warning") || Eq(s, "Warn") || Eq(s, "W") || s == "4") return Level.Warning;
        if (Eq(s, "Verbose") || Eq(s, "Debug") || Eq(s, "Trace") || Eq(s, "V") || Eq(s, "D") || s == "7")
            return Level.Verbose;
        if (Eq(s, "Information") || Eq(s, "Info") || Eq(s, "I") || s == "6") return Level.Info;
        return Level.Info;
    }

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public static DateTime ParseTime(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return DateTime.MinValue;
        // Epoch seconds / milliseconds.
        if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch))
        {
            try
            {
                return s.Length >= 12
                    ? DateTimeOffset.FromUnixTimeMilliseconds(epoch).LocalDateTime
                    : DateTimeOffset.FromUnixTimeSeconds(epoch).LocalDateTime;
            }
            catch { return DateTime.MinValue; }
        }
        return DateTime.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.NoCurrentDateDefault, out var dt) && dt.Year > 1
            ? dt : DateTime.MinValue;
    }

    private static string ToJson(IReadOnlyList<KeyValuePair<string, string>> fields)
    {
        var sb = new StringBuilder("{");
        for (int i = 0; i < fields.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(JsonStr(fields[i].Key)).Append(':').Append(JsonStr(fields[i].Value ?? string.Empty));
        }
        return sb.Append('}').ToString();
    }

    private static string JsonStr(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
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
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}
