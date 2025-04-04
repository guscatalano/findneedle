using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using findneedle.Interfaces;
using FindNeedleCoreUtils;
using FindNeedlePluginLib.Implementations.SearchNotifications;
using FindNeedlePluginLib.Interfaces;

namespace findneedle.Implementations;


public class FolderLocation : ISearchLocation, ICommandLineParser
{
    public SearchProgressSink? sink;
    public SearchStatistics? stats;


    public bool isFile
    { get; set;
     }

    public string path
    {
        get; set;
    }

    [JsonConstructorAttribute]
    public FolderLocation(string path = "", bool isFile=false,  int numRecordsInLastResult = 0, int numRecordsInMemory = 0, SearchLocationDepth depth = SearchLocationDepth.Intermediate)
    {
        procStats = new ReportFromComponent()
        {
            component = this.GetType().Name,
            step = SearchStep.AtLoad,
            summary = "statsByFile",
            metric = new Dictionary<string, dynamic>()
        };
        this.path = path;
        if(File.Exists(path))
        {
#pragma warning disable IDE0059 // Unnecessary assignment of a value
            isFile = true;
#pragma warning restore IDE0059 // Unnecessary assignment of a value
        }
    }

    public override string GetDescription()
    {
        return "file/folder";   
    }
    public override string GetName()
    {
        return path;
    }

    ReportFromComponent procStats;


    public void GetAllFilesErrorHandler(string path)
    {

        //Report errors right away
        if (!procStats.metric.ContainsKey(path))
        {
            procStats.metric[path] = new Dictionary<string, string>();
            procStats.metric[path]["error"] = "Failed to open / denied";
        }
    } 

    public List<Task> tasks = new();
    public override void LoadInMemory()
    {
        procStats = new ReportFromComponent()
        {
            component = this.GetType().Name,
            step = SearchStep.AtLoad,
            summary = "statsByFile",
            metric = new Dictionary<string, dynamic>()
        };

        knownProcessors = new List<IFileExtensionProcessor>();
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
        
        CalculateStats(procStats);
        
    }

    public void QueueNewFolder(string path, bool startWait = false)
    {
        List<Task> myTasks = new List<Task>();
        foreach (var file in FileIO.GetAllFiles(path, GetAllFilesErrorHandler))
        {
            if (sink != null)
            {
                sink.NotifyProgress("queuing up: " + file);
            }
            myTasks.Add(Task.Run(() => ProcessFile(file)));
        }

        if (startWait)
        {
            if (sink != null)
            {
                sink.NotifyProgress("waiting for etl files to be processed");
            }
            var completed = 0;
            while (completed < myTasks.Count)
            {
                completed = myTasks.Where(x => x.IsCompleted).Count();
                if (sink != null)
                {
                    sink.NotifyProgress("waiting for etl files to be processed " + completed + " / " + tasks.Count);
                }
                Thread.Yield();
            }
            Task.WhenAll(myTasks).Wait();
            myTasks.Clear();
            if (sink != null)
            {
                sink.NotifyProgress("processed " + path);
            }
        }
    }

    public void CalculateStats(ReportFromComponent procStats)
    {
        //Do reports

        ReportFromComponent extensionProviderReport = new ReportFromComponent()
        {
            component = this.GetType().Name,
            step = SearchStep.AtLoad,
            summary = "ExtensionProviders",
            metric = new Dictionary<string, dynamic>()
        };

        ReportFromComponent ProviderByFileReport = new ReportFromComponent()
        {
            component = this.GetType().Name,
            step = SearchStep.AtLoad,
            summary = "ProviderByFile",
            metric = new Dictionary<string, dynamic>()
        };


        foreach (var p in knownProcessors)
        {
            if(p == null)
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
                if (name.Contains("ETLProcessor"))
                {
                    /*
                    ETLProcessor etlproc = (ETLProcessor)p;
                    procStats.metric[p.GetFileName()] = new Dictionary<string, string>();
                    procStats.metric[p.GetFileName()]["unknown"] = etlproc.currentResult.TotalFormatsUnknown + "";
                    procStats.metric[p.GetFileName()]["time"] = etlproc.currentResult.TotalElapsedTime + "";
                    procStats.metric[p.GetFileName()]["errors"] = etlproc.currentResult.TotalFormatErrors + "";
                    procStats.metric[p.GetFileName()]["buffers"] = etlproc.currentResult.TotalBuffersProcessed + "";
                    procStats.metric[p.GetFileName()]["lost"] = etlproc.currentResult.TotalEventsLost + "";
                    procStats.metric[p.GetFileName()]["events"] = etlproc.currentResult.TotalEventsProcessed + "";*/
                }
                else
                {
                    procStats.metric[p.GetFileName()] = new Dictionary<string, string>();
                }

            }
            else
            {
                extensionProviderReport.metric[name] = extensionProviderReport.metric[name] + 1;
            }
        }

        if (stats != null)
        {
            stats.ReportFromComponent(extensionProviderReport);
            stats.ReportFromComponent(ProviderByFileReport);
            stats.ReportFromComponent(procStats);
        }
    }

    List<IFileExtensionProcessor> knownProcessors = new();

    bool IsDigitsOnly(string str)
    {
        foreach (var c in str)
        {
            if (c < '0' || c > '9')
                return false;
        }

        return true;
    }

    public void ProcessFile(string file)
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
        
        switch (ext)
        {
            case ".etl":
                /*
                ETLProcessor p = new ETLProcessor(file);
                if(p == null)
                {
                    return; //failed to rpcoess handle it later.
                }
                knownProcessors.Add(p);
                p.DoPreProcessing();

                //this doesn't work :(
                if (GetSearchDepth() != SearchLocationDepth.Shallow || true)
                {
                    p.LoadInMemory();
                }*/
                break;
            case ".txt":
                break;
            case ".zip":/*
                ZipProcessor pz = new ZipProcessor(this);
                pz.OpenFile(file);
                if (pz == null)
                {
                    return;
                }
                knownProcessors.Add(pz);
                pz.DoPreProcessing();
                pz.LoadInMemory();*/
                break;
            case ".7z":
               
                break;
            case ".evtx":
                //Remember we have a native one!
                /*
                EVTXProcessor px = new EVTXProcessor(file);
                if (px == null)
                {
                    return;
                }
                knownProcessors.Add(px);
                px.DoPreProcessing();
                px.LoadInMemory();
                */
                break;
            case ".dmp":
                break;
        }
    }

    public override List<ISearchResult> Search(ISearchQuery? searchQuery)
    {
        List<ISearchResult> results = new List<ISearchResult>();
        lock (knownProcessors)
        {
            foreach (IFileExtensionProcessor item in knownProcessors)
            {
                if(item == null)
                {
                    continue; //bug!
                }
                results.AddRange(item.GetResults());
            }
        }

        List<ISearchResult> filteredResults = new List<ISearchResult>();
        foreach (ISearchResult result in results)
        {
            var passAll = true;
            if (searchQuery != null)
            {
                foreach (ISearchFilter filter in searchQuery.GetFilters())
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
    public override ReportFromComponent ReportStatistics() => throw new NotImplementedException();
}
