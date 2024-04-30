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

        private IEnumerable<string> GetAllFiles(string path)
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
                    Console.Error.WriteLine(ex);
                }
                string[] files = null;
                try
                {
                    files = Directory.GetFiles(path);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
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

        public override void LoadInMemory(bool prefilter, SearchQuery searchQuery)
        {
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
                foreach(string file in GetAllFiles(path))
                {
                    tasks.Add(Task.Run(() => ProcessFile(file)));
                    //ProcessFile(file);
                }
                Task.WhenAll(tasks).Wait();
            }

            ReportFromComponent x = new ReportFromComponent()
            {
                component = "FolderLocation",
                step = SearchStatisticStep.AtLoad,
                summary = "ExtensionProviders",
                metric = new Dictionary<string, dynamic>()
            };

            ReportFromComponent y = new ReportFromComponent()
            {
                component = "FolderLocation",
                step = SearchStatisticStep.AtLoad,
                summary = "ProviderByFile",
                metric = new Dictionary<string, dynamic>()
            };
            foreach (var p in knownProcessors)
            {
                string name = p.GetType().ToString();
                if (!x.metric.ContainsKey(name)) {
                    x.metric[name] = 1;
                } else
                {
                    x.metric[name] = x.metric[name] + 1;
                }

                foreach(string provider in p.GetProviderCount().Keys)
                {
                    if (!y.metric.ContainsKey(provider))
                    {
                        y.metric[provider] = new Dictionary<string, int>();
                    }
                    
                    y.metric[provider].Add(p.GetFileName(), p.GetProviderCount()[provider]);
                    
                }
            }
            
            searchQuery.GetSearchStatistics().ReportFromComponent(x);
            searchQuery.GetSearchStatistics().ReportFromComponent(y);
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
            string ext = Path.GetExtension(path).ToLower();

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
                    if (GetSearchDepth() != SearchLocationDepth.Shallow)
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
