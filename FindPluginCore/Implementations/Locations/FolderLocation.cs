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

namespace findneedle.Implementations;

public class FolderLocation : ISearchLocation, ICommandLineParser, IReportProgress
{
    public void Clone(ICommandLineParser parser)
    {
        //Keep nothing
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
    }

    private SearchProgressSink? _progressSink;
    public void SetProgressSink(SearchProgressSink sink)
    {
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

        //Report errors right away
        if (!procStats.metric.ContainsKey(path))
        {
            procStats.metric[path] = new Dictionary<string, string>();
            procStats.metric[path]["error"] = "Failed to open / denied";
        }
    } 

    public void SetExtensionProcessorList(List<IFileExtensionProcessor> processors)
    {
        lock (knownProcessors)
        {
            knownProcessors = processors;
            foreach(var processor in processors)
            {
                if (processor == null)
                {
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
    public override void LoadInMemory()
    {
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
        } else
        {
            isFile = false;
        }

        if (isFile)
        {
            if (sink != null)
            {
                sink.NotifyProgress("queuing up: " + path);
            }
            tasks.Add(Task.Run(() => ProcessFile(path)));
        }
        else
        {

            foreach (var file in FileIO.GetAllFiles(path, GetAllFilesErrorHandler))
            {
                if (sink != null)
                {
                    sink.NotifyProgress("queuing up: " + file);
                }
                tasks.Add(Task.Run(() => ProcessFile(file)));
            }
        }
        if (sink != null)
        {
            sink.NotifyProgress("waiting for etl files to be processed");
        }
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
        tasks.Clear();
        if (sink != null)
        {
            sink.NotifyProgress("processed " + path);
        }
        
        
        
    }



    private void ProcessFile(string file)
    {
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
                if (processor == null)
                {
                    throw new Exception("null?");
                }
                processor.OpenFile(file);
                if (processor.CheckFileFormat())
                {
                    processor.DoPreProcessing();
                    processor.LoadInMemory();
                }
            }
           
        }

    }

    public override List<ISearchResult> Search(ISearchQuery? searchQuery)
    {
        var results = new List<ISearchResult>();
        lock (knownProcessors)
        {
            foreach (var item in knownProcessors)
            {
                if(item == null)
                {
                    continue; //bug!
                }
                results.AddRange(item.GetResults());
            }
        }

        var filteredResults = new List<ISearchResult>();
        foreach (var result in results)
        {
            var passAll = true;
            if (searchQuery != null)
            {
                foreach (var filter in searchQuery.GetFilters())
                {
                    if (!filter.Filter(result))
                    {
                        passAll = false;
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

    public CommandLineRegistration RegisterCommandHandler() 
    {
        var reg = new CommandLineRegistration()
        {
            handlerType = CommandLineHandlerType.Location,
            key = "path"
        };
        return reg;
    }
    public void ParseCommandParameterIntoQuery(string parameter) 
    {
        //Only one right now
        if (!Path.Exists(parameter) && !File.Exists(parameter))
        {
            throw new Exception("Path: " + parameter + " does not exist");
        }
        
        this.path = parameter;
    }

    public override void ClearStatistics() => throw new NotImplementedException();


    public override List<ReportFromComponent> ReportStatistics() {

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
        return reports;
    }
}
