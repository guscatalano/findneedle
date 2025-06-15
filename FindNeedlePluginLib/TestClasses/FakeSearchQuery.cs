using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle;
using findneedle.Interfaces;
using FindNeedlePluginLib.Interfaces;

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

    public string Name => throw new NotImplementedException();

    public void AddFilter(ISearchFilter filter) { }
    public List<ISearchFilter> GetFilters() {
        return []; 
    }
    public List<ISearchLocation> GetLocations() {
        return []; 
    }
    public SearchStatistics GetSearchStatistics() => throw new NotImplementedException();

    public void RunThrough()
    {
        throw new NotImplementedException();
    }

    public void Step1_LoadAllLocationsInMemory() => throw new NotImplementedException();
    public List<ISearchResult> Step2_GetFilteredResults() => throw new NotImplementedException();
    public void Step3_ResultsToProcessors() => throw new NotImplementedException();
    public void Step4_ProcessAllResultsToOutput() => throw new NotImplementedException();
    public void Step5_Done() => throw new NotImplementedException();
}
