using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FindNeedlePluginLib;

namespace KustoPlugin.Location;

/// <summary>
/// One Kusto result row mapped to <see cref="ISearchResult"/>. Kusto schemas vary, so we probe a
/// few common column names for time/message/level/etc., and the full row is preserved as the
/// searchable text so nothing is lost.
/// </summary>
public class KustoRowResult : ISearchResult
{
    private readonly Dictionary<string, string> _row;
    private readonly string _resultSource;
    private readonly DateTime _time;
    private readonly string _searchable;

    private static readonly string[] TimeCols   = { "PreciseTimeStamp", "TimeStamp", "Timestamp", "TimeGenerated", "EventTime", "ingestion_time()" };
    private static readonly string[] MsgCols     = { "Message", "EventMessage", "RenderedMessage", "Msg" };
    private static readonly string[] LevelCols   = { "Level", "Severity", "SeverityLevel", "LogLevel" };
    private static readonly string[] SourceCols  = { "ProviderName", "Provider", "Source", "SourceName" };
    private static readonly string[] TaskCols    = { "TaskName", "Task", "OperationName" };
    private static readonly string[] MachineCols = { "HostInstance", "Machine", "Computer", "RoleInstance", "Hostname", "Host" };
    private static readonly string[] UserCols    = { "User", "Username", "UserName", "Identity" };

    public KustoRowResult(Dictionary<string, string> row, string resultSource)
    {
        _row = row;
        _resultSource = resultSource;
        _time = ParseTime(First(TimeCols));
        _searchable = string.Join(" | ", row.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    private string First(string[] candidates)
    {
        foreach (var c in candidates)
            if (_row.TryGetValue(c, out var v) && !string.IsNullOrEmpty(v)) return v;
        return string.Empty;
    }

    private static DateTime ParseTime(string s)
        => DateTime.TryParse(s, CultureInfo.InvariantCulture,
                             DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)
            ? d : DateTime.MinValue;

    public DateTime GetLogTime() => _time;
    public string GetMachineName() => First(MachineCols);
    public void WriteToConsole() => Console.WriteLine(GetMessage());

    public Level GetLevel()
    {
        var raw = First(LevelCols).Trim();
        if (string.IsNullOrEmpty(raw)) return Level.Info;
        // Numeric ETW-style levels (1=Critical … 5=Verbose) and common text names.
        return raw.ToLowerInvariant() switch
        {
            "1" or "critical" or "fatal" or "catastrophic"    => Level.Catastrophic,
            "2" or "error" or "err"                           => Level.Error,
            "3" or "warning" or "warn"                        => Level.Warning,
            "4" or "info" or "information" or "informational" => Level.Info,
            "5" or "verbose" or "debug" or "trace"            => Level.Verbose,
            _ => Level.Info,
        };
    }

    public string GetUsername() => First(UserCols);
    public string GetTaskName() => First(TaskCols);
    public string GetOpCode() => string.Empty;
    public string GetSource() => First(SourceCols);
    public string GetSearchableData() => _searchable;

    public string GetMessage()
    {
        var m = First(MsgCols);
        return string.IsNullOrEmpty(m) ? _searchable : m;
    }

    public string GetResultSource() => _resultSource;
}
