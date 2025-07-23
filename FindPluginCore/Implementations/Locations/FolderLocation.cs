using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using findneedle.PluginSubsystem;
using FindNeedleCoreUtils;
using FindNeedlePluginLib;
using FindNeedlePluginLib.Interfaces;
using FindPluginCore; // For Logger

namespace findneedle.Implementations;

public class FolderLocation : ISearchLocation, ICommandLineParser, IReportProgress
{
    public void Clone(ICommandLineParser parser)
    {
        //Keep nothing
        Logger.Instance.Log($"FolderLocation.Clone called for parser type: {parser?.GetType().Name}");
    }
    public SearchProgressSink? sink;
    public SearchStatistics? stats;

    [ExcludeFromCodeCoverage]
    public bool isFile
    { 
        get; set;
    }

    public string path
    {
        get; set;
    }

    public FolderLocation()
    {
        path = "invalidpath";
        procStats = new ReportFromComponent()
        {
            component = this.GetType().Name,
            step = SearchStep.AtLoad,
            summary = "statsByFile",
            metric = new Dictionary<string, dynamic>()
        };
        Logger.Instance.Log($"FolderLocation constructed with path: {path}");
    }

    private SearchProgressSink? _progressSink;
    public void SetProgressSink(SearchProgressSink sink)
    {
        Logger.Instance.Log($"SetProgressSink called for FolderLocation: {path}");
        _progressSink = sink;
        sink = _progressSink;
        foreach (var processor in knownProcessors)
        {
            if (processor is IReportProgress reportable)
            {
                reportable.SetProgressSink(sink);
            }
        }
    }

    [ExcludeFromCodeCoverage]
    public override string GetDescription()
    {
        return "file/folder";
    }

    [ExcludeFromCodeCoverage]
    public override string GetName()
    {
        return path;
    }

    ReportFromComponent procStats;


    [ExcludeFromCodeCoverage]
    private void GetAllFilesErrorHandler(string path)
    {
        Logger.Instance.Log($"Error accessing file or directory: {path}");
        //Report errors right away
        if (!procStats.metric.ContainsKey(path))
        {
            procStats.metric[path] = new Dictionary<string, string>();
            procStats.metric[path]["error"] = "Failed to open / denied";
        }
    } 

    public void SetExtensionProcessorList(List<IFileExtensionProcessor> processors)
    {
        Logger.Instance.Log($"SetExtensionProcessorList called with {processors?.Count} processors for FolderLocation: {path}");
        lock (knownProcessors)
        {
            knownProcessors = processors;
            foreach(var processor in processors)
            {
                if (processor == null)
                {
                    Logger.Instance.Log("Null processor found in SetExtensionProcessorList");
                    throw new Exception("null?");
                }
                var exts = processor.RegisterForExtensions();
                foreach(var ext in exts)
                {
                    if (!extToProcessor.ContainsKey(ext))
                    {
                        extToProcessor[ext] = new();
                    }
                    extToProcessor[ext].Add(processor);
                }
            }
        }
    }
    List<IFileExtensionProcessor> knownProcessors = [];
    private readonly Dictionary<string, List<IFileExtensionProcessor>> extToProcessor = new();

    private readonly List<Task> tasks = new();
    public override void LoadInMemory(System.Threading.CancellationToken cancellationToken = default)
    {
        Logger.Instance.Log($"LoadInMemory started for FolderLocation: {path}");
        procStats = new ReportFromComponent()
        {
            component = this.GetType().Name,
            step = SearchStep.AtLoad,
            summary = "statsByFile",
            metric = new Dictionary<string, dynamic>()
        };

        TempStorage.GetMainTempPath();
        
        if (File.Exists(path))
        {
            isFile = true;
            Logger.Instance.Log($"Path is a file: {path}");
        } else
        {
            isFile = false;
            Logger.Instance.Log($"Path is a directory: {path}");
        }

        if (isFile)
        {
            if (sink != null)
            {
                sink.NotifyProgress("queuing up: " + path);
            }
            Logger.Instance.Log($"Queuing file for processing: {path}");
            tasks.Add(Task.Run(() => ProcessFile(path, cancellationToken), cancellationToken));
        }
        else
        {
            Logger.Instance.Log($"Enumerating files in directory: {path}");
            foreach (var file in FileIO.GetAllFiles(path, GetAllFilesErrorHandler))
            {
                if (sink != null)
                {
                    sink.NotifyProgress("queuing up: " + file);
                }
                Logger.Instance.Log($"Queuing file for processing: {file}");
                tasks.Add(Task.Run(() => ProcessFile(file, cancellationToken), cancellationToken));
            }
        }
        if (sink != null)
        {
            sink.NotifyProgress("waiting for etl files to be processed");
        }
        Logger.Instance.Log($"Waiting for {tasks.Count} file processing tasks to complete in FolderLocation: {path}");
        var completed = 0;
        var initialCount = tasks.Count;
        while (completed < tasks.Count)
        {
            completed = tasks.Where(x => x.IsCompleted).Count();
            if (sink != null)
            {
                sink.NotifyProgress("waiting for etl files to be processed " + completed + " / " + tasks.Count);
            }
            Thread.Yield();
        }
        Task.WhenAll(tasks).Wait();
        Logger.Instance.Log($"All file processing tasks completed in FolderLocation: {path}");
        tasks.Clear();
        if (sink != null)
        {
            sink.NotifyProgress("processed " + path);
        }
    }

