using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FindNeedlePluginLib;
using FindNeedlePluginLib.TestClasses;

namespace TestProcessorPlugin;

[ExcludeFromCodeCoverage]
public class SampleFileExtensionProcessor : IFileExtensionProcessor
{
    public string lastOpenedFile = "";
    public bool hasLoaded = false;
    public bool hasDonePreProcessing = false;
    public void Dispose() => throw new NotImplementedException();
    public void DoPreProcessing()
    {
        hasDonePreProcessing = true;
    }
    public string GetFileName() {
        return lastOpenedFile; 
    }
    public Dictionary<string, int> GetProviderCount() {
        return new Dictionary<string, int>(); 
    }
    public List<ISearchResult> GetResults()
    {
        var list = new List<ISearchResult>();
        list.Add(new FakeSearchResult());
        list.Add(new FakeSearchResult());
        return list;
    }

    public bool CheckFileFormat()
    {
        return true;
    }
    public void LoadInMemory()
    {
        hasLoaded = true;
    }
    public void OpenFile(string fileName)
    {
        lastOpenedFile = fileName;
    }
    public List<string> RegisterForExtensions()
    {
        return [".txt"];
    }
}
