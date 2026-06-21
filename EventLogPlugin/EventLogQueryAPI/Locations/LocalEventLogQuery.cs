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



    // Reading is DEFERRED: LoadInMemory no longer materializes every record. The search engine drains
    // us through SearchWithCallback, streaming each batch straight to storage (SQLite for large loads),
    // so "everything" doesn't hold the whole machine's event history in memory (which OOM-crashed it).
    public override void LoadInMemory(System.Threading.CancellationToken cancellationToken = default)
    {
        numRecordsInMemory = 0; // nothing pre-loaded; records stream during Search/SearchWithCallback
    }

    /// <summary>Lazily yield records across the requested channel(s). For "everything" it walks every
    /// channel on the machine; per-channel failures fall back to the native wrapper, then are skipped.
    /// Honors cancellation between records so callers can bound the load.</summary>
    private IEnumerable<ISearchResult> ReadRecords(System.Threading.CancellationToken cancellationToken)
    {
        if (eventLogName.Equals("everything", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var provider in EventLogDiscovery.GetAllEventLogs())
            {
                if (cancellationToken.IsCancellationRequested) yield break;
                foreach (var r in ReadChannel(provider, cancellationToken)) yield return r;
            }
        }
        else
        {
            foreach (var r in ReadChannel(eventLogName, cancellationToken)) yield return r;
        }
    }

    private IEnumerable<ISearchResult> ReadChannel(string channel, System.Threading.CancellationToken cancellationToken)
    {
        EventLogReader logReader = null;
        List<ISearchResult> fallback = null;
        try
        {
            logReader = new EventLogReader(new EventLogQuery(channel, PathType.LogName, "*"));
        }
        catch (Exception)
        {
            try { fallback = EventLogNativeWrapper.GetEventsAsResults(channel, "*"); }
            catch (Exception) { fallback = null; } // couldn't read this channel — skip it
        }

        if (logReader != null)
        {
            using (logReader)
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    EventRecord eventdetail;
                    try { eventdetail = logReader.ReadEvent(); }
                    catch (Exception) { break; } // a bad record/channel — stop reading it
                    if (eventdetail == null) break;
                    yield return new EventRecordResult(eventdetail, this);
                }
            }
        }
        else if (fallback != null)
        {
            foreach (var r in fallback)
            {
                if (cancellationToken.IsCancellationRequested) yield break;
                yield return r;
            }
        }
    }

    public override List<ISearchResult> Search(System.Threading.CancellationToken cancellationToken = default)
    {
        numRecordsInLastResult = 0;
        var results = new List<ISearchResult>();
        foreach (var result in ReadRecords(cancellationToken))
        {
            results.Add(result);
            numRecordsInLastResult++;
        }
        numRecordsInMemory = results.Count;
        return results;
    }

    public override void ClearStatistics() => throw new NotImplementedException();
    public override List<ReportFromComponent> ReportStatistics()
    {
        return new();
    }

    public override Task SearchWithCallback(Action<List<ISearchResult>> onBatch, System.Threading.CancellationToken cancellationToken = default, int batchSize = 1000)
    {
        numRecordsInLastResult = 0;
        var batch = new List<ISearchResult>(batchSize);
        foreach (var result in ReadRecords(cancellationToken))
        {
            batch.Add(result);
            numRecordsInLastResult++;
            if (batch.Count >= batchSize)
            {
                onBatch(batch);
                batch = new List<ISearchResult>(batchSize); // hand off + drop our reference (no retention)
            }
        }
        if (batch.Count > 0) onBatch(batch);
        return Task.CompletedTask;
    }

    public override (TimeSpan? timeTaken, int? recordCount) GetSearchPerformanceEstimate(System.Threading.CancellationToken cancellationToken = default)
    {
        // Estimate the row count from each channel's RecordCount so Auto storage sizes correctly
        // (large "everything" loads land on SQLite + stream to disk instead of blowing up memory).
        try
        {
            var session = new EventLogSession();
            long total = 0;
            var channels = eventLogName.Equals("everything", StringComparison.OrdinalIgnoreCase)
                ? EventLogDiscovery.GetAllEventLogs()
                : new List<string> { eventLogName };
            foreach (var ch in channels)
            {
                if (cancellationToken.IsCancellationRequested) break;
                try { total += session.GetLogInformation(ch, PathType.LogName).RecordCount ?? 0; }
                catch (Exception) { /* unreadable channel — ignore in the estimate */ }
            }
            return (null, total > int.MaxValue ? int.MaxValue : (int)total);
        }
        catch (Exception) { return (null, null); }
    }
}
