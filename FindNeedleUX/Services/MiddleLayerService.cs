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
using FindPluginCore.Diagnostics;
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

    /// <summary>
    /// Optional storage-tier override applied to the next search (null = use config/Auto). Set by
    /// the command-line load hook (--storage=…) so the storage backend can be exercised directly.
    /// </summary>
    public static StorageType? StorageOverride { get; set; }

    /// <summary>
    /// Optional row-count estimate handed to the Auto tier for the next search (null = let the
    /// plugins estimate). Set by the command-line load hook (--estimate=…) to drive Auto with a
    /// known-good or deliberately-wrong prediction. Only meaningful when storage is Auto.
    /// </summary>
    public static int? RowEstimateOverride { get; set; }

    /// <summary>
    /// Optional cache-reuse mode for the next search (null = use the persisted setting). Set by the
    /// command-line load hook (--cache=on|off) so fresh / cache-hit / cache-disabled can be exercised.
    /// </summary>
    public static CacheReuseMode? CacheModeOverride { get; set; }

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

    /// <summary>Add a pre-built non-folder location (e.g. a live Kusto cluster).</summary>
    public static void AddLocation(ISearchLocation location)
    {
        if (location == null) return;
        Locations.Add(location);
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

            // Push the user's cache-reuse preference + prompt callback onto the freshly-created
            // NuSearchQuery. Step 1 honors the mode (Always / Never / Prompt) and, when Prompt,
            // invokes the callback to ask the user before reusing.
            if (SearchQueryUX.CurrentQuery is NuSearchQuery nu)
            {
                nu.CacheReuseMode = CacheModeOverride ?? ResultsViewerSettings.CacheReuseMode;
                nu.CacheReusePrompt = CacheReusePromptService.Prompt;
                // Apply per-run storage / estimate overrides (set by the CLI load hook). These let
                // the storage backend and the Auto-tier prediction be driven explicitly.
                if (StorageOverride.HasValue) nu.OverrideStorageType = StorageOverride.Value;
                if (RowEstimateOverride.HasValue) nu.EstimatedRowCountOverride = RowEstimateOverride.Value;
                // Lazy/Background modes defer the in-search FTS index build so the viewer opens
                // before the (potentially multi-minute) index finishes; Eager builds it in Step2.
                nu.DeferIndexBuild = EffectiveIndexingMode != FindPluginCore.Searching.IndexingMode.Eager;
            }
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
        // If a streaming search is in flight, its background task is still writing to a storage
        // we're about to replace + dispose. Stop it first so the next UpdateSearchQuery call's
        // storage cleanup doesn't yank the storage out from under a live writer.
        try { CurrentStreamingSearch?.Stop(); } catch { /* ignore */ }
        CurrentStreamingSearch = null;
        ClearOverrideStorage(); // a real search supersedes any cache being viewed
        // Stop any background index build from a previous search before we wipe/replace storage.
        CancelBackgroundIndexBuild();

        UpdateSearchQuery();
        var query = SearchQueryUX.CurrentQuery;
        FlowProgress.StartPlan(BuildFlowPlan(query));
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
        // Capture stats (component/decode breakdown + storage-backed counts) before the next
        // UpdateSearchQuery() swaps in a fresh query.
        SearchStatistics x = SearchQueryUX.GetSearchStatistics();
        if (SearchQueryUX.CurrentQuery is NuSearchQuery nuDone)
            CaptureStats(nuDone, GetSearchStorage());
        else
            LastStats = x;
        try { PerfReport.SetSource(string.Join(", ", Locations.Select(l => l.GetName()))); } catch { /* label only */ }

        // Background mode: Step2 skipped the FTS build so the viewer opens now; kick the batched
        // build off in the background (paging interleaves; substring search uses LIKE until ready).
        // Lazy mode does NOT build here — the viewer builds it on the first substring search.
        if (EffectiveIndexingMode == FindPluginCore.Searching.IndexingMode.Background && !IsSearchIndexBuilt)
            StartBackgroundIndexBuild();

        NotifyStateChanged();
        return Task.FromResult(x.GetSummaryReport());


    }

    /// <summary>
    /// The structured "why did this take so long" report for the most recently completed search +
    /// viewer load, or null if nothing has run yet. Surfaced on the Statistics page.
    /// </summary>
    public static SearchRunReport GetLastPerfReport() => PerfReport.Last;

    // ----- Deferred search-index (lazy/background) orchestration -----

    /// <summary>Per-run override of the indexing mode (set by the CLI --indexing hook). Null = use
    /// the persisted ResultsViewerSettings.IndexingMode (Lazy by default).</summary>
    public static FindPluginCore.Searching.IndexingMode? IndexingModeOverride { get; set; }

    public static FindPluginCore.Searching.IndexingMode EffectiveIndexingMode
        => IndexingModeOverride ?? ResultsViewerSettings.IndexingMode;

    /// <summary>True if substring search can use the fast FTS index now (vs the LIKE fallback).</summary>
    public static bool IsSearchIndexBuilt
        => (GetSearchStorage() as SqliteStorage)?.IsSearchIndexBuilt ?? true;

    /// <summary>Predicted FTS index build time (ms) for the current result set — for the "this will
    /// take a while" warning before a lazy build.</summary>
    public static long PredictSearchIndexMs()
        => SqliteStorage.PredictIndexBuildMs(GetFilteredRowCount());

    private static CancellationTokenSource _indexBuildCts;

    /// <summary>The in-flight background index build, if any (so the UI can show/await it).</summary>
    public static Task CurrentIndexBuild { get; private set; }

    /// <summary>Live progress of the current/last index build (rows). Read by the viewer's indicator;
    /// int (atomic reads) since row counts fit comfortably.</summary>
    public static int IndexBuildIndexed { get; private set; }
    public static int IndexBuildTotal { get; private set; }

    /// <summary>
    /// Build the FTS index in the background (batched, cancellable), reporting progress. No-op if the
    /// index is already built or there's nothing to index. Used by Background mode (after the viewer
    /// opens) — the viewer keeps paging while this runs (each batch releases the storage lock).
    /// </summary>
    public static Task StartBackgroundIndexBuild(Action<long, long> onProgress = null)
    {
        if (SearchQueryUX.CurrentQuery is not NuSearchQuery nu) return Task.CompletedTask;
        if (IsSearchIndexBuilt) return Task.CompletedTask;
        CancelBackgroundIndexBuild();
        var cts = new CancellationTokenSource();
        _indexBuildCts = cts;
        void Progress(long indexed, long totalRows)
        {
            IndexBuildIndexed = (int)indexed;
            IndexBuildTotal = (int)totalRows;
            onProgress?.Invoke(indexed, totalRows);
            NotifyStateChanged(); // viewer indicator refresh
        }
        CurrentIndexBuild = Task.Run(() =>
        {
            try { nu.BuildSearchIndexNow(Progress, cts.Token); }
            finally { NotifyStateChanged(); }
        }, cts.Token);
        return CurrentIndexBuild;
    }

    /// <summary>Cancel any in-flight background index build and wait briefly for it to stop (the
    /// batched build halts between batches, so this returns within ~one batch).</summary>
    public static void CancelBackgroundIndexBuild()
    {
        try { _indexBuildCts?.Cancel(); } catch { /* already disposed */ }
        try { CurrentIndexBuild?.Wait(10000); } catch { /* cancelled / faulted */ }
        _indexBuildCts = null;
        CurrentIndexBuild = null;
    }

    /// <summary>
    /// Build the FTS index synchronously on the calling thread (the Lazy "first substring search"
    /// path). Reports progress and is cancellable. No-op if already built.
    /// </summary>
    public static void EnsureSearchIndex(Action<long, long> onProgress = null, CancellationToken cancellationToken = default)
    {
        if (SearchQueryUX.CurrentQuery is NuSearchQuery nu && !IsSearchIndexBuilt)
            nu.BuildSearchIndexNow(onProgress, cancellationToken);
    }

    /// <summary>
    /// The phases this search+open will actually use, for the "Step X of N" status. Skipped phases
    /// (no cache check, no ETL, no consolidation, deferred index) are left out so N is accurate.
    /// The viewer adds OpenViewer/LoadFirstPage as it reaches them.
    /// </summary>
    private static IEnumerable<FlowPhase> BuildFlowPlan(ISearchQuery query)
    {
        var phases = new List<FlowPhase>();
        var cacheMode = CacheModeOverride ?? ResultsViewerSettings.CacheReuseMode;
        if (cacheMode != FindPluginCore.Searching.CacheReuseMode.Never && Locations.Count == 1)
            phases.Add(FlowPhase.CheckCache);
        phases.Add(FlowPhase.OpenLocations);
        if (Locations.Any(l => (l.GetName() ?? "").EndsWith(".etl", StringComparison.OrdinalIgnoreCase)))
            phases.Add(FlowPhase.DecodeEtl);
        phases.Add(FlowPhase.ReadParse);
        phases.Add(FlowPhase.StoreResults);
        bool hasConsumers = (query?.Processors?.Count ?? 0) > 0
                            || (query?.Outputs?.Count ?? 0) > 0
                            || (query?.RulesConfigPaths?.Count ?? 0) > 0;
        if (hasConsumers) phases.Add(FlowPhase.Consolidate);
        if (EffectiveIndexingMode == FindPluginCore.Searching.IndexingMode.Eager)
            phases.Add(FlowPhase.BuildIndex);
        phases.Add(FlowPhase.OpenViewer);
        phases.Add(FlowPhase.LoadFirstPage);
        return phases;
    }

    /// <summary>
    /// Stats from the most recently completed search, captured at completion. We can't just read
    /// <c>CurrentQuery.GetSearchStatistics()</c> because <see cref="UpdateSearchQuery"/> swaps in a
    /// fresh query (with un-snapped memory snapshots) for the next run — so by the time the
    /// Statistics page reads it, the run's numbers are gone. Mirrors how <c>PerfReport.Last</c> works.
    /// </summary>
    public static SearchStatistics LastStats { get; private set; }

    /// <summary>
    /// Capture the just-completed search's statistics: collect each location's component reports
    /// (provider/decode breakdown) into the stats, and set authoritative record counts from the
    /// result storage (the legacy per-location counters are unused by the streaming pipeline). Runs
    /// at search completion so per-file decode info (tracefmt vs TraceEvent, etc.) is fully populated.
    /// </summary>
    private static void CaptureStats(NuSearchQuery nu, FindNeedlePluginLib.Interfaces.ISearchStorage storage)
    {
        try
        {
            var stats = nu?.GetSearchStatistics();
            if (stats == null) { LastStats = null; return; }

            foreach (var loc in nu.GetLocations())
            {
                try
                {
                    var reports = loc.ReportStatistics();
                    if (reports != null)
                        foreach (var r in reports) stats.ReportFromComponent(r);
                }
                catch (Exception ex) { Logger.Instance.Log($"CaptureStats: {loc?.GetName()} reportstats failed: {ex.Message}"); }
            }

            try
            {
                if (storage != null)
                {
                    var s = storage.GetStatistics();
                    // RawResults is unused by the streaming pipeline (rows go straight to Filtered),
                    // so fall back to the filtered count for "loaded" when raw is 0.
                    int loaded = s.rawRecordCount > 0 ? s.rawRecordCount : s.filteredRecordCount;
                    stats.SetRecordCounts(loaded, s.filteredRecordCount);
                }
            }
            catch (Exception ex) { Logger.Instance.Log($"CaptureStats: record counts failed: {ex.Message}"); }

            LastStats = stats;
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"CaptureStats failed: {ex.Message}");
        }
    }

    public static SearchStatistics GetStats()
    {
        // Prefer the captured run; fall back to the current query (covers legacy SearchQuery and
        // NuSearchQuery — GetSearchStatistics() is on the ISearchQuery interface).
        return LastStats ?? SearchQueryUX.CurrentQuery?.GetSearchStatistics();
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
            test.Add(new LocationListItem()
            {
                Name = loc.GetName(),
                Description = loc.GetDescription(),
                IsEditable = loc is KustoPlugin.Location.KustoLocation,
            });
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
    /// True if the most recently completed search reused the on-disk cache instead of running
    /// a fresh scan. UX layer surfaces this in the status bar and the run-search summary so
    /// the user can tell whether they're looking at cached or freshly scanned data.
    /// </summary>
    public static bool LastSearchReusedCache
        => (SearchQueryUX?.CurrentQuery as NuSearchQuery)?.LastSearchReusedCache ?? false;

    /// <summary>
    /// The storage backing the most recent completed search, if any. Used by the result viewer
    /// to build a paged source (in-memory list, SQLite paging, or hybrid). Returns null until a
    /// search has run.
    /// </summary>
    // When the user opens a cached search for viewing, we point the viewer at that cache's storage
    // instead of the current query's. Cleared when a fresh search runs.
    private static FindNeedlePluginLib.Interfaces.ISearchStorage? _overrideStorage;

    public static FindNeedlePluginLib.Interfaces.ISearchStorage? GetSearchStorage()
    {
        if (_overrideStorage != null) return _overrideStorage;
        // NuSearchQuery exposes ResultStorage; older SearchQuery types don't.
        var query = SearchQueryUX?.CurrentQuery;
        var prop = query?.GetType().GetProperty("ResultStorage");
        return prop?.GetValue(query) as FindNeedlePluginLib.Interfaces.ISearchStorage;
    }

    /// <summary>
    /// Open a cached search (a SQLite .db from the cache directory) for viewing — without rescanning
    /// the original source. Points the viewer's storage at the cache; the caller then navigates to
    /// the result viewer. Works even if the original source file is gone.
    /// </summary>
    public static void OpenCachedResult(string dbPath)
    {
        try { CurrentStreamingSearch?.Stop(); } catch { /* ignore */ }
        CurrentStreamingSearch = null;
        CancelBackgroundIndexBuild();
        try { _overrideStorage?.Dispose(); } catch { /* ignore */ }
        _overrideStorage = FindPluginCore.Implementations.Storage.SqliteStorage.OpenExistingCache(dbPath);
        LastStats = null; // the cache has no live SearchStatistics
        NotifyStateChanged();
    }

    /// <summary>Drop any cache-viewing override so the next viewer load reads the live search again.</summary>
    private static void ClearOverrideStorage()
    {
        if (_overrideStorage == null) return;
        try { _overrideStorage.Dispose(); } catch { /* ignore */ }
        _overrideStorage = null;
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
        ClearOverrideStorage(); // a real search supersedes any cache being viewed

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
                CaptureStats(nu, storage); // decode done now → per-file decode info + counts are complete
                NotifyStateChanged();

                // Background mode: now that all rows are in storage, build the FTS index off the
                // critical path (same as the non-streaming RunSearch path). Without this, a streaming
                // search — e.g. a Kusto location — would never build its index, so substring search
                // stays on the slow LIKE scan forever.
                if (!cts.IsCancellationRequested
                    && EffectiveIndexingMode == FindPluginCore.Searching.IndexingMode.Background
                    && !IsSearchIndexBuilt)
                    StartBackgroundIndexBuild();
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
