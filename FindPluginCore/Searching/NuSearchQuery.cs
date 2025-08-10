using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using findneedle;
using FindNeedlePluginLib;
using FindPluginCore; // Add for Logger
using findneedle.PluginSubsystem;
using FindPluginCore.Implementations.Storage;
using FindNeedlePluginLib.Interfaces; // Fix missing ISearchStorage reference
using FindPluginCore.PluginSubsystem; // For StorageType and PluginConfig

namespace FindPluginCore.Searching;
public class NuSearchQuery : ISearchQuery
{
    public List<ISearchFilter> Filters
    {
        get => [];
        set
        {

        }
    }
    private readonly List<ISearchFilter> _filters;

    public List<IResultProcessor> Processors
    {
        get => _processors;
        set => _processors = value;
    }
    private List<IResultProcessor> _processors;

    public List<ISearchOutput> Outputs
    {
        get => [];
        set
        {

        }
    }
    private readonly List<ISearchOutput> _outputs;

    public List<ISearchLocation> Locations
    {
        get => _locations;
        set => _locations = value;
    }
    private List<ISearchLocation> _locations;

    public SearchLocationDepth Depth
    {
        get => _depth;
        set => _depth = value;
    }
    private SearchLocationDepth _depth;

    public SearchStatistics Statistics
    {
        get => _stats;
        set { /* can't set readonly field, but for interface compliance, do nothing or throw if needed */ }
    }
    public SearchStatistics stats
    {
        get => _stats;
        set { /* can't set readonly field, but for interface compliance, do nothing or throw if needed */ }
    }
    private readonly SearchStatistics _stats;

    public SearchStepNotificationSink SearchStepNotificationSink
    {
        get => _stepnotifysink;
        set => _stepnotifysink = value;
    }
    private SearchStepNotificationSink _stepnotifysink;

    public List<ISearchResult> CurrentResultList => _currentResultList;

    public string Name => throw new NotImplementedException();

    private List<ISearchResult> _currentResultList;

    private ISearchStorage _resultStorage; // Use ISearchStorage instead of InMemoryStorage

    public NuSearchQuery()
    {
        _filters = new();
        _outputs = new();
        _processors = new();
        _depth = SearchLocationDepth.Shallow;
        _locations = new();
        _currentResultList = new();
        _stats = new();
        _stepnotifysink = new();
        _stats.RegisterForNotifications(_stepnotifysink, this);
        _stepnotifysink.NotifyStep(SearchStep.AtLaunch);
        Logger.Instance.Log("NuSearchQuery constructed");
        _resultStorage = CreateStorage(CancellationToken.None);
    }

    // Remove duplicate declaration of filePath in CreateStorage
    private ISearchStorage CreateStorage(CancellationToken cancellationToken)
    {
        var config = PluginManager.GetSingleton().config;
        string filePath = _locations.Count > 0 ? _locations[0].GetName() : "default";
        switch (config?.SearchStorageType)
        {
            case StorageType.SqlLite:
                return new SqliteStorage(filePath);
            case StorageType.InMemory:
                return new InMemoryStorage();
            case StorageType.Auto:
            default:
                int totalRecords = 0;
                TimeSpan totalTime = TimeSpan.Zero;
                foreach (var loc in _locations)
                {
                    try
                    {
                        var perf = loc.GetSearchPerformanceEstimate(cancellationToken);
                        if (perf.recordCount.HasValue)
                            totalRecords += perf.recordCount.Value;
                        if (perf.timeTaken.HasValue)
                            totalTime += perf.timeTaken.Value;
                    }
                    catch (NotImplementedException)
                    {
                        totalRecords += 100;
                    }
                }
                // Heuristic: Use InMemory if < 100,000 records and estimated time < 30s, else SqlLite
                if (totalRecords < 100_000 && totalTime.TotalSeconds < 30)
                    return new InMemoryStorage();
                else
                    return new SqliteStorage(filePath);
        }
    }

    public void RunThrough()
    {
        RunThrough(CancellationToken.None);
    }

    public void RunThrough(CancellationToken cancellationToken)
    {
        Logger.Instance.Log("RunThrough (with cancellation) started");
        Step1_LoadAllLocationsInMemory(cancellationToken);
        _currentResultList = Step2_GetFilteredResults(cancellationToken);
        Step3_ResultsToProcessors();
        Step4_ProcessAllResultsToOutput();
        Step5_Done();
        Logger.Instance.Log("RunThrough (with cancellation) finished");
    }

    #region main functions
    public void Step1_LoadAllLocationsInMemory()
    {
        Step1_LoadAllLocationsInMemory(CancellationToken.None);
    }

    public void Step1_LoadAllLocationsInMemory(CancellationToken cancellationToken)
    {
        Logger.Instance.Log($"Step1_LoadAllLocationsInMemory (with cancellation): {_locations.Count} locations");
        int count = 1;
        int total = _locations.Count;
        var pluginManager = PluginManager.GetSingleton();
        foreach (var loc in _locations)
        {
            if (cancellationToken.IsCancellationRequested) return;
            Logger.Instance.Log($"Loading location {count}/{total}: {loc.GetName()}");
            if (loc is FindNeedlePluginLib.Interfaces.IReportProgress reportable)
            {
                reportable.SetProgressSink(_stepnotifysink.progressSink);
            }
            int percent = total > 0 ? (int)(50.0 * count / total) : 0;
            _stepnotifysink.progressSink.NotifyProgress(percent, "loading location: " + loc.GetName());
            loc.LoadInMemory(cancellationToken);
            Logger.Instance.Log($"Loaded location: {loc.GetName()}");
            try
            {
                var perf = loc.GetSearchPerformanceEstimate(cancellationToken);
                Logger.Instance.Log($"Performance estimate for {loc.GetName()}: time={perf.timeTaken}, records={perf.recordCount}");
            }
            catch (NotImplementedException)
            {
                Logger.Instance.Log($"Performance estimate not implemented for {loc.GetName()}");
            }
            count++;
        }
        _stepnotifysink.NotifyStep(SearchStep.AtLoad);
        Logger.Instance.Log("Step1_LoadAllLocationsInMemory (with cancellation) complete");
    }

