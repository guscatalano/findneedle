using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FindNeedlePluginLib;

namespace GithubPlugin.Location;

/// <summary>
/// One GitHub issue as a search result. The flattened issue fields map onto the common result fields;
/// the full set is exposed as StructuredData for the details panel's "Event data" section.
/// </summary>
public class GithubIssueResult : ISearchResult
{
    private readonly Dictionary<string, string> _fields;
    private readonly string _resultSource;
    private readonly DateTime _time;

    public GithubIssueResult(Dictionary<string, string> fields, string resultSource)
    {
        _fields = fields ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _resultSource = resultSource ?? "GitHub";
        _time = ParseTime(Get("updated_at"));
        if (_time == DateTime.MinValue) _time = ParseTime(Get("created_at"));
    }

    private string Get(string key) => _fields.TryGetValue(key, out var v) ? v ?? "" : "";

    private static DateTime ParseTime(string s)
        => DateTime.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal, out var t) ? t : DateTime.MinValue;

    public DateTime GetLogTime() => _time;
    public string GetMachineName() => "";
    public void WriteToConsole() => Console.WriteLine(GetMessage());

    public Level GetLevel()
    {
        if (Get("state").Equals("closed", StringComparison.OrdinalIgnoreCase)) return Level.Verbose;
        var labels = Get("labels").ToLowerInvariant();
        if (labels.Contains("bug")) return Level.Warning;
        return Level.Info;
    }

    public string GetUsername() => Get("user");
    public string GetTaskName() => Get("state");
    public string GetOpCode() => Get("labels");
    public string GetSource() => "Issue";

    public string GetMessage()
    {
        var title = Get("title");
        var num = Get("number");
        return string.IsNullOrEmpty(title) ? $"Issue #{num}" : $"#{num} {title}";
    }

    public string GetSearchableData() => string.Join(" | ", _fields.Select(kv => $"{kv.Key}={kv.Value}"));
    public string GetResultSource() => _resultSource;
    public string GetEventId() => Get("number");

    public string GetStructuredData()
    {
        if (_fields.Count == 0) return "";
        try { return System.Text.Json.JsonSerializer.Serialize(_fields); } catch { return ""; }
    }
}
