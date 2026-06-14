using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using FindNeedleCoreUtils;
using findneedle.WDK;
using Newtonsoft.Json;
using FindNeedlePluginLib;
using FindNeedlePluginLib.Interfaces;
using Windows.Media.PlayTo;
// Do not import FindPluginCore directly, use reflection for Logger

namespace findneedle.Implementations.FileExtensions;
public class ETLProcessor : IFileExtensionProcessor, IPluginDescription, IReportProgress
{
    public TraceFmtResult currentResult
    {
        get; private set; 
    }

    public Dictionary<string, int> providers = new();

    public bool LoadEarly = true;
    private readonly string tempPath = "";

    public string inputfile = "";
    private SearchProgressSink? _progressSink;

    private int _badlyFormattedCount = 0;

    public ETLProcessor()
    {
        Logger.Instance.Log("ETLProcessor constructed");
        currentResult = new TraceFmtResult(); //empty
        tempPath = TempStorage.GetNewTempPath("etl");
    }

    public void Dispose()
    {
        Logger.Instance.Log($"Disposing ETLProcessor for file: {inputfile}");
        TempStorage.DeleteSomeTempPath(tempPath);
    }

    public void OpenFile(string fileName)
    {
        Logger.Instance.Log($"OpenFile called in ETLProcessor for file: {fileName}");
        inputfile = fileName;
    }

    public Dictionary<string, int> GetProviderCount()
    {
        return providers;
    }

    public string GetFileName()
    {
        return inputfile; 
    }

   
    public void DoPreProcessing()
    {
        DoPreProcessing(CancellationToken.None);
    }
    public void DoPreProcessing(CancellationToken cancellationToken)
    {
        Logger.Instance.Log($"DoPreProcessing started for file: {inputfile}");
        _progressSink?.NotifyProgress(0, $"Preprocessing {inputfile}");
        var getLock = 50;

        if (inputfile.EndsWith(".txt") || inputfile.EndsWith(".log"))
        {
            Logger.Instance.Log($"Input file is .txt or .log, skipping TraceFmt: {inputfile}");
            currentResult.ProcessedFile = inputfile;
            currentResult.outputfile = inputfile;
            currentResult.summaryfile = inputfile;
        }
        else
        {
            Logger.Instance.Log($"Calling TraceFmt.ParseSimpleETL for file: {inputfile}");
            currentResult = TraceFmt.ParseSimpleETL(inputfile, tempPath, _progressSink);
            if (currentResult == null)
            {
                Logger.Instance.Log($"TraceFmt result is null for {inputfile}, skipping ETL processing.");
                _progressSink?.NotifyProgress(100, $"TraceFmt not found or failed for {inputfile}, skipping ETL processing.");
                return;
            }
        }
        _progressSink?.NotifyProgress(20, "Parsing output file");
        while (getLock > 0)
        {
            if (cancellationToken.IsCancellationRequested) return;
            try
            {
                if (currentResult.outputfile == null)
                {
                    Logger.Instance.Log($"Output file is not set for {inputfile}");
                    throw new InvalidOperationException("Output file is not set.");
                }
                using var fileStream = File.OpenRead(currentResult.outputfile);
                using var streamReader = new StreamReader(fileStream, Encoding.UTF8, false); //change buffer if there's perf reasons

                string? line;
                int lineCount = 0;
                while ((line = streamReader.ReadLine()) != null)
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    var failsafe = 10;
                    while (!ETLLogLine.DoesHeaderLookRight(line) && failsafe > 0)
                    {
                        if (line.StartsWith("Unknown"))
                        {
                            failsafe = 0; //This is corrupted, let's just bail;
                            Logger.Instance.Log($"Corrupted line detected in {inputfile}: {line}");
                            continue;
                        }
                        //line is not complete!
                        failsafe--;
                        line += streamReader.ReadLine();
                    }
                    if (failsafe == 0)
                    {
                        Logger.Instance.Log($"Failsafe triggered, skipping line in {inputfile}");
                        continue; // Don't throw or we skip too much!
                    }
                    var etlline = new ETLLogLine(line, inputfile);
                    if (providers.ContainsKey(etlline.GetSource()))
                    {
                        providers[etlline.GetSource()]++;
                    }
                    else
                    {
                        providers[etlline.GetSource()] = 1;
                    }
                    results.Add(etlline);
                    lineCount++;
                    if (lineCount % 1000 == 0)
                    {
                        Logger.Instance.Log($"Processed {lineCount} lines for {inputfile}");
                        _progressSink?.NotifyProgress(20 + (int)(70.0 * lineCount / 100000), $"Processed {lineCount} lines");
                    }
                }
                Logger.Instance.Log($"Finished reading output file for {inputfile}, total lines: {lineCount}");
                _progressSink?.NotifyProgress(90, $"Finished reading output file, total lines: {lineCount}");
                break;
            }
            catch (Exception ex)
            {
                Logger.Instance.Log($"Exception while reading output file for {inputfile}: {ex.Message}");
                Thread.Sleep(100);
                getLock--; // Sometimes tracefmt can hold the lock, wait until file is ready
            }
        }

