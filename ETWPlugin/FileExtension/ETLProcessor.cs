using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
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
    private static void LogInfo(string message)
    {
        // Use reflection to log info if Logger.Instance is available
        var loggerType = Type.GetType("FindPluginCore.Logger, FindPluginCore");
        if (loggerType != null)
        {
            var instanceProp = loggerType.GetProperty("Instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            var logMethod = loggerType.GetMethod("Log");
            var loggerInstance = instanceProp?.GetValue(null);
            logMethod?.Invoke(loggerInstance, new object[] { message });
        }
    }

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
        LogInfo("ETLProcessor constructed");
        currentResult = new TraceFmtResult(); //empty
        tempPath = TempStorage.GetNewTempPath("etl");
    }

    public void Dispose()
    {
        LogInfo($"Disposing ETLProcessor for file: {inputfile}");
        TempStorage.DeleteSomeTempPath(tempPath);
    }

    public void OpenFile(string fileName)
    {
        LogInfo($"OpenFile called in ETLProcessor for file: {fileName}");
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
        LogInfo($"DoPreProcessing started for file: {inputfile}");
        _progressSink?.NotifyProgress(0, $"Preprocessing {inputfile}");
        var getLock = 50;

        if (inputfile.EndsWith(".txt") || inputfile.EndsWith(".log"))
        {
            LogInfo($"Input file is .txt or .log, skipping TraceFmt: {inputfile}");
            currentResult.ProcessedFile = inputfile;
            currentResult.outputfile = inputfile;
            currentResult.summaryfile = inputfile;
        }
        else
        {
            LogInfo($"Calling TraceFmt.ParseSimpleETL for file: {inputfile}");
            currentResult = TraceFmt.ParseSimpleETL(inputfile, tempPath, _progressSink);
            if (currentResult == null)
            {
                LogInfo($"TraceFmt result is null for {inputfile}, skipping ETL processing.");
                _progressSink?.NotifyProgress(100, $"TraceFmt not found or failed for {inputfile}, skipping ETL processing.");
                return;
            }
        }
        _progressSink?.NotifyProgress(20, "Parsing output file");
        while (getLock > 0)
        {
            try
            {
                if (currentResult.outputfile == null)
                {
                    LogInfo($"Output file is not set for {inputfile}");
                    throw new InvalidOperationException("Output file is not set.");
                }
                using var fileStream = File.OpenRead(currentResult.outputfile);
                using var streamReader = new StreamReader(fileStream, Encoding.UTF8, false); //change buffer if there's perf reasons

                string? line;
                int lineCount = 0;
                while ((line = streamReader.ReadLine()) != null)
                {
                    var failsafe = 10;
                    while (!ETLLogLine.DoesHeaderLookRight(line) && failsafe > 0)
                    {
                        if (line.StartsWith("Unknown"))
                        {
                            failsafe = 0; //This is corrupted, let's just bail;
                            LogInfo($"Corrupted line detected in {inputfile}: {line}");
                            continue;
                        }
                        //line is not complete!
                        failsafe--;
                        line += streamReader.ReadLine();
                    }
                    if (failsafe == 0)
                    {
                        LogInfo($"Failsafe triggered, skipping line in {inputfile}");
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
                        LogInfo($"Processed {lineCount} lines for {inputfile}");
                        _progressSink?.NotifyProgress(20 + (int)(70.0 * lineCount / 100000), $"Processed {lineCount} lines");
                    }
                }
                LogInfo($"Finished reading output file for {inputfile}, total lines: {lineCount}");
                _progressSink?.NotifyProgress(90, $"Finished reading output file, total lines: {lineCount}");
                break;
            }
            catch (Exception ex)
            {
                LogInfo($"Exception while reading output file for {inputfile}: {ex.Message}");
                Thread.Sleep(100);
                getLock--; // Sometimes tracefmt can hold the lock, wait until file is ready
            }
        }
        LogInfo($"DoPreProcessing complete for {inputfile}");
        _progressSink?.NotifyProgress(100, $"Preprocessing complete for {inputfile}");
    }

    readonly List<ISearchResult> results = new();
    public void LoadInMemory() 
    {
        LogInfo($"LoadInMemory called for ETLProcessor, file: {inputfile}");
        _badlyFormattedCount = 0;
        _progressSink?.NotifyProgress(0, $"Loading results into memory for {inputfile}");
        if (LoadEarly)
        {
            int total = results.Count;
            int count = 0;
            var lastProgressTime = DateTime.UtcNow;
            foreach(var result in results)
            {
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
                    LogInfo($"Loaded {count} of {total} results into memory for {inputfile} (last 100: {seconds:F2}s, badly formatted: {_badlyFormattedCount})");
                    _progressSink?.NotifyProgress(percent, $"Loaded {count} of {total} results into memory for {inputfile} (last 100: {seconds:F2}s, badly formatted: {_badlyFormattedCount})");
                }
            }
        }
        LogInfo($"Finished loading results into memory for {inputfile} (badly formatted: {_badlyFormattedCount})");
        _progressSink?.NotifyProgress(100, $"Finished loading results into memory for {inputfile} (badly formatted: {_badlyFormattedCount})");
    }

    public List<ISearchResult> GetResults()
    {
        LogInfo($"GetResults called for ETLProcessor, file: {inputfile}, results: {results.Count}");
        return results;
    }

    public List<string> RegisterForExtensions()
    {
        LogInfo("RegisterForExtensions called for ETLProcessor");
        return new List<string>() { ".etl", ".txt", ".log" };
    }

    public bool CheckFileFormat()
    {
        LogInfo($"CheckFileFormat called for ETLProcessor, file: {inputfile}");
        if (inputfile.EndsWith(".txt") || inputfile.EndsWith(".log"))
        {
            var firstline = File.ReadLines(inputfile).Take(1).ToList().First();
            if (ETLLogLine.DoesHeaderLookRight(firstline))
            {
                LogInfo($"File format looks right for .txt/.log: {inputfile}");
                return true;
            }
            else
            {
                LogInfo($"File format does NOT look right for .txt/.log: {inputfile}");
                return false;
            }
        }
        else
        {
            LogInfo($"Assuming file format is correct for .etl: {inputfile}");
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
        LogInfo($"SetProgressSink called for ETLProcessor, file: {inputfile}");
        _progressSink = sink;
    }
}
