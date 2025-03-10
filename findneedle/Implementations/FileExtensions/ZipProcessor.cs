using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle.Interfaces;
using FindNeedleCoreUtils;

namespace findneedle.Implementations.FileExtensions;
public class ZipProcessor : IFileExtensionProcessor
{
    private string inputfile = "";
    private readonly FolderLocation parent;
    public ZipProcessor(FolderLocation parent)
    {
        this.parent = parent;

    }

    public void OpenFile(string fileName)
    {
        inputfile = fileName;
    }

    public List<string> RegisterForExtensions()
    {
        return new List<string>() { ".zip" };
    }

    public void DoPreProcessing() {
        if (inputfile == null)
        {
            return;
        }
        var temp = TempStorage.GetNewTempPath("zip");
        ZipFile.ExtractToDirectory(inputfile, temp);

        parent.QueueNewFolder(temp, true);
    }
    public string GetFileName() 
    {
        return inputfile;
    }
    public Dictionary<string, int> GetProviderCount()
    {
        return new Dictionary<string, int>(); //This has no results
    }
    public List<ISearchResult> GetResults()
    {
        return new List<ISearchResult>(); //This just expands zips provides no real results
    }

    public void LoadInMemory() 
    {
    }

    public void Dispose() => throw new NotImplementedException();
}
