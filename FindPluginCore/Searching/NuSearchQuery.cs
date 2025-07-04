﻿using System;
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
       
        int count = 1;
        int total = _locations.Count;
        foreach (var loc in _locations)
        {
            if (loc is FindNeedlePluginLib.Interfaces.IReportProgress reportable)
            {
                reportable.SetProgressSink(_stepnotifysink.progressSink);
            }
            int percent = total > 0 ? (int)(50.0 * count / total) : 0;
            _stepnotifysink.progressSink.NotifyProgress(percent, "loading location: " + loc.GetName());
            loc.LoadInMemory();
            count++;
        }
        _stepnotifysink.NotifyStep(SearchStep.AtLoad);
    }

    private List<ISearchResult>? _filteredResults;
    public List<ISearchResult> Step2_GetFilteredResults()
    {
        _stepnotifysink.NotifyStep(SearchStep.AtSearch);
        _filteredResults = new();
        int count = 1;
        int total = _locations.Count;
        foreach (var loc in _locations)
        {
            int percent = total > 0 ? 50 + (int)(50.0 * count / total) : 50;
            _stepnotifysink.progressSink.NotifyProgress(percent, "loading results: " + loc.GetName());
            loc.SetSearchDepth(_depth);
            var unfilteredResults = loc.Search();

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
            count++;
        }

        return _filteredResults;
    }

    public void Step3_ResultsToProcessors()
    {
        _stepnotifysink.NotifyStep(SearchStep.AtProcessor);
        foreach (var proc in _processors)
        {
            proc.ProcessResults(_currentResultList);
            Console.WriteLine("Output was written to: " + proc.GetOutputFile());
        }
    }

    public void Step4_ProcessAllResultsToOutput()
    {
        _stepnotifysink.NotifyStep(SearchStep.AtOutput);
        foreach (var output in _outputs)
        {
            output.WriteAllOutput(_currentResultList);
        }
    }

    public void Step5_Done()
    {
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
