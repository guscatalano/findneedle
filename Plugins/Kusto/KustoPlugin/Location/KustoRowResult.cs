using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using FindNeedlePluginLib;

namespace KustoPlugin.Location;

/// <summary>
/// One Kusto result row mapped to <see cref="ISearchResult"/>. Kusto schemas vary, so:
///   • if the row has recognizable columns (PreciseTimeStamp, Message, Level, …) we use them;
///   • if the row is essentially a single text column (a raw log line), we parse a leading
///     "[timestamp] LEVEL: message" out of it so Time/Level/Message populate instead of dumping
///     everything into Message.
/// The full row is always preserved as the searchable text so nothing is lost.
/// </summary>
public class KustoRowResult : ISearchResult
{
    private readonly Dictionary<string, string> _row;
    private readonly string _resultSource;
    private readonly string _searchable;
    private readonly DateTime _time;
    private readonly string _levelText;
    private readonly string _message;

    private static readonly string[] TimeCols   = { "PreciseTimeStamp", "TimeStamp", "Timestamp", "TimeGenerated", "EventTime", "ingestion_time()" };
    private static readonly string[] MsgCols     = { "Message", "EventMessage", "RenderedMessage", "Msg" };
    private static readonly string[] LevelCols   = { "Level", "Severity", "SeverityLevel", "LogLevel" };
    private static readonly string[] SourceCols  = { "ProviderName", "Provider", "Source", "SourceName" };
    private static readonly string[] TaskCols    = { "TaskName", "Task", "OperationName" };
    private static readonly string[] MachineCols = { "HostInstance", "Machine", "Computer", "RoleInstance", "Hostname", "Host" };
    private static readonly string[] UserCols    = { "User", "Username", "UserName", "Identity" };

    // "[2026-01-25 06:33:16] rest..."  (also tolerates a leading "name=" prefix)  →  ts + rest
    private static readonly Regex BracketTime = new(@"^\s*(?:[\w.]+=)?\[(?<ts>[^\]]{4,40})\]\s*(?<rest>.*)$", RegexOptions.Singleline | RegexOptions.Compiled);
    // "INFO: rest..."  →  level + rest
    private static readonly Regex LeadLevel = new(@"^(?<lvl>[A-Za-z]{3,12}):\s*(?<rest>.*)$", RegexOptions.Singleline | RegexOptions.Compiled);

    public KustoRowResult(Dictionary<string, string> row, string resultSource)
    {
        _row = row;
        _resultSource = resultSource;
        _searchable = string.Join(" | ", row.Select(kv => $"{kv.Key}={kv.Value}"));

        _time = ParseTime(First(TimeCols));
        _levelText = First(LevelCols);

        // Choose the primary text: an explicit Message column, else the single (or only non-empty)
        // column's value — that's the "one text column holds the whole log line" case.
        string primary = First(MsgCols);
        if (string.IsNullOrEmpty(primary))
        {
            if (row.Count == 1)
            {
                primary = row.Values.First();
            }
            else
            {
                var nonEmpty = row.Where(kv => !string.IsNullOrEmpty(kv.Value)).ToList();
                if (nonEmpty.Count == 1) primary = nonEmpty[0].Value;
            }
        }

        if (!string.IsNullOrEmpty(primary))
        {
            var display = primary;

            // Pull a leading [timestamp] out (fills Time if no time column gave us one).
            var bt = BracketTime.Match(display);
            if (bt.Success)
            {
                if (_time == DateTime.MinValue)
                {
                    var t = ParseTime(bt.Groups["ts"].Value);
                    if (t != DateTime.MinValue) _time = t;
                }
                display = bt.Groups["rest"].Value;
            }

            // Pull a leading "LEVEL:" out (fills Level if no level column gave us one).
            var ll = LeadLevel.Match(display);
            if (ll.Success)
            {
                if (string.IsNullOrEmpty(_levelText)) _levelText = ll.Groups["lvl"].Value;
                display = ll.Groups["rest"].Value;
            }

            _message = display;
        }
        else
        {
            // Multi-column row with no Message column — show the whole row.
            _message = _searchable;
        }
    }

    private string First(string[] candidates)
    {
        foreach (var c in candidates)
            if (_row.TryGetValue(c, out var v) && !string.IsNullOrEmpty(v)) return v;
        return string.Empty;
    }

    private static DateTime ParseTime(string s)
        => DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d) ? d : DateTime.MinValue;

    public DateTime GetLogTime() => _time;
    public string GetMachineName() => First(MachineCols);
    public void WriteToConsole() => Console.WriteLine(GetMessage());

    public Level GetLevel()
    {
        var raw = (_levelText ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(raw)) return Level.Info;
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
    public string GetMessage() => _message;
    public string GetResultSource() => _resultSource;
}
