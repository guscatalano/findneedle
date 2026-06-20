using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FindNeedlePluginLib;


public enum Level
{
    Catastrophic,
    Error,
    Warning,
    Info,
    Verbose, 
    Unknown // For use when we can't tell the level
}

public interface ISearchResult
{

    public const string NOT_SUPPORTED = "!NOT_SUPPORTED!"; //Use this in a search location where the request makes no sense, or throw.
    public DateTime GetLogTime();
    public string GetMachineName();
    public void WriteToConsole();
    public Level GetLevel();

    public string GetUsername();

    public string GetTaskName();
    public string GetOpCode();
    public string GetSource();

    public string GetSearchableData();
    public string GetMessage();


    public string GetResultSource();

    /// <summary>
    /// A stable, durable identifier for this row within the result set it came from. For the
    /// disk-backed store this is the SQLite <c>FilteredResults.Id</c>; it does not change when the
    /// viewer's filter/sort/paging changes (unlike the displayed row position). Used as the handle
    /// for record lookup and row tagging, including by the MCP server.
    /// Returns -1 for backends that have no stable id (callers fall back to load-order position).
    /// </summary>
    public long GetRowId() => -1;

    /// <summary>Originating process id (ETW ProcessID, EventLog Execution ProcessID), or "" if the
    /// source doesn't carry one. Default "" so existing result types don't have to implement it.</summary>
    public string GetProcessId() => "";

    /// <summary>Originating thread id (ETW ThreadID, EventLog Execution ThreadID), or "".</summary>
    public string GetThreadId() => "";

    /// <summary>Correlation / activity id (ETW ActivityId, EventLog Correlation ActivityID), or "".</summary>
    public string GetActivityId() => "";

    /// <summary>Numeric event id (EventRecord.Id / TraceEvent.ID), or "" if not applicable.</summary>
    public string GetEventId() => "";

    /// <summary>Event keywords (display names), or "".</summary>
    public string GetKeywords() => "";

    /// <summary>Related/parent activity id for causality correlation, or "".</summary>
    public string GetRelatedActivityId() => "";

    /// <summary>Channel / log name the event came from (Application/System/…, ETW channel), or "".</summary>
    public string GetChannel() => "";

    /// <summary>Provider GUID (vs the friendly provider name), or "".</summary>
    public string GetProviderGuid() => "";

    /// <summary>The source log's own record sequence number (EventLog EventRecordID), or "".</summary>
    public string GetRecordId() => "";

    /// <summary>Friendly originating process name (ETW ProcessName), or "" if unknown.</summary>
    public string GetProcessName() => "";

    /// <summary>
    /// The event's structured payload as a JSON object of name→value pairs (parsed EventData/UserData
    /// for the Event Log, named properties for ETW), or "" if none. Shown as an expandable key/value
    /// section in the details panel instead of being buried in the message blob.
    /// </summary>
    public string GetStructuredData() => "";
}
