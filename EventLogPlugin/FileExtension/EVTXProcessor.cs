global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Text;
global using System.Threading.Tasks;
global using System.Threading;
using findneedle.Implementations.Locations;
using FindNeedlePluginLib;
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
        return new();
    }

    public string GetFileName()
    {
        return inputfile;
    }
    public void DoPreProcessing() { }
    public void DoPreProcessing(CancellationToken cancellationToken) { }

    public List<ISearchResult> GetResults() => loc?.Search() ?? new();
    public void LoadInMemory()
    {
        LoadInMemory(CancellationToken.None);
    }
    public void LoadInMemory(CancellationToken cancellationToken)
    {
        if (loc != null)
        {
            loc.LoadInMemory(cancellationToken);
        }
    }

    public List<string> RegisterForExtensions()
    {
        return new() { ".evtx" };
    }

    public bool CheckFileFormat()
    {
        return true;
    }
}
