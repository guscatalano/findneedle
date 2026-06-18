using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

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

    // New overloads for cancellation support
    public void LoadInMemory(CancellationToken cancellationToken);
    public void DoPreProcessing(CancellationToken cancellationToken);

    public List<ISearchResult> GetResults();
    // New: callback-based batch results
    public Task GetResultsWithCallback(Action<List<ISearchResult>> onBatch, CancellationToken cancellationToken = default, int batchSize = 1000);

    public string GetFileName();
    public Dictionary<string, int> GetProviderCount();

    /// <summary>
    /// Per-file decode diagnostics for the Statistics page (e.g. how an .etl was decoded and the
    /// resulting counts). Default empty — processors that have nothing interesting can ignore it.
    /// Keys are short labels (e.g. "method", "rows", "formatErrors"); values are display strings.
    /// </summary>
    public Dictionary<string, string> GetDecodeInfo() => new();

    // New: search performance estimate
    public (TimeSpan? timeTaken, int? recordCount) GetSearchPerformanceEstimate(CancellationToken cancellationToken = default);
}
