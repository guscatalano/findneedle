using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FindNeedlePluginLib;
public interface IFileExtensionProcessor : IDisposable
{
    /* Every file extension processor must implement this interface.
     * It defines what extensions it can handle, they MUST start with .
     */
    public List<string> RegisterForExtensions();
    public void OpenFile(string fileName);

    //This is meant for extensions that are more generic like txt, double check that you can handle it and return false if you can't.
    public bool CheckFileFormat();

    public void LoadInMemory();
    public void DoPreProcessing();

    public List<ISearchResult> GetResults();

    public string GetFileName();
    public Dictionary<string, int> GetProviderCount();

    
}
