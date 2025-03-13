using System.Diagnostics;
using findneedle.Implementations.Discovery;
using findneedle.Implementations.Locations.EventLogQueryLocation;
using FindNeedlePluginLib.Interfaces;

namespace findneedle.Implementations;


public class LocalEventLogEntry : ISearchResult
{
    readonly EventLogEntry entry;
    readonly LocalEventLogLocation location;
    public LocalEventLogEntry(EventLogEntry entry, LocalEventLogLocation location)
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

    public string GetResultSource() {
        return "LocalEventLog-"+location.GetName();
    }
}



public class LocalEventLogLocation : IEventLogQueryLocation
{

    readonly EventLog eventLog = new();
    public string eventLogName
    {
        get; set;
    }
    readonly List<ISearchResult> searchResults = new();
    public LocalEventLogLocation()
    {
        eventLogName = "Application";
    }

    public LocalEventLogLocation(string name)
    {
        eventLogName = name;
    }

    public override string GetDescription()
    {
        return "LocalEventLog";
    }
    public override string GetName()
    {
        return eventLogName;
    }

    public override void LoadInMemory()
    {
        if(eventLogName.Equals("everything", StringComparison.OrdinalIgnoreCase))
        {
            List<string> providers = EventLogDiscovery.GetAllEventLogs();
            foreach (var provider in providers)
            {
                try
                {
                    eventLog.Log = provider;

                    foreach (EventLogEntry log in eventLog.Entries)
                    {
                        ISearchResult result = new LocalEventLogEntry(log, this);
                        searchResults.Add(result);
                        numRecordsInMemory++;
                    }
                } catch (Exception)
                {
                    //skip for now
                }
            }
            return;
        }
        eventLog.Log = eventLogName;
        
        foreach (EventLogEntry log in eventLog.Entries)
        {
            ISearchResult result = new LocalEventLogEntry(log, this);
            searchResults.Add(result);
            numRecordsInMemory++;
        }

    }

    public override List<ISearchResult> Search(ISearchQuery? searchQuery)
    {
        numRecordsInLastResult = 0;
        List<ISearchResult> filteredResults = new List<ISearchResult>();
        foreach (ISearchResult result in searchResults)
        {
            var passAll = true;
            if (searchQuery != null)
            {
                foreach (ISearchFilter filter in searchQuery.GetFilters())
                {
                    if (!filter.Filter(result))
                    {
                        continue;
                    }
                }
            }
            if (passAll)
            {
                filteredResults.Add(result);
                numRecordsInLastResult++;
            }
        }
        return filteredResults;
    }
}
