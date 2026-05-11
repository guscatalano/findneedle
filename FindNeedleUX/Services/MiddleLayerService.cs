using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using findneedle;
using findneedle.Implementations;
using FindNeedlePluginLib;
using findneedle.PluginSubsystem;
using FindNeedleCoreUtils;
using FindNeedleUX.ViewObjects;
using FindPluginCore.Searching;
using FindPluginCore.Searching.Serializers;
using FindPluginCore.Implementations.Storage;
using FindPluginCore.PluginSubsystem;
using FindNeedleUX.Services.PagedLogSource;
using Microsoft.UI.Xaml.Controls;

namespace FindNeedleUX.Services;
public class MiddleLayerService
{
    public static List<ISearchLocation> Locations = new();
    public static List<ISearchFilter> Filters = new();
    public static SearchQueryUX SearchQueryUX = new();

    public static event Action StateChanged;

    public static void NotifyStateChanged() => StateChanged?.Invoke();

    public static void AddFolderLocation(string location)
    {
        var folderloc = new FolderLocation() { path = location };
        //Setup file extension processors
        var extensions = PluginManager.GetSingleton().GetAllPluginsInstancesOfAType<IFileExtensionProcessor>();

        folderloc.SetExtensionProcessorList(extensions);
        Locations.Add(folderloc);
        NotifyStateChanged();
    }

    public static List<ISearchResult> GetSearchResults()
    {
        return SearchResults;
    }

    private static List<ISearchResult> SearchResults = new();

    public static void UpdateSearchQuery()
    {
        // Update the processor list in the query based on the current plugin config
        var pluginManager = PluginManager.GetSingleton();
        var config = pluginManager.config;
        var enabledProcessors = new List<IResultProcessor>();
        if (config != null)
        {
            foreach (var entry in config.entries)
            {
                if (entry.enabled)
                {
                    // Find the processor instance by name (FriendlyName or ClassName)
                    var processor = pluginManager.GetAllPluginsInstancesOfAType<IResultProcessor>()
                        .FirstOrDefault(p =>
                            p.GetType().Name == entry.name ||
                            (p.GetType().FullName != null && p.GetType().FullName.EndsWith(entry.name))
                        );
                    if (processor != null && !enabledProcessors.Contains(processor))
                        enabledProcessors.Add(processor);
                }
            }
        }
        var query = SearchQueryUX.CurrentQuery;
        if (query != null)
        {
            // Add RuleDSL processors for each rules config file
            if (query.RulesConfigPaths != null && query.RulesConfigPaths.Count > 0)
            {
                foreach (var rulesPath in query.RulesConfigPaths)
                {
                    if (System.IO.File.Exists(rulesPath))
                    {
                        var ruleDslProcessor = new FindNeedleRuleDSL.FindNeedleRuleDSLPlugin("*", rulesPath);
                        enabledProcessors.Add(ruleDslProcessor);
                        System.Diagnostics.Debug.WriteLine($"Added RuleDSL processor for: {rulesPath}");
                    }
                }
            }

            query.Processors = enabledProcessors;

            SearchQueryUX.UpdateSearchQuery();
            // Try to get SearchStepNotificationSink if possible
            SearchStepNotificationSink? sink = query?.SearchStepNotificationSink;
            SearchQueryUX.UpdateAllParameters(SearchLocationDepth.Intermediate, Locations, Filters, 
                query.Processors, query.Outputs, sink, query.stats);
        }
    }

    public static List<LogLine> GetLogLines()
    {
        // Fast path: the search consolidated rows into SearchResults (legacy + small-search
        // behaviour). Walk that list.
        var sr = SearchResults;
        if (sr != null && sr.Count > 0)
        {
            var lines = new List<LogLine>(sr.Count);
            int idx = 0;
            foreach (var r in sr) { lines.Add(new LogLine(r, idx++)); }
            return lines;
        }

        // Slow path: SearchResults is empty because the search skipped consolidation (large
        // result set with no rules / processors / outputs). Re-materialize from storage on
        // demand. Only the client-side web viewer hits this — server-side and the native viewer
        // use IPagedLogSource directly and never call GetLogLines.
        var storage = GetSearchStorage();
        if (storage == null) return new List<LogLine>();

        var fromStorage = new List<LogLine>(Math.Max(0, TrySafeFilteredCount(storage)));
        int sIdx = 0;
        try
        {
            storage.GetFilteredResultsInBatches(batch =>
            {
                foreach (var r in batch) { fromStorage.Add(new LogLine(r, sIdx++)); }
            }, 1000);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetLogLines lazy-materialize failed: {ex.Message}");
        }
        return fromStorage;
    }

