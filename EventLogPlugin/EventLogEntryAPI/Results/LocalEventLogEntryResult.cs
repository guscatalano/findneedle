using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle.Implementations;
using findneedle;
using FindNeedlePluginLib;

namespace findneedle.Implementations;


public class LocalEventLogEntryResult : ISearchResult
{
    readonly EventLogEntry entry;
    readonly LocalEventLogLocation location;
    public LocalEventLogEntryResult(EventLogEntry entry, LocalEventLogLocation location)
    {
        this.entry = entry;
        this.location = location;
    }


    public Level GetLevel()
    {
        switch (entry.EntryType)
        {
            case EventLogEntryType.Warning:
                return Level.Warning;
            case EventLogEntryType.Error:
                return Level.Error;
            case EventLogEntryType.Information:
                return Level.Info;
            case EventLogEntryType.SuccessAudit:
            case EventLogEntryType.FailureAudit:
            default:
                return Level.Verbose;

        }
    }

    public DateTime GetLogTime()
    {
        return entry.TimeGenerated;
    }

    public string GetMachineName()
    {
        return entry.MachineName;
    }

    public string GetOpCode()
    {
        // The legacy EventLogEntry API doesn't expose OpCode — return empty (throwing here crashed
        // storage ingest, which calls GetOpCode() on every row).
        return string.Empty;
    }

    // The legacy EventLogEntry API exposes the low word of InstanceId as the event id, and Index as
    // the record number. It has no PID/TID/ActivityId. ReplacementStrings are the event's data fields.
    public string GetEventId()
    {
        try { return (entry.InstanceId & 0xFFFF).ToString(System.Globalization.CultureInfo.InvariantCulture); }
        catch { return ""; }
    }

    public string GetRecordId()
    {
        try { return entry.Index.ToString(System.Globalization.CultureInfo.InvariantCulture); } catch { return ""; }
    }

    public string GetStructuredData()
    {
        try
        {
            var rs = entry.ReplacementStrings;
            if (rs == null || rs.Length == 0) return "";
            var pairs = new Dictionary<string, string>();
            for (int i = 0; i < rs.Length; i++) pairs["Data" + (i + 1)] = rs[i] ?? "";
            return System.Text.Json.JsonSerializer.Serialize(pairs);
        }
        catch { return ""; }
    }

    public string GetSource()
    {
        return entry.Source;
    }

    public string GetTaskName()
    {
        return entry.Category;
    }

    public string GetUsername()
    {
        return entry.UserName;
    }

    public string GetMessage()
    {
        return entry.Message;
    }

    //This is usually the message, but it can be more. This is likely not readable
    public string GetSearchableData()
    {

        return string.Join(' ', entry.Message, entry.UserName, entry.MachineName, entry.Category, entry.CategoryNumber, entry.InstanceId, entry.Source);
    }

    public void WriteToConsole()
    {
        Console.WriteLine(entry.TimeGenerated + ": " + entry.Message + " ;; " + entry.EntryType);
    }

    public string GetResultSource()
    {
        return "LocalEventLog-" + location.GetName();
    }
}



