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
using FindNeedlePluginLib;

namespace findneedle.Implementations;



public class LocalEventLogQueryLocation : IEventLogQueryLocation, ICommandLineParser
{

    public void Clone(ICommandLineParser parser)
    {
        //Keep nothing
    }
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



    public override void LoadInMemory(System.Threading.CancellationToken cancellationToken = default)
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
                        if (cancellationToken.IsCancellationRequested) return;
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
                if (cancellationToken.IsCancellationRequested) return;
            }
        }
       
    }

    public override List<ISearchResult> Search(System.Threading.CancellationToken cancellationToken = default)
    {
        numRecordsInLastResult = 0;
        List<ISearchResult> filteredResults = new List<ISearchResult>();
        foreach (ISearchResult result in searchResults)
        {
            if (cancellationToken.IsCancellationRequested) break;
            filteredResults.Add(result);
            numRecordsInLastResult++;
        }
        return filteredResults;
    }

    public override void ClearStatistics() => throw new NotImplementedException();
    public override List<ReportFromComponent> ReportStatistics()
    {
        return new();
    }

    public override Task SearchWithCallback(Action<List<ISearchResult>> onBatch, System.Threading.CancellationToken cancellationToken = default, int batchSize = 1000)
    {
        // Simple implementation: batch from searchResults
        var batch = new List<ISearchResult>(batchSize);
        foreach (var result in searchResults)
        {
            if (cancellationToken.IsCancellationRequested)
                break;
            batch.Add(result);
            if (batch.Count == batchSize)
            {
                onBatch(batch);
                batch = new List<ISearchResult>(batchSize);
            }
        }
        if (batch.Count > 0)
        {
            onBatch(batch);
        }
        return Task.CompletedTask;
    }

    public override (TimeSpan? timeTaken, int? recordCount) GetSearchPerformanceEstimate(System.Threading.CancellationToken cancellationToken = default)
    {
        // Stub implementation
        return (null, null);
    }
}