    /// <summary>
    /// Row count of the filtered result set, preferring storage when SearchResults was skipped
    /// (large search with no Step3/Step4 consumers). Used by the status bar so the count is
    /// correct regardless of whether the search materialized the in-memory list.
    /// </summary>
    public static int GetFilteredRowCount()
    {
        var sr = SearchResults;
        if (sr != null && sr.Count > 0) return sr.Count;
        return TrySafeFilteredCount(GetSearchStorage());
    }

    private static int TrySafeFilteredCount(FindNeedlePluginLib.Interfaces.ISearchStorage storage)
    {
        if (storage == null) return 0;
        try { return storage.GetStatistics().filteredRecordCount; }
        catch { return 0; }
    }

    public static SearchProgressSink GetProgressEventSink()
    {
        var query = SearchQueryUX.CurrentQuery;
        return query != null ? query.SearchStepNotificationSink.progressSink : null;
    }

    public static Task<string> RunSearch(bool surfacescan = false, CancellationToken cancellationToken = default)
    {

        UpdateSearchQuery();
        var query = SearchQueryUX.CurrentQuery;
        if (query != null)
        {
            if (surfacescan)
            {
                query.SetDepthForAllLocations(SearchLocationDepth.Shallow);
            } else
            {
                query.SetDepthForAllLocations(SearchLocationDepth.Intermediate);
            }
        }
        if (cancellationToken != default)
        {
            SearchResults = SearchQueryUX.GetSearchResults(cancellationToken);
        }
        else
        {
            SearchResults = SearchQueryUX.GetSearchResults();
        }
        SearchStatistics x = SearchQueryUX.GetSearchStatistics();
        NotifyStateChanged();
        return Task.FromResult(x.GetSummaryReport());


    }

    public static SearchStatistics GetStats()
    {
        var query = SearchQueryUX.CurrentQuery;
        var searchQueryConcrete = query as SearchQuery;
        return searchQueryConcrete != null ? searchQueryConcrete.GetSearchStatistics() : null;
    }



    public static void OpenWorkspace(string filename)
    {
        var o = SearchQueryJsonReader.LoadSearchQuery(File.ReadAllText(filename));
        SearchQuery r = SearchQueryJsonReader.GetSearchQueryObject(o);
        Filters = r.Filters;
        Locations = r.Locations;

        foreach(ISearchLocation loc in Locations)
        {
            //Fix up the extension list
            if (loc is FolderLocation)
            {
                ((FolderLocation)loc).SetExtensionProcessorList(PluginManager.GetSingleton().GetAllPluginsInstancesOfAType<IFileExtensionProcessor>());
            }
        }
        UpdateSearchQuery();
        NotifyStateChanged();
    }

    public static void NewWorkspace()
    {
        Filters = new List<ISearchFilter>();
        Locations = new List<ISearchLocation>();

        UpdateSearchQuery();
        NotifyStateChanged();
    }

    public static void SaveWorkspace(string filename)
    {
        UpdateSearchQuery();
        var query = SearchQueryUX.CurrentQuery;
        var searchQueryConcrete = query as SearchQuery;
        if (searchQueryConcrete != null)
        {
            SerializableSearchQuery r = SearchQueryJsonReader.GetSerializableSearchQuery(searchQueryConcrete);
            var json = r.GetQueryJson();
            File.WriteAllText(filename, json);
        }
    }

    public static void RemoveLocationByName(string name)
    {
        var loc = Locations.FirstOrDefault(l => l.GetName() == name);
        if (loc != null)
        {
            Locations.Remove(loc);
            UpdateSearchQuery();
            NotifyStateChanged();
        }
    }

