using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FindNeedlePluginLib;

namespace ADOPlugin.Location;

/// <summary>
/// One Azure DevOps work item as a search result. The work item's flattened fields (System.Id,
/// System.Title, …) map onto the common result fields; the full field set is exposed as
/// StructuredData so it shows in the details panel's "Event data" section.
/// </summary>
public class AdoWorkItemResult : ISearchResult
{
    private readonly Dictionary<string, string> _fields;   // "System.Title" -> value
    private readonly string _resultSource;
    private readonly DateTime _time;

    public AdoWorkItemResult(Dictionary<string, string> fields, string resultSource)
    {
        _fields = fields ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _resultSource = resultSource ?? "ADO";
        _time = ParseTime(Get("System.ChangedDate"));
        if (_time == DateTime.MinValue) _time = ParseTime(Get("System.CreatedDate"));
    }

    private string Get(string key) => _fields.TryGetValue(key, out var v) ? v ?? "" : "";

    private static DateTime ParseTime(string s)
        // ADO timestamps are ISO 8601 with an explicit offset/Z, so RoundtripKind handles them.
        // (RoundtripKind can't be combined with AssumeUniversal — that throws.)
        => DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t)
            ? t : DateTime.MinValue;

    public DateTime GetLogTime() => _time;
    public string GetMachineName() => "";
    public void WriteToConsole() => Console.WriteLine(GetMessage());

    public Level GetLevel()
    {
        // Work items have no log level; map a few common states/types so bugs/blocked items stand out.
        var type = Get("System.WorkItemType");
        var state = Get("System.State");
        if (state.Equals("Closed", StringComparison.OrdinalIgnoreCase)
            || state.Equals("Done", StringComparison.OrdinalIgnoreCase)
            || state.Equals("Resolved", StringComparison.OrdinalIgnoreCase))
            return Level.Verbose;
        if (type.Equals("Bug", StringComparison.OrdinalIgnoreCase)) return Level.Warning;
        return Level.Info;
    }

    public string GetUsername() => DisplayName(Get("System.ChangedBy"));
    public string GetTaskName() => Get("System.State");
    public string GetOpCode() => Get("System.WorkItemType");
    public string GetSource() => Get("System.WorkItemType");

    public string GetMessage()
    {
        var title = Get("System.Title");
        var id = Get("System.Id");
        return string.IsNullOrEmpty(title) ? $"Work item {id}" : title;
    }

    public string GetSearchableData()
        => string.Join(" | ", _fields.Select(kv => $"{kv.Key}={kv.Value}"));

    public string GetResultSource() => _resultSource;
    public string GetEventId() => Get("System.Id");
    public string GetProviderGuid() => "";

    public string GetStructuredData()
    {
        if (_fields.Count == 0) return "";
        // Short, readable keys (drop the System./Microsoft.VSTS. prefixes) for the details panel.
        var clean = new Dictionary<string, string>();
        foreach (var kv in _fields)
        {
            var key = kv.Key;
            int dot = key.LastIndexOf('.');
            if (dot >= 0 && dot < key.Length - 1) key = key.Substring(dot + 1);
            if (!clean.ContainsKey(key)) clean[key] = kv.Value ?? "";
        }
        try { return System.Text.Json.JsonSerializer.Serialize(clean); } catch { return ""; }
    }

    /// <summary>ADO identity fields come back as "Display Name &lt;email&gt;"; show just the name.</summary>
    private static string DisplayName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var lt = raw.IndexOf('<');
        return lt > 0 ? raw.Substring(0, lt).Trim() : raw.Trim();
    }
}
