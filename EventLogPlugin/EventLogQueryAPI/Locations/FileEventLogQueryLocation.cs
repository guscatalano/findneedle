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
        var overallStopwatch = System.Diagnostics.Stopwatch.StartNew();
        EventLogQuery eventsQuery = new EventLogQuery(filename,
                                                  PathType.FilePath
                                                  );
        EventLogReader logReader = new EventLogReader(eventsQuery);
        int count = 0;
        var startTime = DateTime.UtcNow;
        var lastReportTime = startTime;
        int lastReportCount = 0;
        var readEventsStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var lastSecondTime = DateTime.UtcNow;
        int lastSecondCount = 0;
        while (true)
        {
            if (cancellationToken.IsCancellationRequested) return;
            var readEventStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var eventdetail = logReader.ReadEvent();
            readEventStopwatch.Stop();
            Logger.Instance.Log($"[PERF] logReader.ReadEvent() took {readEventStopwatch.Elapsed.TotalMilliseconds:F0} ms");
            if (eventdetail == null) break;
            count++;
            if ((DateTime.UtcNow - lastSecondTime).TotalSeconds >= 1)
            {
                int eventsThisSecond = count - lastSecondCount;
                Logger.Instance.Log($"[PERF] Read {eventsThisSecond} events in the last second");
                lastSecondTime = DateTime.UtcNow;
                lastSecondCount = count;
            }
            var constructResultsStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = new EventRecordResult(eventdetail, this);
            constructResultsStopwatch.Stop();
            Logger.Instance.Log($"[PERF] Constructed EventRecordResult in {constructResultsStopwatch.Elapsed.TotalMilliseconds:F0} ms");
            searchResults.Add(result);
            numRecordsInMemory++;
            if (_progressSink != null && count % 1000 == 0)
            {
                var now = DateTime.UtcNow;
                var elapsed = (now - lastReportTime).TotalMinutes;
                var rate = elapsed > 0 ? (int)((count - lastReportCount) / elapsed) : 0;
                _progressSink.NotifyProgress(0, $"Loaded {count} event log records into memory ({rate} records/min)");
                lastReportTime = now;
                lastReportCount = count;
            }
        }
        overallStopwatch.Stop();
        Logger.Instance.Log($"[PERF] Total LoadInMemory time: {overallStopwatch.Elapsed.TotalSeconds:F2} seconds for {count} records");
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

    public override void ClearStatistics() => throw new NotImplementedException();
    public override List<ReportFromComponent> ReportStatistics() => throw new NotImplementedException();
    public override (TimeSpan? timeTaken, int? recordCount) GetSearchPerformanceEstimate(System.Threading.CancellationToken cancellationToken = default)
    {
        // Stub implementation
        return (null, null);
    }
}
