using System;
using System.Collections.Generic;
using FindNeedlePluginLib;

namespace findneedle.RuleDSL;

/// <summary>
/// Wraps an <see cref="ISearchResult"/> and overrides specific string fields from a map produced by an
/// "extract" enrichment rule (target field name → value). Any field present in the map is returned from
/// it; everything else delegates to the base result. Applied in the scan, before the storage insert, so
/// the overridden values land in the real SQLite columns (queryable, sortable, visible everywhere).
///
/// Keys are the stored field names: ProcessId, ThreadId, Source, ResultSource, TaskName, OpCode, Channel,
/// EventId, ProcessName, Username, MachineName, ActivityId, Keywords, RelatedActivityId, ProviderGuid,
/// RecordId, StructuredData. Typed/identity fields (LogTime, Level, Message, SearchableData, RowId) always
/// delegate.
/// </summary>
public sealed class EnrichedSearchResult : ISearchResult
{
    private readonly ISearchResult _base;
    private readonly Dictionary<string, string> _overrides;

    public EnrichedSearchResult(ISearchResult baseResult, Dictionary<string, string> overrides)
    {
        _base = baseResult ?? throw new ArgumentNullException(nameof(baseResult));
        // Case-insensitive so a rule can write "processId" or "ProcessId".
        _overrides = overrides ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private string Or(string key, Func<string> fallback)
        => _overrides.TryGetValue(key, out var v) ? v : fallback();

    // Identity / typed fields always delegate.
    public DateTime GetLogTime() => _base.GetLogTime();
    public Level GetLevel() => _base.GetLevel();
    // Message can be rewritten by a strip:true extract rule (the pulled-out text is removed); the
    // SearchableData stays the original so search/filter still match the raw text.
    public string GetMessage() => Or("Message", _base.GetMessage);
    public string GetSearchableData() => _base.GetSearchableData();
    public long GetRowId() => _base.GetRowId();
    public void WriteToConsole() => _base.WriteToConsole();

    // Overridable string fields.
    public string GetMachineName()       => Or("MachineName", _base.GetMachineName);
    public string GetUsername()          => Or("Username", _base.GetUsername);
    public string GetTaskName()          => Or("TaskName", _base.GetTaskName);
    public string GetOpCode()            => Or("OpCode", _base.GetOpCode);
    public string GetSource()            => Or("Source", _base.GetSource);
    public string GetResultSource()      => Or("ResultSource", _base.GetResultSource);
    public string GetProcessId()         => Or("ProcessId", _base.GetProcessId);
    public string GetThreadId()          => Or("ThreadId", _base.GetThreadId);
    public string GetActivityId()        => Or("ActivityId", _base.GetActivityId);
    public string GetEventId()           => Or("EventId", _base.GetEventId);
    public string GetKeywords()          => Or("Keywords", _base.GetKeywords);
    public string GetRelatedActivityId() => Or("RelatedActivityId", _base.GetRelatedActivityId);
    public string GetChannel()           => Or("Channel", _base.GetChannel);
    public string GetProviderGuid()      => Or("ProviderGuid", _base.GetProviderGuid);
    public string GetRecordId()          => Or("RecordId", _base.GetRecordId);
    public string GetProcessName()       => Or("ProcessName", _base.GetProcessName);
    public string GetStructuredData()    => Or("StructuredData", _base.GetStructuredData);
}