        // Fallback for non-WPP traces. tracefmt only formats WPP (driver/software) traces; for a
        // modern .etl (EventSource / manifest / kernel) it emits "Unknown" lines and we end up
        // with zero rows. In that case decode the .etl directly with the TraceEvent library — the
        // same source LiveCollector uses for real-time — which understands those event kinds.
        // Gated on results.Count == 0 so the existing WPP path (e.g. the sample test.etl) is
        // untouched; this only rescues traces tracefmt couldn't read.
        if (results.Count == 0
            && !inputfile.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            && !inputfile.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
            && File.Exists(inputfile))
        {
            Logger.Instance.Log($"tracefmt produced no rows for {inputfile}; falling back to TraceEvent decode");
            _progressSink?.NotifyProgress(50, "Decoding ETL with TraceEvent (non-WPP trace)");
            DecodeWithTraceEvent(cancellationToken);
        }

        Logger.Instance.Log($"DoPreProcessing complete for {inputfile}");
        _progressSink?.NotifyProgress(100, $"Preprocessing complete for {inputfile}");
    }

    /// <summary>
    /// Decode an .etl directly via the TraceEvent library (manifest / EventSource / kernel events).
    /// Used as a fallback when tracefmt yields nothing because the trace isn't WPP. Builds the same
    /// <see cref="ETLLogLine"/> objects as the real-time collector, so downstream is identical.
    /// Never throws — a decode failure just leaves results as-is.
    /// </summary>
    private void DecodeWithTraceEvent(CancellationToken cancellationToken)
    {
        try
        {
            using var source = new Microsoft.Diagnostics.Tracing.ETWTraceEventSource(inputfile);

            void Handle(Microsoft.Diagnostics.Tracing.TraceEvent e)
            {
                if (cancellationToken.IsCancellationRequested) { source.StopProcessing(); return; }
                var line = new ETLLogLine(e);
                var src = line.GetSource() ?? string.Empty;
                providers[src] = providers.TryGetValue(src, out var c) ? c + 1 : 1;
                results.Add(line);
            }

            // Dynamic = manifest/EventSource providers; Kernel = NT kernel logger events. Each event
            // is dispatched to exactly one, so no double counting.
            source.Dynamic.All += Handle;
            source.Kernel.All += Handle;
            source.Process();
            Logger.Instance.Log($"TraceEvent decode produced {results.Count} rows for {inputfile}");
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"TraceEvent decode failed for {inputfile}: {ex.Message}");
        }
    }

    readonly List<ISearchResult> results = new();
    public void LoadInMemory() 
    {
        LoadInMemory(CancellationToken.None);
    }
    public void LoadInMemory(CancellationToken cancellationToken)
    {
        Logger.Instance.Log($"LoadInMemory called for ETLProcessor, file: {inputfile}");
        _badlyFormattedCount = 0;
        _progressSink?.NotifyProgress(0, $"Loading results into memory for {inputfile}");
        if (LoadEarly)
        {
            int total = results.Count;
            int count = 0;
            var lastProgressTime = DateTime.UtcNow;
            foreach(var result in results)
            {
                if (cancellationToken.IsCancellationRequested) return;
                if (result is ETLLogLine etlLogLine)
                {
                    etlLogLine.PreLoad();
                    if (etlLogLine.tasktxt == "Badly formatted event")
                    {
                        _badlyFormattedCount++;
                    }
                }
                count++;
                if (count % 100 == 0 && total > 0)
                {
                    var now = DateTime.UtcNow;
                    var seconds = (now - lastProgressTime).TotalSeconds;
                    lastProgressTime = now;
                    int percent = (int)(100.0 * count / total);
                    Logger.Instance.Log($"Loaded {count} of {total} results into memory for {inputfile} (last 100: {seconds:F2}s, badly formatted: {_badlyFormattedCount})");
                    _progressSink?.NotifyProgress(percent, $"Loaded {count} of {total} results into memory for {inputfile} (last 100: {seconds:F2}s, badly formatted: {_badlyFormattedCount})");
                }
            }
        }
        Logger.Instance.Log($"Finished loading results into memory for {inputfile} (badly formatted: {_badlyFormattedCount})");
        _progressSink?.NotifyProgress(100, $"Finished loading results into memory for {inputfile} (badly formatted: {_badlyFormattedCount})");
    }

    public List<ISearchResult> GetResults()
    {
        Logger.Instance.Log($"GetResults called for ETLProcessor, file: {inputfile}, results: {results.Count}");
        return results;
    }

    public async Task GetResultsWithCallback(Action<List<ISearchResult>> onBatch, CancellationToken cancellationToken = default, int batchSize = 1000)
    {
        var batch = new List<ISearchResult>(batchSize);
        foreach (var result in results)
        {
            if (cancellationToken.IsCancellationRequested) break;
            batch.Add(result);
            if (batch.Count >= batchSize)
            {
                onBatch(batch);
                batch = new List<ISearchResult>(batchSize);
            }
        }
        if (batch.Count > 0)
        {
            onBatch(batch);
        }
        await Task.CompletedTask;
    }

    public List<string> RegisterForExtensions()
    {
        Logger.Instance.Log("RegisterForExtensions called for ETLProcessor");
        return new List<string>() { ".etl", ".txt", ".log" };
    }

    public bool CheckFileFormat()
    {
        Logger.Instance.Log($"CheckFileFormat called for ETLProcessor, file: {inputfile}");
        if (inputfile.EndsWith(".txt") || inputfile.EndsWith(".log"))
        {
            using var reader = new StreamReader(inputfile);
            string? validLine = null;
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!line.StartsWith("Unknown("))
                {
                    validLine = line;
                    break;
                }
            }
            if (validLine == null)
            {
                Logger.Instance.Log($"All lines start with 'Unknown(', not a valid ETL format: {inputfile}");
                return false;
            }
            if (ETLLogLine.DoesHeaderLookRight(validLine))
            {
                Logger.Instance.Log($"File format looks right for .txt/.log: {inputfile}");
                return true;
            }
            else
            {
                Logger.Instance.Log($"File format does NOT look right for .txt/.log: {inputfile}");
                return false;
            }
        }
        else
        {
            Logger.Instance.Log($"Assuming file format is correct for .etl: {inputfile}");
        }
        return true;
    }

    public string GetPluginTextDescription() {
        return "Parses ETL and formatted ETL files";
    }
    public string GetPluginFriendlyName()
    {
        return "ETLProcessor";
    }
    public string GetPluginClassName()
    {
        return IPluginDescription.GetPluginClassNameBase(this);
    }

    public void SetProgressSink(SearchProgressSink sink)
    {
        Logger.Instance.Log($"SetProgressSink called for ETLProcessor, file: {inputfile}");
        _progressSink = sink;
    }

    public (TimeSpan? timeTaken, int? recordCount) GetSearchPerformanceEstimate(CancellationToken cancellationToken = default)
    {
        // Stub: no performance data available
        return (null, null);
    }
}
