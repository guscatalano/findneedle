using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using findneedle;
using FindNeedlePluginLib;
using FindPluginCore; // Add for Logger

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
    }

    public void RunThrough()
    {
        Logger.Instance.Log("RunThrough started");
        Step1_LoadAllLocationsInMemory();
        _currentResultList = Step2_GetFilteredResults();
        Step3_ResultsToProcessors();
        Step4_ProcessAllResultsToOutput();
        Step5_Done();
        Logger.Instance.Log("RunThrough finished");
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
        Logger.Instance.Log($"Step1_LoadAllLocationsInMemory: {_locations.Count} locations");
        int count = 1;
        int total = _locations.Count;
        foreach (var loc in _locations)
        {
            Logger.Instance.Log($"Loading location {count}/{total}: {loc.GetName()}");
            if (loc is FindNeedlePluginLib.Interfaces.IReportProgress reportable)
            {
                reportable.SetProgressSink(_stepnotifysink.progressSink);
            }
            int percent = total > 0 ? (int)(50.0 * count / total) : 0;
            _stepnotifysink.progressSink.NotifyProgress(percent, "loading location: " + loc.GetName());
            loc.LoadInMemory();
            Logger.Instance.Log($"Loaded location: {loc.GetName()}");
            count++;
        }
        _stepnotifysink.NotifyStep(SearchStep.AtLoad);
        Logger.Instance.Log("Step1_LoadAllLocationsInMemory complete");
    }

    public void Step1_LoadAllLocationsInMemory(CancellationToken cancellationToken)
    {
        Logger.Instance.Log($"Step1_LoadAllLocationsInMemory (with cancellation): {_locations.Count} locations");
        int count = 1;
        int total = _locations.Count;
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
            count++;
        }
        _stepnotifysink.NotifyStep(SearchStep.AtLoad);
        Logger.Instance.Log("Step1_LoadAllLocationsInMemory (with cancellation) complete");
    }

    private List<ISearchResult>? _filteredResults;
    public List<ISearchResult> Step2_GetFilteredResults()
    {
        Logger.Instance.Log("Step2_GetFilteredResults started");
        _stepnotifysink.NotifyStep(SearchStep.AtSearch);
        _filteredResults = new();
        int count = 1;
        int total = _locations.Count;
        foreach (var loc in _locations)
        {
            Logger.Instance.Log($"Filtering results for location {count}/{total}: {loc.GetName()}");
            int percent = total > 0 ? 50 + (int)(50.0 * count / total) : 50;
            _stepnotifysink.progressSink.NotifyProgress(percent, "loading results: " + loc.GetName());
            loc.SetSearchDepth(_depth);
            var unfilteredResults = loc.Search();
            Logger.Instance.Log($"{unfilteredResults.Count} results from location: {loc.GetName()}");

            foreach (var result in unfilteredResults)
            {
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
                    _filteredResults.Add(result);
                }
            }
            Logger.Instance.Log($"{_filteredResults.Count} results passed filters for location: {loc.GetName()}");
            count++;
        }
        Logger.Instance.Log($"Step2_GetFilteredResults complete: {_filteredResults.Count} total filtered results");
        return _filteredResults;
    }

    public List<ISearchResult> Step2_GetFilteredResults(CancellationToken cancellationToken)
    {
        Logger.Instance.Log("Step2_GetFilteredResults (with cancellation) started");
        _stepnotifysink.NotifyStep(SearchStep.AtSearch);
        _filteredResults = new();
        int count = 1;
        int total = _locations.Count;
        foreach (var loc in _locations)
        {
            if (cancellationToken.IsCancellationRequested) break;
            Logger.Instance.Log($"Filtering results for location {count}/{total}: {loc.GetName()}");
            int percent = total > 0 ? 50 + (int)(50.0 * count / total) : 50;
            _stepnotifysink.progressSink.NotifyProgress(percent, "loading results: " + loc.GetName());
            loc.SetSearchDepth(_depth);
            var unfilteredResults = loc.Search(cancellationToken);
            Logger.Instance.Log($"{unfilteredResults.Count} results from location: {loc.GetName()}");

            foreach (var result in unfilteredResults)
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
                    _filteredResults.Add(result);
                }
            }
            Logger.Instance.Log($"{_filteredResults.Count} results passed filters for location: {loc.GetName()}");
            count++;
        }
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
