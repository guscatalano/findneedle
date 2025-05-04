using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EventLogPlugin.EvQueryNativeAPI;
using findneedle.Implementations.Discovery;
using findneedle.Implementations.Locations.EventLogQueryLocation;
using FindNeedlePluginLib.Interfaces;

namespace findneedle.Implementations;



public class LocalEventLogQueryLocation : IEventLogQueryLocation, ICommandLineParser
{


    public CommandLineRegistration RegisterCommandHandler()
    {
        var reg = new CommandLineRegistration()
        {
            handlerType = CommandLineHandlerType.Location,
            key = "eventlogquery"
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

    public string eventLogName
    { get; set;
    }
    readonly List<ISearchResult> searchResults = new();
    public LocalEventLogQueryLocation()
    {
        eventLogName = "Application";
    }

    public LocalEventLogQueryLocation(string name)
    {
        eventLogName = name;
    }

    public override string GetDescription()
    {
        return "LocalEventLogQuery";
    }
    public override string GetName()
    {
        return eventLogName;
    }



    public override void LoadInMemory()
    {

        if (eventLogName.Equals("everything", StringComparison.OrdinalIgnoreCase))
        {
            List<string> providers = EventLogDiscovery.GetAllEventLogs();
            var failed = 0;
            var succeess = 0;
            foreach (var provider in providers)
            {
                try
                {
                    eventLogName = provider;
                    EventLogQuery eventsQuery = new EventLogQuery(eventLogName,
                                                                  PathType.LogName
                                                                  );
                    EventLogReader logReader = new EventLogReader(eventsQuery);
                    for (EventRecord eventdetail = logReader.ReadEvent(); eventdetail != null; eventdetail = logReader.ReadEvent())
                    {
                        ISearchResult result = new EventRecordResult(eventdetail, this);
                        searchResults.Add(result);
                        numRecordsInMemory++;
                    }
                    succeess++;
                }
                catch (Exception)
                {
                    try
                    {
                        searchResults.AddRange(EventLogNativeWrapper.GetEventsAsResults(provider, "*"));
                        
                    } catch(Exception)
                    {
                        //We really couldnt get it
                        //Console.WriteLine(e2.Message);
                    }
                    //skip for now
                    failed++;
                }
            }
            return;
        }
        else
        {
            //This can be useful to pre-filter
            var query = "*"; //"*[System/Level=3 or Level=4]";
            EventLogQuery eventsQuery = new EventLogQuery(eventLogName, PathType.LogName, query);
            EventLogReader logReader = new EventLogReader(eventsQuery);

            for (EventRecord eventdetail = logReader.ReadEvent(); eventdetail != null; eventdetail = logReader.ReadEvent())
            {
                ISearchResult result = new EventRecordResult(eventdetail, this);
                searchResults.Add(result);
                numRecordsInMemory++;
            }
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
                        passAll = false;
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

    public override void ClearStatistics() => throw new NotImplementedException();
    public override List<ReportFromComponent> ReportStatistics()
    {
        return new();
    }
}
