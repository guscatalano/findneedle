using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle.Implementations.Locations;
using findneedle.Interfaces;
using findneedle.WDK;

namespace findneedle.Implementations.FileExtensions;
public class EVTXProcessor : FileExtensionProcessor
{
    string inputfile;
    FileEventLogQueryLocation loc;
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
}
