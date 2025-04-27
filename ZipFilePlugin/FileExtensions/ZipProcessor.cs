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
    Action<string>? newFolderCallback = null;
    private string newTempFolder = "";
    public ZipProcessor()
    {
        //this.parent = parent;

    }

    public bool CheckFileFormat()
    {
        return true;
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
        newTempFolder = temp;

        if (newFolderCallback != null)
        {
            newFolderCallback(temp);
        }
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

    public void RegisterForQueueNewFolderCallback(Action<string> callback)
    {
        newFolderCallback = callback;
    }

    public void Dispose()
    {
        if (!string.IsNullOrEmpty(newTempFolder))
        {
            TempStorage.DeleteSomeTempPath(newTempFolder);
        }
    }
}
