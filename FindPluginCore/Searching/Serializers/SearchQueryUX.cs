using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle;
using findneedle.Implementations;
using findneedle.PluginSubsystem;
using FindNeedlePluginLib;

namespace FindPluginCore.Searching.Serializers;
public class SearchQueryUX
{
    private ISearchQuery? q = null;
    private PluginManager? pluginManager;
    private bool initalized = false;

    public List<IPluginDescription> GetLoadedPlugins()
    {
        Initialize();
        if (pluginManager == null)
        {
            throw new Exception("wtf");
        }
        return pluginManager.GetAllPluginsInstancesOfAType<IPluginDescription>().ToList();

    }

    public SearchStatistics GetSearchStatistics()
    {
        Initialize();
        if (q == null)
        {
            throw new Exception("wtf");
        }
        return q.GetSearchStatistics();
    }

    public SearchQueryUX()
    {
        Initialize();
    }

    public void Initialize()
    {
        if (!initalized)
        {
            pluginManager = PluginManager.GetSingleton(); // Initialize plugin manager if not already done
            pluginManager.LoadAllPlugins(true);
            q = SearchQueryFactory.CreateSearchQuery(pluginManager); // Initialize 'q' to ensure it is non-null
            initalized = true;

           
        }
    }

    public void UpdateSearchQuery()
    {
        Initialize();
        if(pluginManager == null)
        {
            throw new Exception("wtf");
        }
        q = SearchQueryFactory.CreateSearchQuery(pluginManager);
    }

    public void UpdateAllParameters(SearchLocationDepth depth, List<ISearchLocation> locations, List<ISearchFilter> filters, 
        List<IResultProcessor> processors, List<ISearchOutput> outputs, SearchStepNotificationSink stepnotifysink)
    {
        if(q == null)
        {
            throw new Exception("wtf");
        }
        q.Depth = depth;
        q.Filters = filters;
        q.Locations = locations;
        q.Processors = processors;
        q.Outputs = outputs;
        //q. = stepnotifysink;
    }

    public List<ISearchResult> GetSearchResults()
    {
        Initialize();
        if(q == null)
        {
            throw new Exception("wtf");
        }
        q.Step1_LoadAllLocationsInMemory();
        var x = q.Step2_GetFilteredResults();
        q.Step3_ResultsToProcessors();
        q.Step4_ProcessAllResultsToOutput();

        return x;
    }
}
