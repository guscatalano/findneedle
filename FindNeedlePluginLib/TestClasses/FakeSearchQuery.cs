using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FindNeedlePluginLib.TestClasses;


[ExcludeFromCodeCoverage]
public class FakeSearchQuery : ISearchQuery
{
    public List<ISearchFilter> Filters
    {
        get => [];
        set
        {

        }
    }
    public List<ISearchLocation> Locations
    {
        get => [];
        set
        {

        }
    }
    public List<IResultProcessor> Processors
    {
        get => [];
        set
        {

        }
    }
    public List<ISearchOutput> Outputs
    {
        get => [];
        set
        {
            
        }
    }
    public SearchLocationDepth Depth {
        get;
        set;
    }

    private SearchStepNotificationSink _stepnotifysink = new SearchStepNotificationSink();
    public SearchStepNotificationSink SearchStepNotificationSink {
        get => _stepnotifysink;
        set => _stepnotifysink = value;
    }

    private SearchStatistics _stats = new SearchStatistics();
    public SearchStatistics stats
    {
        get => _stats;
        set => _stats = value;
    }

    public string Name => throw new NotImplementedException();

    public void AddFilter(ISearchFilter filter) { }
    public List<ISearchFilter> GetFilters() {
        return []; 
    }
    public List<ISearchLocation> GetLocations() {
        return []; 
    }
    public SearchStatistics GetSearchStatistics() => throw new NotImplementedException();

    public void SetDepthForAllLocations(SearchLocationDepth depthForAllLocations) { /* no-op for fake */ }

    public void RunThrough()
    {
        throw new NotImplementedException();
    }

    public void Step1_LoadAllLocationsInMemory() => throw new NotImplementedException();
    public void Step1_LoadAllLocationsInMemory(System.Threading.CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public List<ISearchResult> Step2_GetFilteredResults() => throw new NotImplementedException();
    public List<ISearchResult> Step2_GetFilteredResults(System.Threading.CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public void Step3_ResultsToProcessors() => throw new NotImplementedException();
    public void Step4_ProcessAllResultsToOutput() => throw new NotImplementedException();
    public void Step5_Done() => throw new NotImplementedException();
    public void RunThrough(CancellationToken cancellationToken) => throw new NotImplementedException();
}
