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

        public override void LoadInMemory(bool prefilter = false)
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

            }
            //throw new NotImplementedException();
        }

        List<FileExtensionProcessor> knownProcessors = new List<FileExtensionProcessor>();

        public void ProcessFile(string file)
        {
            string ext = Path.GetExtension(path).ToLower();
            switch (ext)
            {
                case ".etl":
                    ETLProcessor p = new ETLProcessor(file);
                    knownProcessors.Add(p);
                    p.DoPreProcessing();
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
            numRecordsInLastResult = results.Count;
            return results;
        }
    }
}
