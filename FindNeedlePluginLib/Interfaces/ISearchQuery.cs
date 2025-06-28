using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace FindNeedlePluginLib;
public interface ISearchQuery
{
    void AddFilter(ISearchFilter filter);

    List<ISearchFilter> GetFilters();

    SearchStatistics GetSearchStatistics();

    List<ISearchLocation> GetLocations();

    List<ISearchFilter> Filters {
        get; set; 
    }
    List<ISearchLocation> Locations
    {
        get; set;
    }
    List<IResultProcessor> Processors { get; set; }
    List<ISearchOutput> Outputs {
        get; set;
    }

    SearchLocationDepth Depth { get; set; }

    string Name { get; }

    // Step 1: Load all locations in memory
    void Step1_LoadAllLocationsInMemory();
    List<ISearchResult> Step2_GetFilteredResults();
    public void Step3_ResultsToProcessors();
    public void Step4_ProcessAllResultsToOutput();
    public void Step5_Done();

    //No matter the implementation, this function should run through every step
    void RunThrough();
}
