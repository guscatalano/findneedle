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
    private string inputfile = "";
    private FileEventLogQueryLocation? loc;


    public void Dispose() => throw new NotImplementedException();

    public void OpenFile(string fileName)
    {
        inputfile = fileName;
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

    public List<ISearchResult> GetResults() => loc?.Search(null) ?? new List<ISearchResult>();
    public void LoadInMemory()
    {
        if (loc != null)
        {
            loc.LoadInMemory();
        }
       
    }

    public List<string> RegisterForExtensions()
    {
        return new List<string>() { ".evtx" };
    }
}