    public static ObservableCollection<LocationListItem> GetLocationListItems()
    {
        ObservableCollection<LocationListItem> test = new ObservableCollection<LocationListItem>();
        foreach (ISearchLocation loc in Locations)
        {
            test.Add(new LocationListItem() { Name = loc.GetName(), Description = loc.GetDescription() });
        }
        return test;
    }

    /// <summary>
    /// Gets the current query (NuSearchQuery) being used in the UI.
    /// </summary>
    public static ISearchQuery? GetCurrentQuery()
    {
        return SearchQueryUX?.CurrentQuery;
    }

    /// <summary>
    /// The storage backing the most recent completed search, if any. Used by the result viewer
    /// to build a paged source (in-memory list, SQLite paging, or hybrid). Returns null until a
    /// search has run.
    /// </summary>
    public static FindNeedlePluginLib.Interfaces.ISearchStorage? GetSearchStorage()
    {
        // NuSearchQuery exposes ResultStorage; older SearchQuery types don't.
        var query = SearchQueryUX?.CurrentQuery;
        var prop = query?.GetType().GetProperty("ResultStorage");
        return prop?.GetValue(query) as FindNeedlePluginLib.Interfaces.ISearchStorage;
    }

    /// <summary>
    /// Snapshot returned by <see cref="RunSearchStreaming"/>: the running search task, the
    /// cancel handle for the Stop button, and the live paged source the viewer reads from.
    /// </summary>
    public sealed class StreamingSearchHandle
    {
        public Task SearchTask { get; init; } = Task.CompletedTask;
        public CancellationTokenSource Cancellation { get; init; } = new();
        public IPagedLogSource Source { get; init; }
        public SqliteStorage Storage { get; init; }

        public void Stop() { try { Cancellation.Cancel(); } catch { /* already disposed */ } }
    }

    /// <summary>
    /// The most recent streaming search, kept alive for the viewer to pick up.
    /// </summary>
    public static StreamingSearchHandle? CurrentStreamingSearch { get; private set; }

    /// <summary>
    /// Streaming variant of <see cref="RunSearch"/>. Forces SQLite storage (only backend that's
    /// safe under concurrent read+write), constructs the storage on this thread, hands the
    /// viewer a paged source wired to it, and kicks off the actual search on the threadpool.
    /// The viewer can show partial results while the task runs and refreshes via the source's
    /// RowsAvailable event.
    /// </summary>
    public static StreamingSearchHandle RunSearchStreaming(bool surfaceScan = false)
    {
        // Cancel any in-flight streaming search before starting a new one. Without this, the
        // previous search's background task would keep producing into an orphaned SqliteStorage,
        // wasting CPU + disk and racing with the new search for the result handle.
        try { CurrentStreamingSearch?.Stop(); } catch { /* ignore */ }

        UpdateSearchQuery();
        if (SearchQueryUX.CurrentQuery is not NuSearchQuery nu)
            throw new InvalidOperationException(
                "Streaming search requires NuSearchQuery — the current ISearchQuery factory returned a different type.");

        nu.SetDepthForAllLocations(surfaceScan ? SearchLocationDepth.Shallow : SearchLocationDepth.Intermediate);
        nu.OverrideStorageType = StorageType.SqlLite;

        var cts = new CancellationTokenSource();
        var storage = (SqliteStorage)nu.PrepareStorage(cts.Token);
        var source = PagedLogSourceFactory.CreateStreaming(storage);

        var task = Task.Run(() =>
        {
            try
            {
                SearchResults = SearchQueryUX.GetSearchResults(cts.Token);
                NotifyStateChanged();
            }
            finally
            {
                // Always release the loading state, even on cancel/exception, so the viewer
                // hides its spinner and stops showing the Stop button.
                source.MarkLoadingComplete();
            }
        }, cts.Token);

        var handle = new StreamingSearchHandle
        {
            SearchTask = task,
            Cancellation = cts,
            Source = source,
            Storage = storage,
        };
        CurrentStreamingSearch = handle;
        return handle;
    }
}
