using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace findneedle.Interfaces;
public interface IFileExtensionProcessor : IDisposable
{
    /* Every file extension processor must implement this interface.
     * It defines what extensions it can handle, they MUST start with .
     */
    public List<string> RegisterForExtensions();
    public void OpenFile(string fileName);

    public void LoadInMemory();
    public void DoPreProcessing();

    public List<SearchResult> GetResults();

    public string GetFileName();
    public Dictionary<string, int> GetProviderCount();
}