    private void ProcessFile(string file, System.Threading.CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return;
        Logger.Instance.Log($"Processing file: {file}");
        var ext = Path.GetExtension(file).ToLower();

        if (file.Length > 10)
        {
            //Let's check that they are within the extension
            var last10 = file.Substring(file.Length - 10); // we pick 10 to get capture things like .etl.001
            if (last10.IndexOf('.') != last10.LastIndexOf("."))
            {
                if (string.IsNullOrEmpty(ext))
                {
                    //extension is wrong, let's pick the other one.
                    ext = last10.Substring(last10.IndexOf("."), 10-last10.LastIndexOf('.'));
                }
            }
        }

        if (extToProcessor.ContainsKey(ext))
        {
            foreach(var processor in extToProcessor[ext])
            {
                if (cancellationToken.IsCancellationRequested) return;
                if (processor == null)
                {
                    Logger.Instance.Log($"Null processor found for extension {ext} in ProcessFile");
                    throw new Exception("null?");
                }
                Logger.Instance.Log($"Opening file {file} with processor {processor.GetType().Name}");
                processor.OpenFile(file);
                if (cancellationToken.IsCancellationRequested) return;
                if (processor.CheckFileFormat())
                {
                    Logger.Instance.Log($"File format valid for {file}, running DoPreProcessing and LoadInMemory");
                    processor.DoPreProcessing(cancellationToken);
                    if (cancellationToken.IsCancellationRequested) return;
                    processor.LoadInMemory(cancellationToken);
                }
                else
                {
                    Logger.Instance.Log($"File format invalid for {file} with processor {processor.GetType().Name}");
                }
            }
        }
        else
        {
            Logger.Instance.Log($"No processor found for extension {ext} for file {file}");
        }
    }

    public override List<ISearchResult> Search(System.Threading.CancellationToken cancellationToken = default)
    {
        Logger.Instance.Log($"Search called for FolderLocation: {path}");
        var results = new List<ISearchResult>();
        lock (knownProcessors)
        {
            foreach (var item in knownProcessors)
            {
                if(item == null)
                {
                    Logger.Instance.Log("Null processor found in Search");
                    continue; //bug!
                }
                Logger.Instance.Log($"Getting results from processor: {item.GetType().Name}");
                results.AddRange(item.GetResults());
            }
        }

        var filteredResults = new List<ISearchResult>();
        foreach (var result in results)
        {
            var passAll = true;
            // No searchQuery, so no filters applied here
            if (passAll)
            {
                filteredResults.Add(result);
                numRecordsInLastResult++;
            }
        }
        Logger.Instance.Log($"Search completed for FolderLocation: {path}, {filteredResults.Count} results found");
        return filteredResults;
    }

    public CommandLineRegistration RegisterCommandHandler() 
    {
        Logger.Instance.Log($"RegisterCommandHandler called for FolderLocation: {path}");
        var reg = new CommandLineRegistration()
        {
            handlerType = CommandLineHandlerType.Location,
            key = "path"
        };
        return reg;
    }
    public void ParseCommandParameterIntoQuery(string parameter) 
    {
        Logger.Instance.Log($"ParseCommandParameterIntoQuery called with parameter: {parameter}");
        //Only one right now
        if (!Path.Exists(parameter) && !File.Exists(parameter))
        {
            Logger.Instance.Log($"Path does not exist: {parameter}");
            throw new Exception("Path: " + parameter + " does not exist");
        }
        
        this.path = parameter;
        Logger.Instance.Log($"Path set to: {parameter}");
    }

    public override void ClearStatistics() => throw new NotImplementedException();


    public override List<ReportFromComponent> ReportStatistics() {
        Logger.Instance.Log($"ReportStatistics called for FolderLocation: {path}");
        var reports = new List<ReportFromComponent>();
        var extensionProviderReport = new ReportFromComponent()
        {
            component = this.GetType().Name,
            step = SearchStep.AtLoad,
            summary = "ExtensionProviders",
            metric = new Dictionary<string, dynamic>()
        };
        reports.Add(extensionProviderReport);

        var ProviderByFileReport = new ReportFromComponent()
        {
           component = this.GetType().Name,
           step = SearchStep.AtLoad,
           summary = "ProviderByFile",
           metric = new Dictionary<string, dynamic>()
        };
        reports.Add(ProviderByFileReport);
      
        
        foreach (var p in knownProcessors)
        {
            if (p == null)
            {
               Logger.Instance.Log("Null processor found in ReportStatistics");
               continue; //bug!
            }
            var name = p.GetType().ToString();
            if (!extensionProviderReport.metric.ContainsKey(name))
            {
               extensionProviderReport.metric[name] = 1;
            }
            else
            {
               extensionProviderReport.metric[name] = extensionProviderReport.metric[name] + 1;
            }

            foreach (var provider in p.GetProviderCount().Keys)
            {
               if (!ProviderByFileReport.metric.ContainsKey(provider))
               {
                   ProviderByFileReport.metric[provider] = new Dictionary<string, int>();
               }

               ProviderByFileReport.metric[provider].Add(p.GetFileName(), p.GetProviderCount()[provider]);

            }

            if (!procStats.metric.ContainsKey(p.GetFileName()))
            {
                procStats.metric[p.GetFileName()] = new Dictionary<string, string>();
            }
            else
            {
                extensionProviderReport.metric[name] = extensionProviderReport.metric[name] + 1;
            }
        }
        Logger.Instance.Log($"ReportStatistics completed for FolderLocation: {path}");
        return reports;
    }
}
