using System.Diagnostics;
using findneedle.Implementations.Discovery;

namespace findneedle.Implementations;


public class LocalEventLogEntry : SearchResult
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
        return "LocalEventLog";
    }
}



public class LocalEventLogLocation : SearchLocation
{

    readonly EventLog eventLog = new();
    public string eventLogName
    {
        get; set;
    }
    readonly List<SearchResult> searchResults = new();
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

    public override void LoadInMemory(bool prefilter, SearchQuery searchQuery)
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
                        SearchResult result = new LocalEventLogEntry(log, this);
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
            SearchResult result = new LocalEventLogEntry(log, this);
            searchResults.Add(result);
            numRecordsInMemory++;
        }

    }

    public override List<SearchResult> Search(SearchQuery searchQuery)
    {
        numRecordsInLastResult = 0;
        List<SearchResult> filteredResults = new List<SearchResult>();
        foreach (SearchResult result in searchResults)
        {
            var passAll = true;
            foreach (SearchFilter filter in searchQuery.GetFilters())
            {
                if (!filter.Filter(result))
                {
                    passAll = false;
                    continue;
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
