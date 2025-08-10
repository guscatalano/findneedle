using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Security;
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
        //throw new NotSupportedException("Use Search or SearchWithCallback instead.");
        return; //We do nothing here, all is done in search
    }

    public override List<ISearchResult> Search(System.Threading.CancellationToken cancellationToken = default)
    {
        numRecordsInLastResult = 0;
        numRecordsInMemory = 0;
        var results = new List<ISearchResult>();

        var overallStopwatch = System.Diagnostics.Stopwatch.StartNew();
        EventLogQuery eventsQuery = new EventLogQuery(filename, PathType.FilePath);
        EventLogReader logReader = new EventLogReader(eventsQuery);
        int count = 0;
        var lastReportTime = DateTime.UtcNow;
        int lastReportCount = 0;
        var lastSecondTime = DateTime.UtcNow;
        int lastSecondCount = 0;
        while (true)
        {
            if (cancellationToken.IsCancellationRequested) break;
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
            results.Add(result);
            numRecordsInMemory++;
            numRecordsInLastResult++;
            if (_progressSink != null && count % 1000 == 0)
            {
                var now = DateTime.UtcNow;
                var elapsed = (now - lastReportTime).TotalMinutes;
                var rate = elapsed > 0 ? (int)((count - lastReportCount) / elapsed) : 0;
                int totalRecords = 0;
                double estimatedTimeLeft = 0;
                // Use cached performance estimate if available
                if (_cachedPerfEstimate != null && _cachedPerfEstimate.Value.recordCount.HasValue)
                {
                    totalRecords = _cachedPerfEstimate.Value.recordCount.Value;
                    int recordsLeft = totalRecords - count;
                    if (rate > 0)
                    {
                        estimatedTimeLeft = recordsLeft / (double)rate;
                    }
                    _progressSink.NotifyProgress(0, $"Loaded {count} event log records ({rate} records/min), {recordsLeft} left, est. time left: {(rate > 0 ? $"{estimatedTimeLeft:F1} min" : "unknown")}");
                }
                else
                {
                    _progressSink.NotifyProgress(0, $"Loaded {count} event log records ({rate} records/min)");
                }
                lastReportTime = now;
                lastReportCount = count;
            }
        }
        overallStopwatch.Stop();
        Logger.Instance.Log($"[PERF] Total Search time: {overallStopwatch.Elapsed.TotalSeconds:F2} seconds for {count} records");
        if (_progressSink != null)
        {
            _progressSink.NotifyProgress(100, $"Finished searching {count} event log records");
        }
        return results;
    }

    public override Task SearchWithCallback(Action<List<ISearchResult>> onBatch, System.Threading.CancellationToken cancellationToken = default, int batchSize = 1000)
    {
        numRecordsInLastResult = 0;
        numRecordsInMemory = 0;
       
        var overallStopwatch = System.Diagnostics.Stopwatch.StartNew();
        EventLogQuery eventsQuery = new EventLogQuery(filename, PathType.FilePath);
        EventLogReader logReader = new EventLogReader(eventsQuery);
        int count = 0;
        var lastReportTime = DateTime.UtcNow;
        int lastReportCount = 0;
        var lastSecondTime = DateTime.UtcNow;
        int lastSecondCount = 0;
        var batch = new List<ISearchResult>(batchSize);
        while (true)
        {
            if (cancellationToken.IsCancellationRequested) break;
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
            batch.Add(result);
            numRecordsInMemory++;
            numRecordsInLastResult++;
            if (batch.Count == batchSize)
            {
                onBatch(batch);
                batch = new List<ISearchResult>(batchSize);
            }
            if (_progressSink != null && count % 1000 == 0)
            {
                var now = DateTime.UtcNow;
                var elapsed = (now - lastReportTime).TotalMinutes;
                var rate = elapsed > 0 ? (int)((count - lastReportCount) / elapsed) : 0;
                int totalRecords = 0;
                double estimatedTimeLeft = 0;
                // Use cached performance estimate if available
                if (_cachedPerfEstimate != null && _cachedPerfEstimate.Value.recordCount.HasValue)
                {
                    totalRecords = _cachedPerfEstimate.Value.recordCount.Value;
                    int recordsLeft = totalRecords - count;
                    if (rate > 0)
                    {
                        estimatedTimeLeft = recordsLeft / (double)rate;
                    }
                    _progressSink.NotifyProgress(0, $"Loaded {count} event log records ({rate} records/min), {recordsLeft} left, est. time left: {(rate > 0 ? $"{estimatedTimeLeft:F1} min" : "unknown")}");
                }
                else
                {
                    _progressSink.NotifyProgress(0, $"Loaded {count} event log records ({rate} records/min)");
                }
                lastReportTime = now;
                lastReportCount = count;
            }
        }
        if (batch.Count > 0)
        {
            onBatch(batch);
        }
        overallStopwatch.Stop();
        Logger.Instance.Log($"[PERF] Total SearchWithCallback time: {overallStopwatch.Elapsed.TotalSeconds:F2} seconds for {count} records");
        if (_progressSink != null)
        {
            _progressSink.NotifyProgress(100, $"Finished searching {count} event log records (batched)");
        }
        return Task.CompletedTask;
    }

    public override void ClearStatistics() => throw new NotImplementedException();
    public override List<ReportFromComponent> ReportStatistics() => throw new NotImplementedException();
    private (TimeSpan? timeTaken, int? recordCount)? _cachedPerfEstimate = null;
    private string? _cachedPerfEstimateFilename = null;
    private CancellationToken _cachedPerfEstimateToken;

    public override (TimeSpan? timeTaken, int? recordCount) GetSearchPerformanceEstimate(System.Threading.CancellationToken cancellationToken = default)
    {
        if (_cachedPerfEstimate != null && _cachedPerfEstimateFilename == filename && _cachedPerfEstimateToken == cancellationToken)
        {
            return _cachedPerfEstimate.Value;
        }
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        int count = 0;
        try
        {
            EventLogQuery eventsQuery = new EventLogQuery(filename, PathType.FilePath);
            using EventLogReader logReader = new EventLogReader(eventsQuery);
            while (true)
            {
                if (cancellationToken.IsCancellationRequested) break;
                var eventdetail = logReader.ReadEvent();
                if (eventdetail == null) break;
                count++;
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"[PERF] Exception in GetSearchPerformanceEstimate: {ex.Message}");
            _cachedPerfEstimate = (null, null);
            _cachedPerfEstimateFilename = filename;
            _cachedPerfEstimateToken = cancellationToken;
            return (null, null);
        }
        finally
        {
            stopwatch.Stop();
        }
        var result = (stopwatch.Elapsed, count);
        _cachedPerfEstimate = result;
        _cachedPerfEstimateFilename = filename;
        _cachedPerfEstimateToken = cancellationToken;
        return result;
    }
}
