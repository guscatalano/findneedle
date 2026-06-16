using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using findneedle;
using FindNeedlePluginLib;
using FindPluginCore; // Add for Logger
using FindPluginCore.Diagnostics;
using findneedle.PluginSubsystem;
using FindPluginCore.Implementations.Storage;
using FindNeedlePluginLib.Interfaces; // Fix missing ISearchStorage reference
using FindPluginCore.PluginSubsystem; // For StorageType and PluginConfig
using findneedle.RuleDSL; // For RuleLoader and RuleEvaluationEngine
using FindNeedleRuleDSL; // For OutputRuleProcessor

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

    public List<string> RulesConfigPaths { get; set; } = new();
    public object? LoadedRules { get; set; }

    // SystemConfig support
    private FindNeedleRuleDSL.SystemConfig? _systemConfig;
    public FindNeedleRuleDSL.SystemConfig? SystemConfig
    {
        get => _systemConfig;
        set => _systemConfig = value;
    }

    // Rule processing components
    private readonly RuleLoader _ruleLoader = new();
    private readonly RuleEvaluationEngine _ruleEngine = new();
    private readonly OutputRuleProcessor _outputProcessor = new();

    public SearchStepNotificationSink SearchStepNotificationSink
    {
        get => _stepnotifysink;
        set => _stepnotifysink = value;
    }
    private SearchStepNotificationSink _stepnotifysink;

    public List<ISearchResult> CurrentResultList => _currentResultList;

    public string Name
    {
        get => "noname";
        set
        {
            // Optionally implement setter logic or leave empty for interface compliance
        }
    }

    private List<ISearchResult> _currentResultList;

    private ISearchStorage? _resultStorage; // Use ISearchStorage instead of InMemoryStorage

    /// <summary>
    /// The storage holding the search's raw + filtered results, available after the search has
    /// run. Used by the native result viewer to build a paged source (in-memory list, SQLite
    /// queries, or hybrid) without needing to re-materialize every row.
    /// </summary>
    public ISearchStorage? ResultStorage => _resultStorage;

    /// <summary>
    /// When set, <see cref="CreateStorage"/> uses this storage type instead of the one in
    /// <c>PluginManager.config.SearchStorageType</c>. Streaming searches set this to
    /// <see cref="StorageType.SqlLite"/> because the viewer reads while plugins are still
    /// writing and only SqliteStorage is safe under concurrent read+write (lock-protected).
    /// </summary>
    public StorageType? OverrideStorageType { get; set; }

    /// <summary>
    /// When set, the Auto storage tier uses this row count instead of asking the locations for a
    /// performance estimate. Lets callers (and tests) drive the Auto decision with a known-good or
    /// deliberately-wrong prediction. Ignored unless the resolved storage type is Auto.
    /// </summary>
    public int? EstimatedRowCountOverride { get; set; }

    /// <summary>
    /// How the cache-reuse fast path in Step 1 should behave. Set from the UX layer based on
    /// the user's Settings → Results viewer choice. Default is <see cref="CacheReuseMode.Always"/>
    /// so callers that never set it (tests, plugins) get the silently-reuse behaviour.
    /// </summary>
    public CacheReuseMode CacheReuseMode { get; set; } = CacheReuseMode.Always;

    /// <summary>
    /// Required when <see cref="CacheReuseMode"/> is <see cref="CacheReuseMode.Prompt"/>. The
    /// query calls this synchronously on the search thread to ask whether to reuse the cache.
    /// The UX layer's implementation marshals to the UI thread, shows a dialog, and blocks
    /// (via TaskCompletionSource) until the user picks. If null while in Prompt mode, we fall
    /// back to "use cache" — same effect as Always — so a missing callback never strands the
    /// user without results.
    /// </summary>
    public Func<CacheReusePromptInfo, bool> CacheReusePrompt { get; set; }

    // Internal: set in Step 1 when the on-disk cache matched, telling Step 2 to skip scanning.
    private bool _skipScan;
    private string _cachedSourcePath;

    /// <summary>
    /// True if the most recent <c>Step1_LoadAllLocationsInMemory</c> resolved to a cache hit
    /// (and the rest of the pipeline skipped scanning). Reset to false at the top of each
    /// Step 1. UX layer reads this to annotate the status bar with "from cache" vs
    /// "fresh scan".
    /// </summary>
    public bool LastSearchReusedCache => _skipScan;

    /// <summary>
    /// Pre-create the result storage on the calling thread (typically the UI thread). Used by
    /// the streaming entry point so the viewer can grab a reference to the live store before
    /// the background search task starts populating it. Idempotent — subsequent calls return
    /// the existing storage. Step 2 picks up whatever's already there via <c>??=</c>.
    /// </summary>
    public ISearchStorage PrepareStorage(CancellationToken cancellationToken = default)
    {
        _resultStorage ??= CreateStorage(cancellationToken);
        return _resultStorage;
    }

    /// <summary>
    /// Release the result storage so its SQLite connection (if any) drops the file lock on the
    /// cache <c>.db</c>. Called when this query is being replaced by a fresh one — otherwise the
    /// next search on the same log file path can stick waiting for the lock.
    /// Idempotent; safe to call multiple times.
    /// </summary>
    public void DisposeStorage()
    {
        var s = _resultStorage as IDisposable;
        _resultStorage = null;
        try { s?.Dispose(); } catch { /* never let cleanup throw */ }
    }

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
        Logger.Instance.Log("NuSearchQuery constructed");
        _resultStorage = null;
    }

    // Remove duplicate declaration of filePath in CreateStorage
    private ISearchStorage CreateStorage(CancellationToken cancellationToken)
    {
        var config = PluginManager.GetSingleton().config;
        var filePath = _locations.Count > 0 ? _locations[0].GetName() : "default";
        var requested = OverrideStorageType ?? config?.SearchStorageType;
        switch (requested)
        {
            case StorageType.SqlLite:
                PerfLog.Log("storage.selected", ("type", nameof(SqliteStorage)), ("mode", "forced"));
                return new SqliteStorage(filePath);
            case StorageType.InMemory:
                PerfLog.Log("storage.selected", ("type", nameof(InMemoryStorage)), ("mode", "forced"));
                return new InMemoryStorage();
            case StorageType.Hybrid:
                PerfLog.Log("storage.selected", ("type", nameof(HybridStorage)), ("mode", "forced"));
                return new HybridStorage(filePath);
            case StorageType.Auto:
            default:
                var totalRecords = 0;
                TimeSpan totalTime = TimeSpan.Zero;
                if (EstimatedRowCountOverride.HasValue)
                {
                    // Caller (or test) supplied the prediction directly — exercise the Auto decision
                    // with a known-good or deliberately-wrong estimate.
                    totalRecords = EstimatedRowCountOverride.Value;
                }
                else
                {
                    foreach (var loc in _locations)
                    {
                        try
                        {
                            var perf = loc.GetSearchPerformanceEstimate(cancellationToken);
                            if (perf.recordCount.HasValue)
                                totalRecords += perf.recordCount.Value;
                            if (perf.timeTaken.HasValue)
                                totalTime += perf.timeTaken.Value;
                        }
                        catch (NotImplementedException)
                        {
                            totalRecords += 100;
                        }
                    }
                }
                // Tiered Auto, recalibrated based on perf-log data:
                //   < 10k rows and quick   -> InMemory  (no disk, fastest for tiny logs)
                //   10k - 50k rows         -> Hybrid    (RAM-hot; settle at end is fast at this size)
                //   > 50k rows             -> SqlLite   (write rows straight to disk during the
                //                                        search; spreads the I/O across the scan
                //                                        instead of forcing a multi-second
                //                                        "moving N results into the cache" phase
                //                                        at the end).
                //
                // Old upper bound was 1M, which meant 500k-row Quick Opens were taking the slow
                // settle path. Direct-to-SQLite has comparable per-row throughput (multi-row
                // INSERT + prepared statement + journal_mode=MEMORY / synchronous=OFF), so the
                // total wall-clock cost is roughly the same, but the user's wait between
                // "search done" and "viewer open" disappears.
                string autoType = (totalRecords < 10_000 && totalTime.TotalSeconds < 30) ? nameof(InMemoryStorage)
                                : (totalRecords < 50_000) ? nameof(HybridStorage)
                                : nameof(SqliteStorage);
                PerfLog.Log("storage.selected", ("type", autoType), ("mode", "auto"), ("est", totalRecords));
                if (autoType == nameof(InMemoryStorage)) return new InMemoryStorage();
                if (autoType == nameof(HybridStorage)) return new HybridStorage(filePath);
                return new SqliteStorage(filePath);
        }
    }

    public void RunThrough()
    {
        RunThrough(CancellationToken.None);
    }

    public void RunThrough(CancellationToken cancellationToken)
    {
        // RunThrough is the "do everything synchronously" entry point. The UI takes a different
        // path (SearchQueryUX.GetSearchResults) which calls Step1..Step4 directly, so the per-
        // step PerfLog scopes + the end-of-Step2 settle live INSIDE the Step methods themselves.
        // That keeps both call sites equally instrumented and avoids double-firing scopes here.
        Logger.Instance.Log("RunThrough (with cancellation) started");
        LoadRules();
        ApplySystemConfig();

        Step1_LoadAllLocationsInMemory(cancellationToken);
        _currentResultList = Step2_GetFilteredResults(cancellationToken);
        Step3_ResultsToProcessors();
        Step4_ProcessAllResultsToOutput(cancellationToken);
        Step5_Done();

        Logger.Instance.Log("RunThrough (with cancellation) finished");
    }

    /// <summary>Friendlier storage label for status text: "in-memory" / "hybrid" / "SQLite".</summary>
    private static string ShortStorageLabel(ISearchStorage storage)
    {
        if (storage is InMemoryStorage) return "in-memory";
        if (storage is HybridStorage)   return "hybrid";
        if (storage is SqliteStorage)   return "SQLite";
        return storage?.GetType().Name ?? "?";
    }

    /// <summary>Last path segment so the status doesn't wrap with a multi-hundred-char filename.</summary>
    private static string ShortName(string path)
    {
        if (string.IsNullOrEmpty(path)) return "?";
        int i = path.LastIndexOfAny(new[] { '/', '\\' });
        return i >= 0 && i < path.Length - 1 ? path.Substring(i + 1) : path;
    }

    /// <summary>
    /// Read the current filtered row count from storage for status display. Never throws — the
    /// progress text is best-effort cosmetic, not load-bearing.
    /// </summary>
    private int SafeFilteredCount()
    {
        try { return _resultStorage?.GetStatistics().filteredRecordCount ?? 0; }
        catch { return 0; }
    }

    /// <summary>
    /// Format a remaining-seconds value for the status bar: "&lt;1s", "12s", "3m 14s",
    /// "2h 7m". Bucketed coarsely on purpose — finer precision would just churn while the
    /// estimate is still settling.
    /// </summary>
    private static string FormatEta(double seconds)
    {
        if (seconds < 1) return "<1s";
        if (seconds < 60) return $"{(int)seconds}s";
        if (seconds < 3600)
        {
            int m = (int)(seconds / 60);
            int s = (int)(seconds - m * 60);
            return $"{m}m {s}s";
        }
        int h = (int)(seconds / 3600);
        int mm = (int)((seconds - h * 3600) / 60);
        return $"{h}h {mm}m";
    }

    /// <summary>
    /// Load rules from configured paths.
    /// Also loads SystemConfig from RuleDSL.
    /// </summary>
    private void LoadRules()
    {
        if (RulesConfigPaths == null || RulesConfigPaths.Count == 0)
        {
            Logger.Instance.Log("No rules config paths specified");
            return;
        }

        try
        {
            LoadedRules = _ruleLoader.LoadRulesFromPaths(RulesConfigPaths);
            
            // Load SystemConfig from rule files
            _systemConfig = _ruleLoader.LoadMergedSystemConfig(RulesConfigPaths);
            
            Logger.Instance.Log($"Loaded rules from {RulesConfigPaths.Count} paths");
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"Error loading rules: {ex.Message}");
        }
    }

    #region main functions
    public void Step1_LoadAllLocationsInMemory()
    {
        Step1_LoadAllLocationsInMemory(CancellationToken.None);
    }

    public void Step1_LoadAllLocationsInMemory(CancellationToken cancellationToken)
    {
        Logger.Instance.Log($"Step1_LoadAllLocationsInMemory (with cancellation): {_locations.Count} locations");
        using var _perf = PerfLog.Scope("search.step1", ("locations", _locations.Count));
        _skipScan = false;
        _cachedSourcePath = null;

        // ----- Cache reuse fast path -----
        // For single-file searches, see if the previous run's cache DB is still valid for this
        // file (size + mtime + schema version match). If it is, we skip the entire file read +
        // parse + index pipeline — the storage already has every row and the FTS5 index is
        // already built. Saves ~5-9s on a 500k-row reopen.
        //
        // CacheReuseMode controls whether we look at the cache at all and whether we ask the
        // user before reusing it:
        //   Always — silently reuse if valid (fastest, no prompt)
        //   Never  — don't even check; always rescan
        //   Prompt — validate cache; if valid, call CacheReusePrompt; user decides
        if (CacheReuseMode != CacheReuseMode.Never
            && _locations.Count == 1
            && OverrideStorageType != StorageType.InMemory
            && OverrideStorageType != StorageType.Hybrid)
        {
            FlowProgress.Begin(FlowPhase.CheckCache);
            var path = _locations[0].GetName();
            if (System.IO.File.Exists(path))
            {
                try
                {
                    _resultStorage ??= CreateStorage(cancellationToken);
                    if (_resultStorage is SqliteStorage sql
                        && sql.EvaluateCacheReuse(path, SqliteStorage.CacheSchemaVersion))
                    {
                        bool reuse = true;
                        if (CacheReuseMode == CacheReuseMode.Prompt && CacheReusePrompt != null)
                        {
                            // Gather info for the dialog. Pull from the metadata we just
                            // validated; if any field is missing, the prompt simply doesn't
                            // surface that detail — it never blocks.
                            CacheReusePromptInfo info;
                            try
                            {
                                var fi = new System.IO.FileInfo(path);
                                info = new CacheReusePromptInfo
                                {
                                    SourceFilePath = path,
                                    SourceFileSize = fi.Length,
                                    SourceFileMtimeUtc = fi.LastWriteTimeUtc,
                                    CachedRowCount = sql.GetStatistics().filteredRecordCount,
                                    CacheCompletedAtUtc = sql.TryGetCacheCompletedAt(),
                                };
                            }
                            catch
                            {
                                info = new CacheReusePromptInfo { SourceFilePath = path };
                            }

                            try
                            {
                                reuse = CacheReusePrompt(info);
                                PerfLog.Log("cache.prompt", ("answer", reuse ? "use" : "rescan"));
                            }
                            catch (Exception ex)
                            {
                                Logger.Instance.Log($"Cache prompt callback threw: {ex.Message} — defaulting to reuse.");
                                reuse = true;
                            }
                        }

                        if (reuse)
                        {
                            _skipScan = true;
                            _cachedSourcePath = path;
                            int n = sql.GetStatistics().filteredRecordCount;
                            _stepnotifysink.progressSink.NotifyProgress(
                                100, $"reusing cached results · {n:N0} rows");
                            FlowProgress.Detail($"reused {n:N0} rows");
                            PerfLog.Log("cache.hit", ("path", System.IO.Path.GetFileName(path)),
                                ("rows", n), ("mode", CacheReuseMode.ToString()));
                            Logger.Instance.Log($"Cache hit for {path}: {n} rows. Skipping scan.");
                            _stepnotifysink.NotifyStep(SearchStep.AtLoad);
                            return;
                        }
                        else
                        {
                            // User declined reuse — wipe and fall through to fresh scan.
                            sql.ClearTables();
                            _stepnotifysink.progressSink.NotifyProgress(
                                0, "cache declined · rescanning…");
                            PerfLog.Log("cache.declined", ("path", System.IO.Path.GetFileName(path)));
                            Logger.Instance.Log($"Cache declined for {path}; running fresh scan.");
                            FlowProgress.Detail("declined · rescanning");
                        }
                    }
                    else
                    {
                        PerfLog.Log("cache.miss", ("path", System.IO.Path.GetFileName(path)));
                        FlowProgress.Detail("no usable cache · scanning");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Instance.Log($"Cache check failed for {path}: {ex.Message} — falling back to fresh scan.");
                }
            }
        }

        var count = 1;
        var total = _locations.Count;
        _ = PluginManager.GetSingleton();
        FlowProgress.Begin(FlowPhase.OpenLocations);
        foreach (var loc in _locations)
        {
            if (cancellationToken.IsCancellationRequested) return;
            Logger.Instance.Log($"Loading location {count}/{total}: {loc.GetName()}");
            FlowProgress.Detail(System.IO.Path.GetFileName(loc.GetName()?.TrimEnd('\\', '/')) ?? "");
            if (loc is FindNeedlePluginLib.Interfaces.IReportProgress reportable)
            {
                reportable.SetProgressSink(_stepnotifysink.progressSink);
            }
            var percent = total > 0 ? (int)(50.0 * count / total) : 0;
            _stepnotifysink.progressSink.NotifyProgress(percent, "loading location: " + loc.GetName());
            loc.LoadInMemory(cancellationToken);
            Logger.Instance.Log($"Loaded location: {loc.GetName()}");
            try
            {
                var perf = loc.GetSearchPerformanceEstimate(cancellationToken);
                Logger.Instance.Log($"Performance estimate for {loc.GetName()}: time={perf.timeTaken}, records={perf.recordCount}");
            }
            catch (NotImplementedException)
            {
                Logger.Instance.Log($"Performance estimate not implemented for {loc.GetName()}");
            }
            count++;
        }
        _stepnotifysink.NotifyStep(SearchStep.AtLoad);
        Logger.Instance.Log("Step1_LoadAllLocationsInMemory (with cancellation) complete");
    }

    private List<ISearchResult>? _filteredResults;
    public List<ISearchResult> Step2_GetFilteredResults()
    {
        return Step2_GetFilteredResults(CancellationToken.None);
    }

    public List<ISearchResult> Step2_GetFilteredResults(CancellationToken cancellationToken)
    {
        Logger.Instance.Log("Step2_GetFilteredResults (with cancellation) started");
        using var _perf = PerfLog.Scope("search.step2");
        _stepnotifysink.NotifyStep(SearchStep.AtSearch);
        _filteredResults = new();

        // Cache hit from Step 1: nothing to scan. Storage already holds the rows + FTS5 index.
        // Downstream consumers (viewer) read from storage directly via IPagedLogSource — no
        // in-memory list needs to be populated.
        if (_skipScan)
        {
            Logger.Instance.Log("Step2: skipping (cache reuse).");
            return _filteredResults;
        }

        // If a caller (e.g. the streaming entry point) already prepared storage on the UI thread
        // we reuse that instance — otherwise create it lazily here, preserving the legacy flow.
        _resultStorage ??= CreateStorage(cancellationToken);

        // We're doing a fresh scan (a cache hit would have returned above via _skipScan). The
        // storage constructor no longer wipes on open — it preserves any on-disk cache so the
        // cache-reuse fast path can validate it — so guarantee a clean slate here before we
        // start writing. Without this, a stale cache file (CacheReuseMode.Never, a multi-location
        // search, or a cache miss) could surface duplicated rows. Harmless no-op when already
        // empty (fresh InMemory, or a SQLite miss that EvaluateCacheReuse already cleared).
        _resultStorage.ClearTables();

        var storageLabel = ShortStorageLabel(_resultStorage);
        var count = 1;
        var total = _locations.Count;
        var pluginManager = PluginManager.GetSingleton();
        var useSync = pluginManager.config?.UseSynchronousSearch ?? false;

        // ----- Streaming-to-disk fast path (constant memory) -----
        // For the "just view a huge log" case — no filters, no rules/processors/outputs (so no
        // in-RAM list is needed), SQLite storage, async scan — we can write each batch to the
        // filtered store as it arrives and never retain it. Without this we'd accumulate the entire
        // result set in `rawResults` just to hand it to one post-scan AddFilteredBatch, holding every
        // row alive (a 5M-row .etl peaked ~2.9 GB purely on that). synchronous=OFF + journal=MEMORY
        // make per-batch transactions cheap, so streaming costs the same total insert work but keeps
        // peak RAM to ~one batch. Any filters/rules/processors/outputs, sync scan, or non-SQLite
        // storage fall back to the accumulate-then-store path below.
        bool noFilters = _filters == null || _filters.Count == 0;
        bool willMaterializeList =
            LoadedRules != null
            || (_processors != null && _processors.Count > 0)
            || (_outputs != null && _outputs.Count > 0);
        bool streamFilteredDuringScan =
            !useSync && noFilters && !willMaterializeList && _resultStorage is SqliteStorage;
        if (streamFilteredDuringScan)
            Logger.Instance.Log("Step2: streaming filtered rows straight to SQLite (constant memory, no rawResults retention)");

        FlowProgress.Begin(FlowPhase.ReadParse);
        foreach (var loc in _locations)
        {
            if (cancellationToken.IsCancellationRequested) break;
            var basename = ShortName(loc.GetName());
            Logger.Instance.Log($"Filtering results for location {count}/{total}: {loc.GetName()}");

            // Ask the plugin how many rows it expects. Used for "12,345 / ~500,000" status and
            // for refining the progress bar within this location. Some plugins don't implement
            // estimation — those just don't get the denominator.
            int? estRows = null;
            try
            {
                var perf = loc.GetSearchPerformanceEstimate(cancellationToken);
                if (perf.recordCount.HasValue && perf.recordCount.Value > 0)
                    estRows = perf.recordCount.Value;
            }
            catch (NotImplementedException) { /* unknown — that's fine */ }
            catch (Exception ex) { Logger.Instance.Log($"perf estimate failed for {basename}: {ex.Message}"); }

            // Percent at start-of-location. Was `count / total` which is wrong: count is the
            // 1-based index of the *current* location, so for the first iteration we'd show 100%
            // before any work was done. Use (count-1) so the base of this location is the bar
            // position when its scan starts; in-callback intra-location progress walks it up.
            int locBasePercent = total > 0 ? 50 + (int)(50.0 * (count - 1) / total) : 50;
            int locSpan        = total > 0 ? (int)(50.0 / total) : 0;

            string EstSuffix(int n) =>
                estRows.HasValue
                    ? $"{n:N0} / ~{estRows.Value:N0} rows"
                    : $"{n:N0} rows";

            _stepnotifysink.progressSink.NotifyProgress(
                locBasePercent,
                $"[{count}/{total}] scanning {basename} · {EstSuffix(0)} · {storageLabel}");
            PerfLog.Log("location.start", ("idx", count), ("of", total), ("name", basename),
                ("rows_before", SafeFilteredCount()), ("est_rows", estRows ?? -1));
            var locStart = Environment.TickCount64;
            loc.SetSearchDepth(_depth);
            List<ISearchResult> rawResults = new();
            // Running row count for the streaming path (rawResults stays empty there, so we can't
            // use rawResults.Count for the progress denominator).
            int streamedCount = 0;
            if (!useSync)
            {
                // Throttled per-batch status update so the user sees rows climbing during the
                // scan instead of "0 rows" frozen for several seconds. Fire on either a row-
                // count delta (every 5k rows) or a time delta (every 250 ms) so a fast plugin
                // doesn't flood the sink and a slow one still ticks visibly.
                int lastReportedRows = 0;
                long lastReportMs = Environment.TickCount64;
                int locIdxCapture = count, totalCapture = total;
                string nameCapture = basename, storageCapture = storageLabel;
                int basePctCapture = locBasePercent;
                int spanCapture = locSpan;
                int? estCapture = estRows;
                long locStartCapture = locStart;

                try
                {
                    loc.SearchWithCallback(batch => {
                        if (cancellationToken.IsCancellationRequested) return;
                        // Constant-memory path: write filtered straight to disk and don't retain.
                        // Otherwise accumulate for the post-scan filter/store/consolidate steps.
                        if (streamFilteredDuringScan)
                        {
                            _resultStorage.AddRawBatch(batch, cancellationToken);
                            _resultStorage.AddFilteredBatch(batch, cancellationToken);
                            streamedCount += batch.Count;
                        }
                        else
                        {
                            rawResults.AddRange(batch);
                            _resultStorage.AddRawBatch(batch, cancellationToken);
                        }

                        int n = streamFilteredDuringScan ? streamedCount : rawResults.Count;
                        long now = Environment.TickCount64;
                        if (n - lastReportedRows >= 5000 || now - lastReportMs >= 250)
                        {
                            lastReportedRows = n;
                            lastReportMs = now;

                            // Refine the bar position within this location when we have an
                            // estimate. Capped at the next location's base so the bar can't
                            // overshoot if the plugin under-estimated.
                            int pct = basePctCapture;
                            if (estCapture.HasValue && estCapture.Value > 0 && spanCapture > 0)
                            {
                                double frac = Math.Min(1.0, (double)n / estCapture.Value);
                                pct = basePctCapture + (int)(spanCapture * frac);
                            }

                            // ETA: only if we have both an estimate and enough samples for the
                            // rate to mean something (>=1s elapsed AND >=1000 rows). Early
                            // samples are dominated by startup costs and produce noisy numbers
                            // like "ETA 47m" that retreat immediately to "ETA 8s" — worse than
                            // showing nothing.
                            long elapsedMs = now - locStartCapture;
                            string etaText = "";
                            if (estCapture.HasValue && estCapture.Value > n
                                && elapsedMs >= 1000 && n >= 1000)
                            {
                                double rate = n * 1000.0 / elapsedMs; // rows/sec
                                if (rate > 0)
                                {
                                    double remainingSec = (estCapture.Value - n) / rate;
                                    etaText = " · ETA " + FormatEta(remainingSec);
                                }
                            }

                            var rowsText = estCapture.HasValue
                                ? $"{n:N0} / ~{estCapture.Value:N0} rows"
                                : $"{n:N0} rows";
                            _stepnotifysink.progressSink.NotifyProgress(
                                pct,
                                $"[{locIdxCapture}/{totalCapture}] scanning {nameCapture} · {rowsText}{etaText} · {storageCapture}");
                            // Structured flow detail: metric text + storage suffix, with percent in
                            // its own field (no [i/j]/name noise, no ETA in the metric).
                            FlowProgress.Detail($"{rowsText} · {storageCapture}",
                                estCapture.HasValue && estCapture.Value > 0
                                    ? Math.Clamp((int)(n * 100L / estCapture.Value), 0, 100) : (int?)null);
                        }
                    }, cancellationToken).Wait();
                }
                catch (NotImplementedException)
                {
                    Logger.Instance.Log($"SearchWithCallback not implemented for {loc.GetName()}");
                }
            }
            else
            {
                // Sync (non-streaming) path: the plugin doesn't support callbacks, so we get the
                // whole result list at once.
                try
                {
                    rawResults = loc.Search(cancellationToken);
                    Logger.Instance.Log($"Search for {loc.GetName()} returned {rawResults.Count} raw results");
                    _resultStorage.AddRawBatch(rawResults, cancellationToken);
                }
                catch (NotImplementedException)
                {
                    Logger.Instance.Log($"Search not implemented for {loc.GetName()}");
                }
            }

            // Streaming path already wrote filtered rows to disk during the scan (rawResults is
            // empty) — nothing left to store for this location.
            if (streamFilteredDuringScan)
            {
                Logger.Instance.Log($"Results streamed to storage for location: {loc.GetName()} ({streamedCount:N0} rows)");
                int locEndPctStream = locBasePercent + locSpan;
                _stepnotifysink.progressSink.NotifyProgress(
                    locEndPctStream,
                    $"[{count}/{total}] done {basename} · {SafeFilteredCount():N0} rows · {storageLabel}");
                PerfLog.Log("location.end", ("idx", count), ("name", basename),
                    ("rows_total", SafeFilteredCount()),
                    ("rows_this_loc_raw", streamedCount),
                    ("elapsed_ms", Environment.TickCount64 - locStart));
                count++;
                continue;
            }

            // ----- One bulk AddFilteredBatch per location -----
            // Doing this per-batch inside the SearchWithCallback closure feels nicer for status
            // updates but pays a fresh transaction + multi-row INSERT prepare cost (~30 KB SQL
            // parse) on EVERY batch. On a 500k-row search that's 500 transactions instead of 1,
            // ~3× slower in practice. Instead we do one big bulk insert here and use the storage's
            // progress callback to keep the status text climbing during it.
            List<ISearchResult> filteredBatch;
            if (_filters == null || _filters.Count == 0)
            {
                // No filters configured → the raw set IS the filtered set. Pass the list by
                // reference rather than copying 500k elements.
                filteredBatch = rawResults;
            }
            else
            {
                filteredBatch = new List<ISearchResult>(rawResults.Count);
                for (int i = 0; i < rawResults.Count; i++)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    var r = rawResults[i];
                    bool pass = true;
                    for (int f = 0; f < _filters.Count; f++)
                    {
                        if (!_filters[f].Filter(r)) { pass = false; break; }
                    }
                    if (pass) filteredBatch.Add(r);
                }
            }

            if (filteredBatch.Count > 0) FlowProgress.Begin(FlowPhase.StoreResults);
            if (filteredBatch.Count > 0 && _resultStorage is SqliteStorage sqlitePass)
            {
                // SqliteStorage exposes a progress callback overload — fires every 500 rows so
                // the status text can climb during the (single) bulk insert + FTS5 index build.
                int filteredTotal = filteredBatch.Count;
                int locIdx = count, totCap = total;
                string nameCap = basename, storageCap = storageLabel;
                int basePct = locBasePercent, span = locSpan;
                sqlitePass.AddFilteredBatch(filteredBatch, cancellationToken, inserted =>
                {
                    int pct = basePct;
                    if (filteredTotal > 0 && span > 0)
                    {
                        double frac = Math.Min(1.0, (double)inserted / filteredTotal);
                        pct = basePct + (int)(span * frac);
                    }
                    _stepnotifysink.progressSink.NotifyProgress(
                        pct,
                        $"[{locIdx}/{totCap}] indexing {nameCap} · {inserted:N0} / {filteredTotal:N0} rows · {storageCap}");
                    FlowProgress.Detail($"{inserted:N0} / {filteredTotal:N0} rows · {storageCap}",
                        filteredTotal > 0 ? Math.Clamp((int)(inserted * 100L / filteredTotal), 0, 100) : (int?)null);
                });
            }
            else if (filteredBatch.Count > 0)
            {
                _resultStorage.AddFilteredBatch(filteredBatch, cancellationToken);
            }

            Logger.Instance.Log($"Results stored for location: {loc.GetName()}");

            // End-of-location update: bar now sits at the top of this location's span.
            int locEndPercent = locBasePercent + locSpan;
            _stepnotifysink.progressSink.NotifyProgress(
                locEndPercent,
                $"[{count}/{total}] done {basename} · {SafeFilteredCount():N0} rows · {storageLabel}");
            PerfLog.Log("location.end", ("idx", count), ("name", basename),
                ("rows_total", SafeFilteredCount()),
                ("rows_this_loc_raw", rawResults?.Count ?? 0),
                ("elapsed_ms", Environment.TickCount64 - locStart));
            count++;
        }
        // ----- Decide whether to materialize the full result list in RAM -----
        // The list is only meaningful if something downstream walks it. Step3 walks it iff
        // processors are configured or rule-enrichment is loaded; Step4 walks it iff outputs
        // are configured or rule-output is loaded; the post-search legacy in-memory client-side
        // web viewer reads it via MiddleLayerService.GetLogLines (now lazy from storage). For
        // Quick Open on a huge log — no rules, no processors, no outputs — this is 36 seconds
        // of pure allocation. Skip it; downstream consumers fall back to the storage-backed path.
        int known = SafeFilteredCount();
        bool needsList =
            LoadedRules != null
            || (_processors != null && _processors.Count > 0)
            || (_outputs != null && _outputs.Count > 0);

        List<ISearchResult> allResults;

        if (!needsList)
        {
            Logger.Instance.Log($"Step2: skipping consolidate ({known:N0} rows stay in storage, no downstream consumer)");
            PerfLog.Log("consolidate.skipped", ("known_rows", known), ("reason", "no_consumers"));
            // Return an empty list — _currentResultList becomes empty, Step3/Step4 iterate over
            // nothing (they're no-ops anyway), and consumers read from storage instead.
            allResults = new List<ISearchResult>();
        }
        else
        {
            // Pre-size the list to the known row count so internal array doubling doesn't fire
            // ~20 times on a 500k consolidation.
            allResults = new List<ISearchResult>(Math.Max(known, 1024));
            int gathered = 0;
            int lastReport = 0;
            _stepnotifysink.progressSink.NotifyProgress(
                100, $"consolidating {known:N0} rows from {storageLabel}…");

            FlowProgress.Begin(FlowPhase.Consolidate);
            using (PerfLog.Scope("consolidate", ("known_rows", known), ("storage", storageLabel)))
            {
                _resultStorage.GetFilteredResultsInBatches(batch =>
                {
                    allResults.AddRange(batch);
                    gathered += batch.Count;
                    // Throttle status to every 10k rows; spamming the dispatcher with 500
                    // updates does more harm than good.
                    if (gathered - lastReport >= 10_000)
                    {
                        lastReport = gathered;
                        _stepnotifysink.progressSink.NotifyProgress(
                            100, $"consolidating · {gathered:N0} / {known:N0} rows · {storageLabel}");
                        FlowProgress.Detail($"{gathered:N0} / {known:N0} rows · {storageLabel}",
                            known > 0 ? Math.Clamp((int)(gathered * 100L / known), 0, 100) : (int?)null);
                    }
                }, 1000, cancellationToken);
            }

            // Apply rule-based filtering if rules are loaded
            if (LoadedRules != null)
            {
                _stepnotifysink.progressSink.NotifyProgress(100, $"applying rule filters to {allResults.Count:N0} rows…");
                Logger.Instance.Log("Applying rule-based filtering...");
                using (PerfLog.Scope("rule_filter", ("in_rows", allResults.Count)))
                    allResults = ApplyRuleFiltering(allResults);
                Logger.Instance.Log($"After rule filtering: {allResults.Count} results");
            }
        }

        _filteredResults = allResults;

        // ----- Settle HybridStorage to disk on the search thread, not the UI thread -----
        // The viewer (any viewer) opens immediately after Step2; if Hybrid still has rows in RAM
        // when the viewer asks PagedLogSourceFactory for a source, the resulting SettleToDisk
        // call blocks the UI for tens of seconds. Doing it here means the search task pays the
        // cost (where progress is visible) instead of the UI thread paying it.
        if (_resultStorage is HybridStorage hybrid)
        {
            var rowsToMove = SafeFilteredCount();
            _stepnotifysink.progressSink.NotifyProgress(
                100, $"moving {rowsToMove:N0} results into the cache…");
            try
            {
                using (PerfLog.Scope("search.settle", ("storage", "hybrid"), ("rows", rowsToMove)))
                    hybrid.SettleToDisk(cancellationToken);
                Logger.Instance.Log("HybridStorage settled to disk at end of Step2");
            }
            catch (Exception ex)
            {
                PerfLog.Log("search.settle.error", ("msg", ex.GetType().Name));
                Logger.Instance.Log($"HybridStorage settle failed (viewer will retry on open): {ex.Message}");
            }
        }

        // ----- Build the full-text search index in one bulk pass -----
        // All filtered rows are now in storage (and, for Hybrid, settled to disk above). Build the
        // FTS5 trigram index once here instead of maintaining it per-row during ingest. Skipped when
        // DeferIndexBuild is set (UI lazy/background modes build it later via BuildSearchIndexNow so
        // the viewer can open before the index finishes); until it's built, substring search falls
        // back to LIKE (handled in storage).
        if (!cancellationToken.IsCancellationRequested && !DeferIndexBuild)
        {
            try
            {
                FlowProgress.Begin(FlowPhase.BuildIndex);
                using (PerfLog.Scope("search.build_index"))
                    _resultStorage.BuildSearchIndex(cancellationToken,
                        (indexed, totalRows) => FlowProgress.Detail(
                            $"{indexed:N0} / {totalRows:N0} rows",
                            totalRows > 0 ? (int)(indexed * 100 / totalRows) : (int?)null));
            }
            catch (Exception ex)
            {
                PerfLog.Log("search.build_index.error", ("msg", ex.GetType().Name));
                Logger.Instance.Log($"BuildSearchIndex failed (search falls back to LIKE): {ex.Message}");
            }
        }
        else if (DeferIndexBuild)
        {
            PerfLog.Log("search.build_index.deferred");
        }

        TryStampCacheCompletion(cancellationToken);

        Logger.Instance.Log($"Step2_GetFilteredResults (with cancellation) complete: {_filteredResults.Count} total filtered results");
        return _filteredResults;
    }

    /// <summary>
    /// When set, Step2 does NOT build the FTS search index inline. The UI's lazy/background indexing
    /// modes set this so the viewer can open before the (potentially multi-minute) index build, then
    /// drive the build later via <see cref="BuildSearchIndexNow"/>. The CLI / RunThrough path leaves
    /// it false so search works immediately without a viewer to orchestrate the build.
    /// </summary>
    public bool DeferIndexBuild { get; set; }

    /// <summary>
    /// Build the search index on demand (lazy first-search, or background after the viewer opens),
    /// reporting progress, then re-stamp the cache so a warm reopen reuses the now-built index.
    /// Cancellable between batches. Safe to call when the index is already built (cheap rebuild).
    /// </summary>
    public void BuildSearchIndexNow(Action<long, long> onProgress = null, CancellationToken cancellationToken = default)
    {
        if (_resultStorage == null) return;
        try
        {
            using (PerfLog.Scope("search.build_index.ondemand"))
                _resultStorage.BuildSearchIndex(cancellationToken, onProgress);
        }
        catch (Exception ex)
        {
            PerfLog.Log("search.build_index.error", ("msg", ex.GetType().Name));
            Logger.Instance.Log($"BuildSearchIndexNow failed (search falls back to LIKE): {ex.Message}");
            return;
        }
        // Persist the now-built index state so the next reopen's cache reuse sees fts_built=1.
        if (!cancellationToken.IsCancellationRequested)
            TryStampCacheCompletion(cancellationToken);
    }

    /// <summary>
    /// Stamp the on-disk cache as complete + valid for the next run. Only meaningful for a
    /// single-file SqliteStorage workspace; no-op otherwise. The fts_built flag it records reflects
    /// whether the index has been built at the time of the call.
    /// </summary>
    private void TryStampCacheCompletion(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            PerfLog.Log("cache.write.skip", ("reason", "cancelled"));
        }
        else if (CacheReuseMode == CacheReuseMode.Never)
        {
            PerfLog.Log("cache.write.skip", ("reason", "disabled"));
        }
        else if (_locations.Count != 1)
        {
            PerfLog.Log("cache.write.skip", ("reason", "multi_location"), ("count", _locations.Count));
        }
        else if (_resultStorage is not SqliteStorage sqliteForMeta)
        {
            PerfLog.Log("cache.write.skip", ("reason", "not_sqlite"),
                ("type", _resultStorage?.GetType().Name ?? "(null)"));
        }
        else
        {
            var path = _locations[0].GetName();
            if (!System.IO.File.Exists(path))
            {
                PerfLog.Log("cache.write.skip", ("reason", "source_missing"));
            }
            else
            {
                try
                {
                    sqliteForMeta.WriteCompletionMetadata(path, SqliteStorage.CacheSchemaVersion);
                    PerfLog.Log("cache.write.ok", ("rows", SafeFilteredCount()));
                }
                catch (Exception ex)
                {
                    PerfLog.Log("cache.write.skip", ("reason", "exception"), ("msg", ex.GetType().Name));
                    Logger.Instance.Log($"WriteCompletionMetadata failed: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Apply rule-based filtering from loaded rules with purpose="filter".
    /// </summary>
    private List<ISearchResult> ApplyRuleFiltering(List<ISearchResult> results)
    {
        try
        {
            var filterSections = _ruleLoader.GetSectionsByPurpose(LoadedRules, "filter");
            if (filterSections == null || !filterSections.Any())
            {
                Logger.Instance.Log("No filter sections found in rules");
                return results;
            }

            Logger.Instance.Log($"Found {filterSections.Count()} filter section(s)");
            var filtered = new List<ISearchResult>();
            
            foreach (var result in results)
            {
                var include = false; // Default to exclude unless a rule matches with include action
                var explicitlyExcluded = false;

                foreach (var section in filterSections)
                {
                    var evalResult = _ruleEngine.EvaluateRules(result, section);
                    
                    // If any rule matched with include action, include the result
                    if (evalResult.Include)
                    {
                        include = true;
                    }
                    
                    // Check if explicitly excluded
                    if (!evalResult.Include && evalResult.Tags.Count == 0)
                    {
                        // Rule matched but set Include to false = explicit exclude
                        explicitlyExcluded = true;
                    }
                }

                if (include && !explicitlyExcluded)
                {
                    filtered.Add(result);
                }
            }

            Logger.Instance.Log($"Rule filtering: {results.Count} -> {filtered.Count} results");
            return filtered;
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"Error applying rule filtering: {ex.Message}");
            return results;
        }
    }

    public void Step3_ResultsToProcessors()
    {
        Logger.Instance.Log("Step3_ResultsToProcessors started");
        using var _perf = PerfLog.Scope("search.step3",
            ("processors", _processors?.Count ?? 0), ("rules", LoadedRules != null));
        _stepnotifysink.NotifyStep(SearchStep.AtProcessor);

        // Apply enrichment rules if loaded
        if (LoadedRules != null)
        {
            _stepnotifysink.progressSink.NotifyProgress(100, "applying rule enrichments…");
            ApplyRuleEnrichment();
        }

        // Run any configured processors
        int procIdx = 1;
        int procTotal = _processors.Count;
        foreach (var proc in _processors)
        {
            var name = proc.GetType().Name;
            _stepnotifysink.progressSink.NotifyProgress(100, $"running processor [{procIdx}/{procTotal}] {name}…");
            Logger.Instance.Log($"Processing results with processor: {name}");
            proc.ProcessResults(_currentResultList);
            Logger.Instance.Log($"Processor {name} complete");
            procIdx++;
        }

        Logger.Instance.Log("Step3_ResultsToProcessors complete");
    }

    /// <summary>
    /// Apply rule-based enrichment from loaded rules with purpose="enrichment".
    /// </summary>
    private void ApplyRuleEnrichment()
    {
        try
        {
            var enrichmentSections = _ruleLoader.GetSectionsByPurpose(LoadedRules, "enrichment");
            if (enrichmentSections == null || !enrichmentSections.Any())
            {
                Logger.Instance.Log("No enrichment sections found in rules");
                return;
            }

            Logger.Instance.Log($"Found {enrichmentSections.Count()} enrichment section(s)");
            var totalTags = 0;

            foreach (var result in _currentResultList)
            {
                foreach (var section in enrichmentSections)
                {
                    var evalResult = _ruleEngine.EvaluateRules(result, section);
                    
                    // Apply tags to the result
                    foreach (var tag in evalResult.Tags)
                    {
                        // Store tags in extended properties if the result supports it
                        // For now, just log the tags
                        totalTags++;
                    }
                }
            }

            Logger.Instance.Log($"Rule enrichment: applied {totalTags} tags to {_currentResultList.Count} results");
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"Error applying rule enrichment: {ex.Message}");
        }
    }

    public void Step4_ProcessAllResultsToOutput(CancellationToken cancellationToken = default)
    {
        Logger.Instance.Log("Step4_ProcessAllResultsToOutput started");
        using var _perf = PerfLog.Scope("search.step4",
            ("outputs", _outputs?.Count ?? 0), ("rules", LoadedRules != null));
        _stepnotifysink.NotifyStep(SearchStep.AtOutput);

        // Apply output rules if loaded
        if (LoadedRules != null)
        {
            _stepnotifysink.progressSink.NotifyProgress(100, "running rule outputs…");
            ApplyRuleOutput(cancellationToken);
        }

        // Run standard outputs
        int outIdx = 1;
        int outTotal = _outputs.Count;
        foreach (var output in _outputs)
        {
            if (cancellationToken.IsCancellationRequested) break;
            var name = output.GetType().Name;
            _stepnotifysink.progressSink.NotifyProgress(100, $"writing output [{outIdx}/{outTotal}] {name}…");
            Logger.Instance.Log($"Writing all output with: {name}");
            output.WriteAllOutput(_currentResultList);
            outIdx++;
        }
        Logger.Instance.Log("Step4_ProcessAllResultsToOutput complete");
    }

    /// <summary>
    /// Apply rule-based output from loaded rules with purpose="output".
    /// </summary>
    private void ApplyRuleOutput(CancellationToken cancellationToken = default)
    {
        try
        {
            var outputSections = _ruleLoader.GetSectionsByPurpose(LoadedRules, "output");
            if (outputSections == null || !outputSections.Any())
            {
                Logger.Instance.Log("No output sections found in rules");
                return;
            }

            Logger.Instance.Log($"Found {outputSections.Count()} output section(s)");
            _outputProcessor.ProcessOutputRules(_currentResultList, outputSections, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"Error applying rule output: {ex.Message}");
        }
    }

    public void Step5_Done()
    {
        Logger.Instance.Log("Step5_Done called");
        _stepnotifysink.NotifyStep(SearchStep.Total);
    }
    
    /// <summary>
    /// Applies SystemConfig settings from RuleDSL to the search query.
    /// </summary>
    private void ApplySystemConfig()
    {
        if (_systemConfig == null)
            return;

        Logger.Instance.Log("Applying SystemConfig settings");

        // Apply search configuration
        if (_systemConfig.Search != null)
        {
            // Apply search depth if specified
            if (!string.IsNullOrEmpty(_systemConfig.Search.DefaultDepth))
            {
                try
                {
                    var depth = (SearchLocationDepth)Enum.Parse(typeof(SearchLocationDepth), _systemConfig.Search.DefaultDepth, true);
                    SetDepthForAllLocations(depth);
                    Logger.Instance.Log($"Applied search depth: {depth}");
                }
                catch
                {
                    Logger.Instance.Log($"Invalid depth setting: {_systemConfig.Search.DefaultDepth}");
                }
            }

            // Apply search name if specified
            if (!string.IsNullOrEmpty(_systemConfig.Search.Name))
            {
                Name = _systemConfig.Search.Name;
                Logger.Instance.Log($"Applied search name: {_systemConfig.Search.Name}");
            }
        }

        // Apply plugin configuration if needed
        if (_systemConfig.Plugins != null)
        {
            // Plugin loading is handled by PluginManager
            // SystemConfig can override which ISearchQuery class to use
            if (!string.IsNullOrEmpty(_systemConfig.Plugins.SearchQueryClass))
            {
                Logger.Instance.Log($"SearchQuery class: {_systemConfig.Plugins.SearchQueryClass}");
            }
        }
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
