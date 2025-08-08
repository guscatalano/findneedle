using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using findneedle.Implementations.Locations.EventLogQueryLocation;
using FindNeedlePluginLib;
using FindNeedlePluginLib.Interfaces;

namespace findneedle.Implementations.Locations;

public class FileEventLogQueryLocation : IEventLogQueryLocation, IReportProgress
{
    private SearchProgressSink? _progressSink;
    public void SetProgressSink(SearchProgressSink sink)
    {
        _progressSink = sink;
    }

    public SearchProgressSink searchProgressSink { get; set; } = new();

    public string filename
    {
        get; set;
    }
    readonly List<ISearchResult> searchResults = new();

    public FileEventLogQueryLocation(string filename)
    {
        this.filename = filename;
    }

    public override string GetDescription()
    {
        return "LocalEventLogQuery";
    }
    public override string GetName()
    {
        return filename;
    }

    public override void LoadInMemory(System.Threading.CancellationToken cancellationToken = default)
    {
        if (_progressSink != null)
        {
            _progressSink.NotifyProgress(0, "Starting event log load...");
        }
        EventLogQuery eventsQuery = new EventLogQuery(filename,
                                                      PathType.FilePath
                                                      );
        EventLogReader logReader = new EventLogReader(eventsQuery);
        int count = 0;
        var startTime = DateTime.UtcNow;
        var lastReportTime = startTime;
        int lastReportCount = 0;
        for (EventRecord eventdetail = logReader.ReadEvent(); eventdetail != null; eventdetail = logReader.ReadEvent())
        {
            if (cancellationToken.IsCancellationRequested) return;
            ISearchResult result = new EventRecordResult(eventdetail, this);
            searchResults.Add(result);
            numRecordsInMemory++;
            count++;
            if ((count == 1 || count % 1000 == 0) && _progressSink != null)
            {
                var now = DateTime.UtcNow;
                var elapsed = (now - lastReportTime).TotalMinutes;
                var rate = elapsed > 0 ? (int)((count - lastReportCount) / elapsed) : 0;
                _progressSink.NotifyProgress(0, $"Loaded {count} event log records into memory ({rate} records/min)");
                lastReportTime = now;
                lastReportCount = count;
            }
        }
        if (_progressSink != null)
        {
            _progressSink.NotifyProgress(100, $"Finished loading {count} event log records into memory");
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
    public override List<ReportFromComponent> ReportStatistics() => throw new NotImplementedException();
}
