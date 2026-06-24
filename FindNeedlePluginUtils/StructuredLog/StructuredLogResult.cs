using System;
using System.IO;
using FindNeedlePluginLib;

namespace FindNeedlePluginUtils.StructuredLog;

/// <summary>
/// An <see cref="ISearchResult"/> for a row parsed out of a structured log (CSV / JSON). The
/// well-known fields (time, level, message, provider, ids…) are mapped into columns by
/// <see cref="StructuredLogFieldMapper"/>; every original field is also preserved in
/// <see cref="StructuredDataJson"/> and folded into <see cref="SearchableData"/> so search and
/// filters still hit columns we didn't map.
///
/// Note: the viewer's "Provider" column is fed by <see cref="GetSource"/> and the "Source" column by
/// <see cref="GetResultSource"/> (the file path) — matching the rest of the codebase.
/// </summary>
public sealed class StructuredLogResult : ISearchResult
{
    public string SourceFile { get; set; } = string.Empty;
    public int LineNumber { get; set; }

    // Mapped fields (empty when the source didn't carry one).
    public string Message { get; set; } = string.Empty;
    public Level Level { get; set; } = Level.Info;
    public DateTime LogTime { get; set; } = DateTime.MinValue;
    public string Provider { get; set; } = string.Empty;   // logger/category/source name → "Provider" column
    public string TaskName { get; set; } = string.Empty;
    public string ProcessId { get; set; } = string.Empty;
    public string ThreadId { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string ActivityId { get; set; } = string.Empty;

    /// <summary>All original fields as a JSON object (shown in the details panel).</summary>
    public string StructuredDataJson { get; set; } = string.Empty;
    /// <summary>Message plus every field value, so search/filter matches unmapped columns too.</summary>
    public string SearchableData { get; set; } = string.Empty;

    public DateTime GetLogTime() => LogTime;
    public Level GetLevel() => Level;
    public string GetMessage() => Message;
    public string GetSearchableData() => string.IsNullOrEmpty(SearchableData) ? Message : SearchableData;

    // Provider name → "Provider" column (GetSource); file path → "Source" column (GetResultSource).
    // When the data has no recognized provider/logger column, leave Provider blank rather than falling
    // back to the file name — the file is already shown in the Source column, so putting the file name
    // in Provider was misleading (it looked like the provider was literally the CSV file).
    public string GetSource() => Provider ?? string.Empty;
    public string GetResultSource() => SourceFile;

    public string GetMachineName() => MachineName;
    public string GetUsername() => Username;
    public string GetTaskName() => TaskName;
    public string GetOpCode() => string.Empty;
    public string GetProcessId() => ProcessId;
    public string GetThreadId() => ThreadId;
    public string GetActivityId() => ActivityId;
    public string GetEventId() => EventId;
    public string GetStructuredData() => StructuredDataJson;

    public void WriteToConsole() => Console.WriteLine($"[{LineNumber}] {Message}");
}
