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
    private volatile bool initalized = false;
    private readonly object _initLock = new();

    /// <summary>True once plugins are loaded and the query is created (a search ran, or the background warm
    /// finished). Lets callers do a pre-search read of <see cref="CurrentQuery"/> without forcing the load.</summary>
    public bool IsLoaded => initalized;

    /// <summary>Explicit name for <see cref="Initialize"/> — load all plugins + create the query, once. The
    /// first call pays the ~2s. UI-thread callers must await MiddleLayerService.PluginsReady first so this
    /// never runs inline on the UI thread (it would freeze the window).</summary>
    public void EnsureLoaded() => Initialize();

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
        // Cheap by design: plugin loading (~2s) is DEFERRED to Initialize()/EnsureLoaded, so constructing this
        // — or reading CurrentQuery for a pre-search status read — never loads plugins on the launch path.
        // (Was: eager Initialize() here, which made touching MiddleLayerService at launch cost ~2s.)
    }

    public void Initialize()
    {
        if (initalized) return;
        // Thread-safe + once: the background warm and a search can both call this; only the first loads.
        lock (_initLock)
        {
            if (initalized) return;
            pluginManager = PluginManager.GetSingleton();
            pluginManager.LoadAllPlugins(true);
            q = SearchQueryFactory.CreateSearchQuery(pluginManager); // create 'q' so CurrentQuery is non-null
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
        // SNAPSHOT, do not alias. These lists come straight from MiddleLayerService's process-wide
        // statics, and the search enumerates q.Locations on a background thread for the whole run. Storing
        // the caller's list by reference meant the UI thread adding a source (opening a second file) or
        // clearing the workspace mutated the very collection being enumerated — an
        // InvalidOperationException on the search thread, which nothing observes, so the load silently
        // died. A run should search the set it was started with; a later change belongs to a later run.
        q.Filters = filters == null ? null : new List<ISearchFilter>(filters);
        q.Locations = locations == null ? null : new List<ISearchLocation>(locations);
        q.Processors = processors == null ? null : new List<IResultProcessor>(processors);
        q.Outputs = outputs == null ? null : new List<ISearchOutput>(outputs);
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
