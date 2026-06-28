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

    /// <summary>Files written by RuleDSL output rules during this search (UML diagrams, images,
    /// CSV/JSON/etc. exports). These go straight to the output folder, so the UI reads this to
    /// surface them — they have no other handle.</summary>
    public IReadOnlyList<string> GeneratedRuleOutputFiles => _outputProcessor.GeneratedFiles;

    /// <summary>Per-diagram UML rule-usage from this search (which rules fired and how often), so the
    /// UI can show which rules contributed to each generated diagram.</summary>
    public IReadOnlyList<FindNeedleRuleDSL.UmlDiagramUsage> GeneratedDiagramUsages => _outputProcessor.GeneratedDiagrams;

    /// <summary>Source rows used by UML diagrams in this search (row id → matching rule), so the
    /// results viewer can tag the rows that fed a diagram.</summary>
    public IReadOnlyList<FindNeedleRuleDSL.UmlRowTag> UmlMatchedRows => _outputProcessor.UmlMatchedRows;

    /// <summary>Per-rule cost of the in-scan field-extraction enrichment from the last fresh scan: the
    /// rule name, how many rows its gate matched, and the wall time it took. Lets the UI show which rule
    /// is slow (and replaces the bogus "0 matched" the Active rules page showed for in-scan enrichment).
    /// Empty after a warm cache reuse (no scan ran).</summary>
    public sealed record EnrichmentRuleStat(string Name, int Matches, double Ms);
    public IReadOnlyList<EnrichmentRuleStat> EnrichmentRuleStats
    {
        get
        {
            var list = new List<EnrichmentRuleStat>(_ruleEngine.RuleStats.Count);
            foreach (var kv in _ruleEngine.RuleStats)
                list.Add(new EnrichmentRuleStat(kv.Key, kv.Value.Matches,
                    kv.Value.Ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency));
            return list;
        }
    }

    public SearchStepNotificationSink SearchStepNotificationSink
    {
        get => _stepnotifysink;
        set
        {
            _stepnotifysink = value;
            // The ctor registered _stats on the original sink. UpdateAllParameters swaps in the
            // prior query's sink (to keep the UI's progress subscriptions), which would otherwise
            // orphan the stats — Step1/Step2 fire AtLoad/AtSearch on the new sink, so re-register
            // here or the memory snapshots never get taken ("not snapped yet" on the Statistics page).
            _stats?.RegisterForNotifications(_stepnotifysink, this);
        }
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
    /// When true, RuleDSL "extract" enrichment runs per-row during the Step 2 scan, writing the
    /// extracted fields (ProcessId/ThreadId/Source/…) onto the row before it's stored — so they become
    /// real, queryable columns. Off by default: the per-row regex is the cost, so it's opt-in. Set from
    /// the UX (Settings → Results viewer) via <c>MiddleLayerService</c>. When false, the scan path is
    /// byte-for-byte the same as before (no per-row work).
    /// </summary>
    public bool EnrichmentEnabled { get; set; }

    // The enrichment sections to apply in-scan (resolved once per Step 2 from LoadedRules when
    // EnrichmentEnabled). Null/empty ⇒ EnrichRow is a no-op.
    private List<dynamic>? _enrichmentSections;
    // Accumulated enrichment cost for the current location (logged at location.end for visibility).
    private readonly System.Diagnostics.Stopwatch _enrichWatch = new();
    private int _enrichedRowCount;

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
        // Load rules now (idempotent) so the enrichment cache-key suffix can be computed here — the
        // streaming entry point pre-creates storage on the UI thread, before Step 1 runs.
        LoadRules();
        _resultStorage ??= CreateStorage(cancellationToken);
        return _resultStorage;
    }

    /// <summary>
    /// Cache-key suffix that distinguishes an enriched scan's cache db from the plain one (and from a
    /// different enrichment config). Empty when enrichment is off or no enrichment rules are loaded — so
    /// the default key is unchanged and existing caches still match. Only affects the db *filename*
    /// (hashed); the source signature still keys off the real log path.
    /// </summary>
    private string EnrichmentCacheSuffix()
    {
        if (!EnrichmentEnabled || LoadedRules == null) return string.Empty;
        try
        {
            var sections = _ruleLoader.GetSectionsByPurpose(LoadedRules, "enrichment");
            if (sections == null || sections.Count == 0) return string.Empty;
            var json = System.Text.Json.JsonSerializer.Serialize(sections);
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(json));
            return "|enrich=" + Convert.ToHexString(hash, 0, 4).ToLowerInvariant(); // 8 hex chars
        }
        catch { return "|enrich=on"; }
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
    /// <summary>
    /// Pure tiering decision for "Auto" storage, extracted so the thresholds are unit-testable and
    /// documented in one place:
    ///   &lt; 10k rows and a quick (&lt;30s) estimate -> InMemory (no disk, fastest for tiny logs)
    ///   10k–50k rows                            -> Hybrid   (RAM-hot; cheap settle at this size)
    ///   &gt; 50k rows                              -> SqlLite  (write straight to disk during the scan)
    /// </summary>
    public static StorageType ChooseAutoStorageType(int totalRecords, TimeSpan totalTime) =>
        (totalRecords < 10_000 && totalTime.TotalSeconds < 30) ? StorageType.InMemory
        : (totalRecords < 50_000) ? StorageType.Hybrid
        : StorageType.SqlLite;

    private ISearchStorage CreateStorage(CancellationToken cancellationToken)
    {
        var config = PluginManager.GetSingleton().config;
        var filePath = _locations.Count > 0 ? _locations[0].GetName() : "default";
        // Enriched scans get their own cache db (different rows than a plain scan). Suffix only changes
        // the hashed db filename; empty when enrichment is off so the default cache key is unchanged.
        filePath += EnrichmentCacheSuffix();
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
                var autoChoice = ChooseAutoStorageType(totalRecords, totalTime);
                // Report the actual storage CLASS name (matching the forced cases above), not the
                // StorageType enum — so the perf report's StorageType is consistent however it was chosen
                // (was "SqlLite" here vs "SqliteStorage" when forced).
                string autoTypeName = autoChoice switch
                {
                    StorageType.Hybrid => nameof(HybridStorage),
                    StorageType.InMemory => nameof(InMemoryStorage),
                    _ => nameof(SqliteStorage),
                };
                PerfLog.Log("storage.selected", ("type", autoTypeName), ("mode", "auto"), ("est", totalRecords));
                return autoChoice switch
                {
                    StorageType.InMemory => new InMemoryStorage(),
                    StorageType.Hybrid => new HybridStorage(filePath),
                    _ => new SqliteStorage(filePath),
                };
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
        if (LoadedRules != null) return; // idempotent: the UI path calls this from Step1, RunThrough also calls it
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
        // The UI drives Step1..Step4 directly (not RunThrough), so load rules + SystemConfig here too —
        // otherwise LoadedRules stays null and Step3 enrichment / Step4 rule outputs (UML) never run.
        LoadRules();
        ApplySystemConfig();
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
            // File OR folder — the cache signature (EvaluateCacheReuse) handles both, so a folder
            // source like the DISM Logs folder gets the same warm-cache skip-the-rescan fast path.
            if (FindNeedleCoreUtils.CachedStorage.SourceExists(path))
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
                                // Folder-aware signature so the prompt shows the aggregate size/mtime
                                // for a folder source, not a throw from FileInfo on a directory.
                                FindNeedleCoreUtils.CachedStorage.TryGetSourceSignature(
                                    path, out var sigSize, out var sigMtime, out _);
                                info = new CacheReusePromptInfo
                                {
                                    SourceFilePath = path,
                                    SourceFileSize = sigSize,
                                    SourceFileMtimeUtc = sigMtime,
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
            // The viewer reads cached rows straight from storage via IPagedLogSource. But RuleDSL
            // enrichment/output and any processors/outputs walk _currentResultList (not storage), so
            // when those are active we must still materialize the cached rows into the list — otherwise
            // Step3/Step4 run over nothing (e.g. the UML diagram comes out empty on a cache hit).
            bool needsListOnReuse = NeedsResultList();
            if (needsListOnReuse && _resultStorage != null)
            {
                var cached = new List<ISearchResult>();
                _resultStorage.GetFilteredResultsInBatches(b => cached.AddRange(b), 1000, cancellationToken);
                if (LoadedRules != null)
                {
                    using (PerfLog.Scope("rule_filter", ("in_rows", cached.Count)))
                        cached = ApplyRuleFiltering(cached);
                }
                _filteredResults = cached;
                _currentResultList = cached;
                Logger.Instance.Log($"Step2: cache reuse materialized {cached.Count} rows for rule/processor/output consumers.");
            }
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
        bool willMaterializeList = NeedsResultList();
        bool streamFilteredDuringScan =
            !useSync && noFilters && !willMaterializeList && _resultStorage is SqliteStorage;
        if (streamFilteredDuringScan)
            Logger.Instance.Log("Step2: streaming filtered rows straight to SQLite (constant memory, no rawResults retention)");

        // Resolve enrichment "extract" sections once. Applied per-row in EnrichBatch before the storage
        // insert (both the streaming and accumulate paths), so the extracted fields land in real columns.
        // Off/empty ⇒ EnrichBatch is a no-op and the scan path is unchanged. Doesn't force the list and
        // doesn't break the streaming fast path (it runs inside the scan).
        _enrichmentSections = (EnrichmentEnabled && LoadedRules != null)
            ? _ruleLoader.GetSectionsByPurpose(LoadedRules, "enrichment")
            : null;
        bool enrichActive = _enrichmentSections != null && _enrichmentSections.Count > 0;
        // Collect per-rule match/time stats during the scan so the UI can show which enrichment rule
        // costs what (and so the Active rules page stops showing "0 matched" for in-scan enrichment).
        _ruleEngine.RuleStats.Clear();
        _ruleEngine.CollectRuleStats = enrichActive;
        if (enrichActive)
            Logger.Instance.Log($"Step2: enrichment ON ({_enrichmentSections.Count} section(s)) — extracting fields per-row");

        // ----- Parallel fan-out ingest (large streaming loads) -----
        // One thread can't keep the disk busy while it also decodes/wraps events, so when the streaming
        // path is active for a large log we fan the per-shard SQLite inserts out across N writer threads,
        // then merge the shards into _resultStorage. Each shard row carries its global scan-order Id, so
        // the merged DB's default ORDER BY Id ASC still shows events in scan order. Gated by the toggle
        // (default on) and a row-estimate floor; off ⇒ the untouched serial insert below (and the viewer
        // can fill in live, which the fan-out can't — rows are queryable only after the merge).
        ParallelIngestSink ingestSink = null;
        if (streamFilteredDuringScan && SqliteStorage.ParallelIngestEnabled)
        {
            long estTotal = 0;
            foreach (var loc in _locations)
            {
                if (cancellationToken.IsCancellationRequested) break;
                try
                {
                    var pe = loc.GetSearchPerformanceEstimate(cancellationToken);
                    if (pe.recordCount.HasValue && pe.recordCount.Value > 0) estTotal += pe.recordCount.Value;
                }
                catch { /* unknown estimate — leave at 0 (stays serial below the floor) */ }
            }
            if (estTotal >= SqliteStorage.ParallelIngestMinRows)
            {
                ingestSink = new ParallelIngestSink(SqliteStorage.ParallelIngestShardCount(), cancellationToken);
                Logger.Instance.Log($"Step2: parallel fan-out ingest ON — {ingestSink.ShardCount} shards, ~{estTotal:N0} est rows");
                PerfLog.Log("ingest.parallel.begin", ("shards", ingestSink.ShardCount), ("est_rows", estTotal));
            }
        }
        // Bigger scan batches on the fan-out path: each shard insert is one transaction, so 1k batches mean
        // ~5k tiny transactions on a 5M log; 8k batches cut that ~8× (the single-writer serial path keeps
        // its tuned 1k default). This is the main lever closing the gap to the prototype's overlap.
        int scanBatchSize = ingestSink != null ? 8192 : 1000;

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
            _enrichWatch.Reset(); _enrichedRowCount = 0; // per-location enrichment cost, logged at location.end
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
                            // Filtered-only: in this path there are no filters, so raw == filtered,
                            // and nothing downstream reads RawResults — the viewer + cache reuse read
                            // FilteredResults, and consolidation is skipped (no_consumers). Writing the
                            // raw table too just doubles the SQLite insert work during the scan (on a
                            // 1.45M-row DISM folder that's the bulk of a ~21s load). See CaptureStats,
                            // which already treats raw as 0 on the streaming pipeline.
                            var enriched = EnrichBatch(batch);
                            if (ingestSink != null) ingestSink.Add(enriched);          // fan out to shard writers
                            else _resultStorage.AddFilteredBatch(enriched, cancellationToken); // serial single writer
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
                    }, cancellationToken, scanBatchSize).Wait();
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
                // During fan-out the rows live in shards (not _resultStorage) until the post-loop merge, so
                // report the sink's running produced count rather than SafeFilteredCount() (which is 0 then).
                long streamDoneRows = ingestSink != null ? ingestSink.ProducedCount : SafeFilteredCount();
                _stepnotifysink.progressSink.NotifyProgress(
                    locEndPctStream,
                    $"[{count}/{total}] done {basename} · {streamDoneRows:N0} rows · {storageLabel}");
                PerfLog.Log("location.end", ("idx", count), ("name", basename),
                    ("rows_total", streamDoneRows),
                    ("rows_this_loc_raw", streamedCount),
                    ("enriched", _enrichedRowCount), ("enrich_ms", _enrichWatch.ElapsedMilliseconds),
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

            // Enrich (extract fields) before storing, so the values land in the filtered store's columns.
            filteredBatch = EnrichBatch(filteredBatch);

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
                ("enriched", _enrichedRowCount), ("enrich_ms", _enrichWatch.ElapsedMilliseconds),
                ("elapsed_ms", Environment.TickCount64 - locStart));
            count++;
        }

        // ----- Merge the fan-out shards into _resultStorage -----
        // The scan wrote rows into N shard DBs; fold them into the real store (INSERT…SELECT, preserving
        // the global scan-order Id) so everything downstream — consolidate, FTS, the viewer — runs on one
        // DB exactly as the serial path produces. A shard-writer fault rethrows here; the toggle is the
        // operator's recovery (disable parallel ingest and re-run on the serial path).
        if (ingestSink != null)
        {
            using (ingestSink)
            {
                _stepnotifysink.progressSink.NotifyProgress(
                    100, $"merging {ingestSink.ProducedCount:N0} rows from {ingestSink.ShardCount} shards…");
                long merged = ingestSink.CompleteAndMergeInto((SqliteStorage)_resultStorage);
                Logger.Instance.Log($"Step2: parallel ingest merged {merged:N0} rows from {ingestSink.ShardCount} shards into {storageLabel}");
            }
            ingestSink = null;
        }

        // Per-rule enrichment cost (this scan) → perf log, so it's queryable via get_diagnostics.
        if (_ruleEngine.CollectRuleStats)
        {
            foreach (var s in EnrichmentRuleStats)
                PerfLog.Log("enrich.rule", ("name", s.Name), ("matches", s.Matches), ("ms", (long)s.Ms));
        }

        // ----- Decide whether to materialize the full result list in RAM -----
        // The list is only meaningful if something downstream walks it. Step3 walks it iff
        // processors are configured or rule-enrichment is loaded; Step4 walks it iff outputs
        // are configured or rule-output is loaded; the post-search legacy in-memory client-side
        // web viewer reads it via MiddleLayerService.GetLogLines (now lazy from storage). For
        // Quick Open on a huge log — no rules, no processors, no outputs — this is 36 seconds
        // of pure allocation. Skip it; downstream consumers fall back to the storage-backed path.
        int known = SafeFilteredCount();
        bool needsList = NeedsResultList();

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
        // The UI path calls Step2 directly and doesn't assign the return to _currentResultList (only
        // RunThrough does), so set it here — Step3 enrichment / Step4 rule outputs (UML) read it.
        _currentResultList = allResults;

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
    /// When set, Step4 does NOT run output rules / outputs automatically. The UI sets this so a search
    /// stays on the lazy "just view a log" path (no full-list consolidation, no output scan) even when
    /// an output rule — e.g. a UML diagram — is enabled; the diagram is generated on demand instead via
    /// <see cref="GenerateOutputsNow"/>. The CLI / RunThrough path leaves it false so a batch run still
    /// writes its outputs. Filter and enrichment rules are unaffected (they always apply).
    /// </summary>
    public bool DeferOutputs { get; set; }

    /// <summary>Whether this search has output rules (purpose "output") or output plugins to generate —
    /// lets the UI show a "Generate" action only when there's something to produce.</summary>
    public bool HasOutputRules =>
        (LoadedRules != null && _ruleLoader.GetSectionsByPurpose(LoadedRules, "output").Count > 0)
        || (_outputs != null && _outputs.Count > 0);

    /// <summary>
    /// Whether the loaded RuleDSL rules require the full in-RAM result list to be built right now.
    /// Filter / enrichment sections always do (they run during Step2/Step3); output sections only do
    /// when outputs aren't deferred. Lets a deferred-output search (e.g. an enabled UML diagram with no
    /// other rules) stay on the lazy storage-backed path.
    /// </summary>
    private bool LoadedRulesNeedListNow()
    {
        if (LoadedRules == null) return false;
        // Enrichment ("extract") is applied per-row during the Step 2 scan (see EnrichBatch), not over a
        // consolidated list — so it does NOT force materialization and stays on the lazy/streaming path.
        // Filter rules only feed outputs (the viewer reads raw scanned rows and applies filter rules via
        // its rule-view toggle). So filtering alone doesn't force materialization during a plain search —
        // it's needed only when outputs actually run. When outputs are deferred, neither does.
        if (DeferOutputs) return false;
        return _ruleLoader.GetSectionsByPurpose(LoadedRules, "output").Count > 0
            || _ruleLoader.GetSectionsByPurpose(LoadedRules, "filter").Count > 0;
    }

    /// <summary>Whether anything will consume the full list this run (so Step2 must consolidate it).</summary>
    private bool NeedsResultList() =>
        LoadedRulesNeedListNow()
        || (_processors != null && _processors.Count > 0)
        || (!DeferOutputs && _outputs != null && _outputs.Count > 0);

    /// <summary>
    /// Run the (previously deferred) output rules / outputs now, on demand. Consolidates the result
    /// list from storage first if the search stayed lazy (re-applying any rule filtering), then runs
    /// Step4. Returns the output files produced. Lets the UI generate a UML diagram explicitly instead
    /// of on every search.
    /// </summary>
    public List<string> GenerateOutputsNow(CancellationToken cancellationToken = default,
        DateTime? fromTime = null, DateTime? toTime = null)
    {
        if ((_currentResultList == null || _currentResultList.Count == 0) && _resultStorage != null)
        {
            var list = new List<ISearchResult>(Math.Max(SafeFilteredCount(), 1024));
            _resultStorage.GetFilteredResultsInBatches(b => list.AddRange(b), 1000, cancellationToken);
            if (LoadedRules != null)
                using (PerfLog.Scope("rule_filter", ("in_rows", list.Count)))
                    list = ApplyRuleFiltering(list);
            _currentResultList = list;
            _filteredResults = list;
        }

        // Optional time window — lets the UI generate a diagram over the same range the results view is
        // showing, without re-running the search. Filter a copy; the cached full list is untouched.
        var listForOutput = _currentResultList;
        if ((fromTime.HasValue || toTime.HasValue) && listForOutput != null)
        {
            int before = listForOutput.Count;
            listForOutput = listForOutput.Where(r =>
            {
                var t = r.GetLogTime();
                if (fromTime.HasValue && t < fromTime.Value) return false;
                if (toTime.HasValue && t > toTime.Value) return false;
                return true;
            }).ToList();
            PerfLog.Log("search.generate_outputs.timefilter", ("kept", listForOutput.Count), ("of", before));
        }

        bool prev = DeferOutputs;
        DeferOutputs = false;
        var savedCurrent = _currentResultList;
        var savedFiltered = _filteredResults;
        try
        {
            _currentResultList = listForOutput;
            _filteredResults = listForOutput;
            using (PerfLog.Scope("search.generate_outputs", ("rows", listForOutput?.Count ?? 0)))
                Step4_ProcessAllResultsToOutput(cancellationToken);
        }
        finally
        {
            _currentResultList = savedCurrent;
            _filteredResults = savedFiltered;
            DeferOutputs = prev;
        }

        return GeneratedRuleOutputFiles.Where(System.IO.File.Exists).ToList();
    }

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
            if (!FindNeedleCoreUtils.CachedStorage.SourceExists(path))
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
    /// Apply enrichment "extract" rules to a batch in place, replacing each row with an
    /// <see cref="findneedle.RuleDSL.EnrichedSearchResult"/> when any field was extracted. No-op when
    /// enrichment is off / has no sections / the batch is empty — so the fast scan path pays nothing.
    /// Accumulates the per-location cost into <c>_enrichWatch</c>/<c>_enrichedRowCount</c>.
    /// </summary>
    private List<ISearchResult> EnrichBatch(List<ISearchResult> batch)
    {
        if (_enrichmentSections == null || _enrichmentSections.Count == 0 || batch == null || batch.Count == 0)
            return batch;
        _enrichWatch.Start();
        for (int i = 0; i < batch.Count; i++)
        {
            var enriched = EnrichRow(batch[i]);
            if (!ReferenceEquals(enriched, batch[i])) { batch[i] = enriched; _enrichedRowCount++; }
        }
        _enrichWatch.Stop();
        return batch;
    }

    /// <summary>Evaluate the enrichment sections against one row; if any "extract" action produced
    /// fields, wrap the row so those fields override the stored values. Later sections win on conflict.</summary>
    private ISearchResult EnrichRow(ISearchResult r)
    {
        Dictionary<string, string>? fields = null;
        List<(int Start, int Length)>? strips = null;
        foreach (var section in _enrichmentSections!)
        {
            RuleEvaluationEngine.EvaluationResult eval;
            try { eval = _ruleEngine.EvaluateRules(r, section); }
            catch { continue; }
            if (eval.Fields.Count > 0)
            {
                fields ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in eval.Fields) fields[kv.Key] = kv.Value;
            }
            if (eval.MessageStrips.Count > 0)
            {
                strips ??= new List<(int, int)>();
                strips.AddRange(eval.MessageStrips);
            }
        }
        if (fields == null && strips == null) return r;
        // strip:true rules rewrite the displayed Message by removing the text they pulled out.
        if (strips != null)
        {
            var rewritten = ApplyMessageStrips(r.GetMessage(), strips);
            if (rewritten != null)
            {
                fields ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                fields["Message"] = rewritten;
            }
        }
        return fields != null ? new findneedle.RuleDSL.EnrichedSearchResult(r, fields) : r;
    }

    /// <summary>Remove the matched spans from the message (the text strip:true rules pulled out) and
    /// tidy the whitespace left behind, so the displayed line gets cleaner as fields move to columns.
    /// Returns null if nothing valid to remove. Out-of-range spans are skipped; overlaps are merged.</summary>
    private static string? ApplyMessageStrips(string message, List<(int Start, int Length)> strips)
    {
        if (string.IsNullOrEmpty(message) || strips == null || strips.Count == 0) return null;
        var valid = strips
            .Where(s => s.Start >= 0 && s.Length > 0 && s.Start + s.Length <= message.Length)
            .OrderBy(s => s.Start).ToList();
        if (valid.Count == 0) return null;

        // Merge overlapping/adjacent spans into [start,end) ranges.
        var merged = new List<(int Start, int End)>();
        foreach (var (st, len) in valid)
        {
            int end = st + len;
            if (merged.Count > 0 && st <= merged[^1].End)
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, end));
            else
                merged.Add((st, end));
        }

        var sb = new StringBuilder(message.Length);
        int pos = 0;
        foreach (var (st, end) in merged)
        {
            if (st > pos) sb.Append(message, pos, st - pos);
            pos = end;
        }
        if (pos < message.Length) sb.Append(message, pos, message.Length - pos);

        // Collapse the gaps left by removal (and the source's padding) so the line reads cleanly.
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s{2,}", " ").Trim();
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

        // Deferred: the UI runs outputs on demand (GenerateOutputsNow) so a normal search stays lazy.
        if (DeferOutputs)
        {
            PerfLog.Log("search.step4.deferred");
            Logger.Instance.Log("Step4: outputs deferred (run on demand via GenerateOutputsNow)");
            return;
        }

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
