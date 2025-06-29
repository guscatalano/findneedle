﻿using System.Diagnostics;
using EventLogPlugin.EvQueryNativeAPI;
using findneedle.Implementations.Discovery;
using findneedle.Implementations.Locations.EventLogQueryLocation;
using FindNeedlePluginLib;

namespace findneedle.Implementations;


public class LocalEventLogLocation : IEventLogQueryLocation, ICommandLineParser
{
    public void Clone(ICommandLineParser parser)
    {
        //Keep nothing
    }
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
            var failed = 0;
            var succeess = 0;
            foreach (var provider in providers)
            {
                try
                {
                    eventLog.Log = provider;

                    foreach (EventLogEntry log in eventLog.Entries)
                    {
                        ISearchResult result = new LocalEventLogEntryResult(log, this);
                        searchResults.Add(result);
                        numRecordsInMemory++;
                    }
                    succeess++;
                } catch (Exception)
                {
                    //skip for now
                    failed++;
                }
            }
            Console.WriteLine("Out of everything, pass: " + succeess + " , failed: " + failed);
            Console.WriteLine("Note, this API has limited support for Application, System, etc. logs, and may not work for all logs.");
            return;
        }
        eventLog.Log = eventLogName;
        
        foreach (EventLogEntry log in eventLog.Entries)
        {
            ISearchResult result = new LocalEventLogEntryResult(log, this);
            searchResults.Add(result);
            numRecordsInMemory++;
        }

    }

    public override List<ISearchResult> Search(ISearchQuery? searchQuery)
    {
        if(searchQuery == null)
        {
            return searchResults;
        }
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
        return searchResults;
    }

    public CommandLineRegistration RegisterCommandHandler() 
    {
        var reg = new CommandLineRegistration()
        {
            handlerType = CommandLineHandlerType.Location,
            key = "eventlogentry"
        };
        return reg;
    }
    public void ParseCommandParameterIntoQuery(string parameter)
    {
        if (!string.IsNullOrEmpty(parameter))
        {
            eventLogName = parameter;
        } 
        else
        {
            eventLogName = "everything";
        }
        
    }

    public override void ClearStatistics() => throw new NotImplementedException();
    public override List<ReportFromComponent> ReportStatistics() {
        return new List<ReportFromComponent>();
    }
}
