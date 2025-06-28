using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle;
using FindNeedlePluginLib;

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

    public SearchStatistics Statistics => _stats;
    private readonly SearchStatistics _stats;

    public SearchStepNotificationSink SearchStepNotificationSink => _stepnotifysink;
    private readonly SearchStepNotificationSink _stepnotifysink;

    public List<ISearchResult> CurrentResultList => _currentResultList;

    public string Name => throw new NotImplementedException();

    private List<ISearchResult> _currentResultList;

    public NuSearchQuery()
    {
        _filters = [];
        _outputs = [];
        _processors = [];
        _depth = SearchLocationDepth.Shallow;
        _locations = [];
        _currentResultList = [];
        _stats = new();
        _stepnotifysink = new();
        _stats.RegisterForNotifications(_stepnotifysink, this);
        _stepnotifysink.NotifyStep(SearchStep.AtLaunch);
    }

    public void RunThrough()
    {
        Step1_LoadAllLocationsInMemory();
        _currentResultList = Step2_GetFilteredResults();
        Step3_ResultsToProcessors();
        Step4_ProcessAllResultsToOutput();
        Step5_Done();
    }

    #region main functions
    public void Step1_LoadAllLocationsInMemory()
    {
        _stepnotifysink.NotifyStep(SearchStep.AtLoad);
        foreach(var loc in _locations)
        {
            loc.LoadInMemory();
        }
    }

    private List<ISearchResult>? _filteredResults;
    public List<ISearchResult> Step2_GetFilteredResults()
    {
        _stepnotifysink.NotifyStep(SearchStep.AtSearch);
        _filteredResults = new List<ISearchResult>();
        foreach (var loc in _locations)
        {
            loc.SetSearchDepth(_depth);
            var unfilteredResults = loc.Search();
           
            foreach (ISearchResult result in unfilteredResults)
            {
                var passAllFilters = true;
                
                foreach (ISearchFilter filter in _filters)
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
            
            
            
        }
        
        return _filteredResults;
    }

    public void Step3_ResultsToProcessors()
    {
        _stepnotifysink.NotifyStep(SearchStep.AtProcessor);
        foreach(var proc in _processors)
        {
            proc.ProcessResults(_currentResultList);
            Console.WriteLine("Output was written to: " + proc.GetOutputFile());
            
        }
    }

    public void Step4_ProcessAllResultsToOutput()
    {
        _stepnotifysink.NotifyStep(SearchStep.AtOutput);
        foreach(var output in _outputs)
        {
            output.WriteAllOutput(_currentResultList);
        }
    }

    public void Step5_Done()
    {
        _stepnotifysink.NotifyStep(SearchStep.Total);
    }
    #endregion

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