    private List<ISearchResult>? _filteredResults;
    public List<ISearchResult> Step2_GetFilteredResults()
    {
        return Step2_GetFilteredResults(CancellationToken.None);
    }

    public List<ISearchResult> Step2_GetFilteredResults(CancellationToken cancellationToken)
    {
        Logger.Instance.Log("Step2_GetFilteredResults (with cancellation) started");
        _stepnotifysink.NotifyStep(SearchStep.AtSearch);
        _filteredResults = new();
        _resultStorage = CreateStorage(cancellationToken); // Use selected storage
        int count = 1;
        int total = _locations.Count;
        var pluginManager = PluginManager.GetSingleton();
        bool useSync = pluginManager.config?.UseSynchronousSearch ?? false;
        foreach (var loc in _locations)
        {
            if (cancellationToken.IsCancellationRequested) break;
            Logger.Instance.Log($"Filtering results for location {count}/{total}: {loc.GetName()}");
            int percent = total > 0 ? 50 + (int)(50.0 * count / total) : 50;
            _stepnotifysink.progressSink.NotifyProgress(percent, "loading results: " + loc.GetName());
            loc.SetSearchDepth(_depth);
            List<ISearchResult> rawResults = new();
            if (!useSync)
            {
                try
                {
                    loc.SearchWithCallback(batch => {
                        rawResults.AddRange(batch);
                        Logger.Instance.Log($"SearchWithCallback for {loc.GetName()} returned batch of {batch.Count} raw results");
                        _resultStorage.AddRawBatch(batch, cancellationToken);
                    }, cancellationToken).Wait();
                }
                catch (NotImplementedException)
                {
                    Logger.Instance.Log($"SearchWithCallback not implemented for {loc.GetName()}");
                }
            }
            else
            {
                try
                {
                    rawResults = loc.Search(cancellationToken);
                    Logger.Instance.Log($"Search for {loc.GetName()} returned {rawResults.Count} raw results");
                    _resultStorage.AddRawBatch(rawResults, cancellationToken);
                }
                catch (NotImplementedException)
                {
                    Logger.Instance.Log($"Search not implemented for {loc.GetName()}");
                }
            }
            // Filter and add to filtered batch
            var filteredBatch = new List<ISearchResult>();
            foreach (var result in rawResults)
            {
                if (cancellationToken.IsCancellationRequested) break;
                var passAllFilters = true;
                foreach (var filter in _filters)
                {
                    if (!filter.Filter(result))
                    {
                        passAllFilters = false;
                    }
                }
                if (passAllFilters)
                {
                    filteredBatch.Add(result);
                }
            }
            _resultStorage.AddFilteredBatch(filteredBatch, cancellationToken);
            Logger.Instance.Log($"Results stored for location: {loc.GetName()}");
            count++;
        }
        // Gather all filtered results from storage
        var allResults = new List<ISearchResult>();
        _resultStorage.GetFilteredResultsInBatches(batch => allResults.AddRange(batch), 1000, cancellationToken);
        _filteredResults = allResults;
        Logger.Instance.Log($"Step2_GetFilteredResults (with cancellation) complete: {_filteredResults.Count} total filtered results");
        return _filteredResults;
    }

    public void Step3_ResultsToProcessors()
    {
        
        Logger.Instance.Log("Step3_ResultsToProcessors skipped (due to ux)");
        _stepnotifysink.NotifyStep(SearchStep.AtProcessor);
        /* skipping
        foreach (var proc in _processors)
        {
            Logger.Instance.Log($"Processing results with processor: {proc.GetType().Name}");
            proc.ProcessResults(_currentResultList);
            Logger.Instance.Log($"Output was written to: {proc.GetOutputFile()}");
        }
        Logger.Instance.Log("Step3_ResultsToProcessors complete");*/
    }

    public void Step4_ProcessAllResultsToOutput()
    {
        Logger.Instance.Log("Step4_ProcessAllResultsToOutput started");
        _stepnotifysink.NotifyStep(SearchStep.AtOutput);
        foreach (var output in _outputs)
        {
            Logger.Instance.Log($"Writing all output with: {output.GetType().Name}");
            output.WriteAllOutput(_currentResultList);
        }
        Logger.Instance.Log("Step4_ProcessAllResultsToOutput complete");
    }

    public void Step5_Done()
    {
        Logger.Instance.Log("Step5_Done called");
        _stepnotifysink.NotifyStep(SearchStep.Total);
    }
    #endregion

    public void SetDepthForAllLocations(SearchLocationDepth depthForAllLocations)
    {
        foreach (var loc in _locations)
        {
            loc.SetSearchDepth(depthForAllLocations);
        }
    }

    //Consider rethinking this one
    public void AddFilter(ISearchFilter filter) 
    {
        _filters.Add(filter);
    }
    public List<ISearchFilter> GetFilters()
    {
        return _filters;
    }
    public SearchStatistics GetSearchStatistics()
    {
        return _stats;
    }
    public List<ISearchLocation> GetLocations()
    {
        return _locations;
    }
}
