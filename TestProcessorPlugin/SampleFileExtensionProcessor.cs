using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
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
    public void DoPreProcessing(CancellationToken cancellationToken)
    {
        hasDonePreProcessing = true;
        if (cancellationToken.IsCancellationRequested) return;
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

    public async Task GetResultsWithCallback(Action<List<ISearchResult>> onBatch, CancellationToken cancellationToken = default, int batchSize = 1000)
    {
        var batch = new List<ISearchResult>(batchSize);
        foreach (var result in GetResults())
        {
            if (cancellationToken.IsCancellationRequested) break;
            batch.Add(result);
            if (batch.Count >= batchSize)
            {
                onBatch(batch);
                batch = new List<ISearchResult>(batchSize);
            }
        }
        if (batch.Count > 0)
        {
            onBatch(batch);
        }
        await Task.CompletedTask;
    }

    public bool CheckFileFormat()
    {
        return true;
    }
    public void LoadInMemory()
    {
        hasLoaded = true;
    }
    public void LoadInMemory(CancellationToken cancellationToken)
    {
        hasLoaded = true;
        if (cancellationToken.IsCancellationRequested) return;
    }
    public void OpenFile(string fileName)
    {
        lastOpenedFile = fileName;
    }
    public List<string> RegisterForExtensions()
    {
        return [".txt"];
    }

    public (TimeSpan? timeTaken, int? recordCount) GetSearchPerformanceEstimate(CancellationToken cancellationToken = default)
    {
        // Stub: no performance data available
        return (null, null);
    }
}
