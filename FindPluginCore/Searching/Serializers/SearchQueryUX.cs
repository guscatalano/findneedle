using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle;
using findneedle.Implementations;
using findneedle.PluginSubsystem;
using FindNeedlePluginLib;
using FindPluginCore.Diagnostics;
using System.Threading;

namespace FindPluginCore.Searching.Serializers;
public class SearchQueryUX
{
    private ISearchQuery? q = null;
    private PluginManager? pluginManager;
    private bool initalized = false;

    public ISearchQuery? CurrentQuery => q;

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

        // Release the previous query's SQLite storage before swapping. Without this, a search
        // that was cancelled (or even one that completed) leaves a SqliteStorage with an open
        // connection holding the cache .db file's lock. The next search on the same log file
        // (same CachedStorage path) then sticks in its own SqliteStorage ctor's ClearTables
        // waiting for the lock.
        try { (q as FindPluginCore.Searching.NuSearchQuery)?.DisposeStorage(); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SearchQueryUX: old storage dispose failed: {ex.Message}");
        }

        q = SearchQueryFactory.CreateSearchQuery(pluginManager);
    }

    public void UpdateAllParameters(SearchLocationDepth depth, List<ISearchLocation> locations, List<ISearchFilter> filters, 
        List<IResultProcessor> processors, List<ISearchOutput> outputs, SearchStepNotificationSink stepnotifysink, SearchStatistics stats)
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
        q.SearchStepNotificationSink = stepnotifysink;
        q.stats = stats;
    }

    public List<ISearchResult> GetSearchResults()
    {
        Initialize();
        if(q == null)
        {
            throw new Exception("wtf");
        }
        using var _ = PerfLog.Scope("search.run", ("entry", "ux_sync"));
        q.Step1_LoadAllLocationsInMemory();
        var x = q.Step2_GetFilteredResults();
        q.Step3_ResultsToProcessors();
        q.Step4_ProcessAllResultsToOutput();

        return x;
    }

    public List<ISearchResult> GetSearchResults(CancellationToken cancellationToken)
    {
        Initialize();
        if(q == null)
        {
            throw new Exception("wtf");
        }
        // Top-level wall-clock scope so the perf log captures total search time on the UI path
        // (which goes through here instead of NuSearchQuery.RunThrough).
        using var _ = PerfLog.Scope("search.run", ("entry", "ux_cancellable"));
        q.Step1_LoadAllLocationsInMemory(cancellationToken);
        var x = q.Step2_GetFilteredResults(cancellationToken);
        q.Step3_ResultsToProcessors();
        q.Step4_ProcessAllResultsToOutput(cancellationToken);
        return x;
    }
}
