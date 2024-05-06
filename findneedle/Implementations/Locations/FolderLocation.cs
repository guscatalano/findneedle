using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using findneedle.Implementations.FileExtensions;
using findneedle.Interfaces;
using findneedle.Utils;

namespace findneedle.Implementations
{

    public class FolderLocation : SearchLocation
    {

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
            this.path = path;
            if(File.Exists(path))
            {
                isFile = true;
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

        private IEnumerable<string> GetAllFiles(string path, ReportFromComponent procStats)
        {
            Queue<string> queue = new Queue<string>();
            queue.Enqueue(path);
            while (queue.Count > 0)
            {
                path = queue.Dequeue();
                try
                {
                    foreach (string subDir in Directory.GetDirectories(path))
                    {
                        queue.Enqueue(subDir);
                    }
                }
                catch (Exception ex)
                {

                    if (!procStats.metric.ContainsKey(path))
                    {
                        procStats.metric[path] = new Dictionary<string, string>();
                        procStats.metric[path]["error"] = "Failed to open / denied";
                    }
                    Console.Error.WriteLine(ex.ToString());
                }
                string[] files = null;
                try
                {
                    files = Directory.GetFiles(path);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                    if (!procStats.metric.ContainsKey(path))
                    {
                        procStats.metric[path] = new Dictionary<string, string>();
                        procStats.metric[path]["error"] = "Failed to open / denied";
                    }
                }
                if (files != null)
                {
                    for (int i = 0; i < files.Length; i++)
                    {
                        yield return files[i];
                    }
                }
            }
        }

        public void CalculateStats()
        {
        
        }

        public override void LoadInMemory(bool prefilter, SearchQuery searchQuery)
        {
            ReportFromComponent procStats = new ReportFromComponent()
            {
                component = this.GetType().Name,
                step = SearchStatisticStep.AtLoad,
                summary = "statsByFile",
                metric = new Dictionary<string, dynamic>()
            };


            knownProcessors = new List<FileExtensionProcessor>();
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
                ProcessFile(path);

            } else
            {
                List<Task> tasks = new List<Task>();
                foreach(string file in GetAllFiles(path, procStats))
                {
                    tasks.Add(Task.Run(() => ProcessFile(file)));
                    //ProcessFile(file);
                }
                Task.WhenAll(tasks).Wait();
            }
            CalculateStats(searchQuery, procStats);
            
        }

        public void CalculateStats(SearchQuery searchQuery, ReportFromComponent procStats)
        {
            //Do reports

            ReportFromComponent extensionProviderReport = new ReportFromComponent()
            {
                component = this.GetType().Name,
                step = SearchStatisticStep.AtLoad,
                summary = "ExtensionProviders",
                metric = new Dictionary<string, dynamic>()
            };

            ReportFromComponent ProviderByFileReport = new ReportFromComponent()
            {
                component = this.GetType().Name,
                step = SearchStatisticStep.AtLoad,
                summary = "ProviderByFile",
                metric = new Dictionary<string, dynamic>()
            };


            foreach (var p in knownProcessors)
            {
                string name = p.GetType().ToString();
                if (!extensionProviderReport.metric.ContainsKey(name))
                {
                    extensionProviderReport.metric[name] = 1;
                }
                else
                {
                    extensionProviderReport.metric[name] = extensionProviderReport.metric[name] + 1;
                }

                foreach (string provider in p.GetProviderCount().Keys)
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
                        ETLProcessor etlproc = (ETLProcessor)p;
                        procStats.metric[p.GetFileName()] = new Dictionary<string, string>();
                        procStats.metric[p.GetFileName()]["unknown"] = etlproc.currentResult.TotalFormatsUnknown + "";
                        procStats.metric[p.GetFileName()]["time"] = etlproc.currentResult.TotalElapsedTime + "";
                        procStats.metric[p.GetFileName()]["errors"] = etlproc.currentResult.TotalFormatErrors + "";
                        procStats.metric[p.GetFileName()]["buffers"] = etlproc.currentResult.TotalBuffersProcessed + "";
                        procStats.metric[p.GetFileName()]["lost"] = etlproc.currentResult.TotalEventsLost + "";
                        procStats.metric[p.GetFileName()]["events"] = etlproc.currentResult.TotalEventsProcessed + "";
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

            searchQuery.GetSearchStatistics().ReportFromComponent(extensionProviderReport);
            searchQuery.GetSearchStatistics().ReportFromComponent(ProviderByFileReport);
            searchQuery.GetSearchStatistics().ReportFromComponent(procStats);
        }

        List<FileExtensionProcessor> knownProcessors = new List<FileExtensionProcessor>();

        bool IsDigitsOnly(string str)
        {
            foreach (char c in str)
            {
                if (c < '0' || c > '9')
                    return false;
            }

            return true;
        }

        public void ProcessFile(string file)
        {
            string ext = Path.GetExtension(file).ToLower();

            if (file.Length > 10)
            {
                //Let's check that they are within the extension
                string last10 = file.Substring(file.Length - 10); // we pick 10 to get capture things like .etl.001
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
                    ETLProcessor p = new ETLProcessor(file);
                    knownProcessors.Add(p);
                    p.DoPreProcessing();

                    //this doesn't work :(
                    if (GetSearchDepth() != SearchLocationDepth.Shallow || true)
                    {
                        p.LoadInMemory();
                    }
                    break;
                case ".txt":
                    break;
                case ".zip":
                    break;
                case ".7z":
                    break;
                case ".evtx":
                    //Remember we have a native one!
                    break;
                case ".dmp":
                    break;
            }
        }

        public override List<SearchResult> Search(SearchQuery searchQuery)
        {
            List<SearchResult> results = new List<SearchResult>();
            foreach(FileExtensionProcessor item in knownProcessors)
            {
                results.AddRange(item.GetResults());
            }

            List<SearchResult> filteredResults = new List<SearchResult>();
            foreach (SearchResult result in results)
            {
                bool passAll = true;
                foreach (SearchFilter filter in searchQuery.GetFilters())
                {
                    if (!filter.Filter(result))
                    {
                        passAll = false;
                    }
                }
                if (passAll)
                {
                    filteredResults.Add(result);
                    numRecordsInLastResult++;
                }
            }
            return filteredResults;
            numRecordsInLastResult = filteredResults.Count;
            return results;
        }
    }
}
