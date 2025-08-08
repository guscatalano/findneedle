global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Text;
global using System.Threading.Tasks;
global using System.Threading;
using FindNeedlePluginLib.Interfaces;
using findneedle.Implementations.Locations;
using FindNeedlePluginLib;
using FindNeedleCoreUtils;

namespace findneedle.Implementations.FileExtensions;
public class EVTXProcessor : IFileExtensionProcessor, IReportProgress
{
    private string inputfile = "";
    private FileEventLogQueryLocation? loc;
    private SearchProgressSink? _progressSink;

    public void SetProgressSink(SearchProgressSink sink)
    {
        _progressSink = sink;
        if (loc is IReportProgress reportable)
        {
            reportable.SetProgressSink(sink);
        }
    }

    public void Dispose() => throw new NotImplementedException();

    public void OpenFile(string fileName)
    {
        inputfile = fileName;
        loc = new FileEventLogQueryLocation(inputfile);
        if (_progressSink != null && loc is IReportProgress reportable)
        {
            reportable.SetProgressSink(_progressSink);
        }
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
