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
        throw new NotImplementedException();
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



