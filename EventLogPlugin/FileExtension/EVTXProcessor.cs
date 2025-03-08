using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle.Implementations.Locations;
using findneedle.Interfaces;
using FindNeedleCoreUtils;

namespace findneedle.Implementations.FileExtensions;
public class EVTXProcessor : IFileExtensionProcessor
{
    readonly string inputfile;
    readonly FileEventLogQueryLocation loc;
    public EVTXProcessor(string file)
    {
        inputfile = file;
        loc = new FileEventLogQueryLocation(inputfile);
    }

    public Dictionary<string, int> GetProviderCount()
    {
        return new Dictionary<string, int>();
    }

    public string GetFileName()
    {
        return inputfile;
    }
    public void DoPreProcessing() 
    {
    }
    
    public List<SearchResult> GetResults() 
    {
        return loc.Search(null);
    }
    public void LoadInMemory()
    {
        
        loc.LoadInMemory();
       
    }

    public List<string> RegisterForExtensions()
    {
        return new List<string>() { ".evtx" };
    }
}
