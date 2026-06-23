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

    /// <summary>Optional enrichment on/off for the next search (null = use the persisted setting). The
    /// MCP <c>run_search</c> <c>enrich</c> arg sets this for one run, like <see cref="CacheModeOverride"/>.</summary>
    public static bool? EnrichmentOverride { get; set; }

    public static event Action StateChanged;

    public static void NotifyStateChanged() => StateChanged?.Invoke();

    /// <summary>Raised by <see cref="ClearWorkspace"/> so an already-open result viewer drops its
    /// loaded rows. Fires (on the UI thread) before any storage is released, so the viewer detaches
    /// its paged source before the underlying SQLite connection goes away.</summary>
    public static event Action WorkspaceCleared;

    // Set by ClearWorkspace, reset the moment a new result set is established (UpdateSearchQuery /
    // OpenCachedResult). While set, GetSearchStorage returns null so a re-navigate to the viewer
    // doesn't re-materialize the previous search's rows from the still-undisposed query storage.
    private static bool _workspaceCleared;

    public static void AddFolderLocation(string location)
    {
        // Don't add the same folder/file twice — repeated adds (e.g. an agent calling add_folder per run)
        // would otherwise scan it N times. Skip if a folder location with this path is already loaded.
        if (Locations.Any(l => l is FolderLocation f
                && string.Equals(f.path, location, StringComparison.OrdinalIgnoreCase)))
        {
            NotifyStateChanged();
            return;
        }

        var folderloc = new FolderLocation() { path = location };
        //Setup file extension processors
        var extensions = PluginManager.GetSingleton().GetAllPluginsInstancesOfAType<IFileExtensionProcessor>();

        folderloc.SetExtensionProcessorList(extensions);
        Locations.Add(folderloc);
        try { RecentLocationsStore.Record(location); } catch { /* recents are best-effort */ }
        NotifyStateChanged();
    }

    /// <summary>Add a pre-built non-folder location (e.g. a live Kusto cluster).</summary>
    public static void AddLocation(ISearchLocation location)
    {
        if (location == null) return;
        Locations.Add(location);
        NotifyStateChanged();
    }

    /// <summary>Remove all loaded locations + filters and cancel any in-flight search, so the workspace
    /// starts fresh. Surfaced as the "Clear workspace" button and the MCP clear_workspace tool.</summary>
    public static void ClearWorkspace()
    {
        try { CurrentStreamingSearch?.Stop(); } catch { /* ignore */ }
        CurrentStreamingSearch = null;
        Locations.Clear();
        Filters.Clear();
        SearchResults.Clear();
        ViewerQuickRulesStore.Clear(); // session right-click rules don't outlive the workspace
        OutputTimeFrom = OutputTimeTo = null;
        LastRunSummary = null;
        LastStats = null; // drop the previous run's decode-warning stats so its banner clears
        _workspaceCleared = true; // GetSearchStorage / GetStats now report "nothing to show"
        // Tell an open viewer to drop its source BEFORE we dispose any storage (avoids reading a
        // closed SQLite connection). Both callers (menu + MCP) run this on the UI thread, so the
        // handler completes synchronously here.
        try { WorkspaceCleared?.Invoke(); } catch { /* a viewer handler shouldn't block the clear */ }
        ClearOverrideStorage();
        NotifyStateChanged();
    }

    public static List<ISearchResult> GetSearchResults()
    {
        return SearchResults;
    }

    private static List<ISearchResult> SearchResults = new();

    /// <summary>Opt out of auto-adding rules for just the next search (reset after one run). The
    /// global on/off lives in <see cref="FindPluginCore.Searching.AutoRules.AutoRulesStore.Enabled"/>.</summary>
    public static bool SkipAutoRulesForNextSearch { get; set; }

    /// <summary>Rule paths that were auto-added to the most recent search (for the Rules page to show
    /// "these were added automatically").</summary>
    public static List<string> LastAutoAddedRules { get; private set; } = new();

    /// <summary>Files written by RuleDSL output rules in the most recent search (UML .mmd diagrams,
    /// rendered images, CSV/JSON exports). The Processor Output page surfaces these so generated
    /// diagrams/exports are discoverable — they're written straight to the output folder otherwise.</summary>
    public static List<string> LastRuleOutputFiles { get; private set; } = new();

    /// <summary>Human-readable summary of the most recent search (row count + cache/scanned), set on
    /// every search path so the main window status strip's "Last run" is accurate. Null until a run.</summary>
    public static string? LastRunSummary { get; private set; }

    /// <summary>The RuleDSL processor instances applied in the most recent search. The "Active rules"
    /// page reads their per-run stats (matched count + tag counts) after the search completes.</summary>
    public static List<FindNeedleRuleDSL.FindNeedleRuleDSLPlugin> LastRuleProcessors { get; private set; } = new();

    /// <summary>Per-rule cost of in-scan field-extraction enrichment from the most recent fresh scan
    /// (rule name → matches + ms). The Active rules page shows these so enrichment rules report real
    /// match counts (not 0) and the user can see/disable a slow rule. Empty after a warm cache reuse.</summary>
    public static IReadOnlyList<FindPluginCore.Searching.NuSearchQuery.EnrichmentRuleStat> LastEnrichmentRuleStats { get; private set; }
        = new List<FindPluginCore.Searching.NuSearchQuery.EnrichmentRuleStat>();

    // Provider/build metadata pre-scanned for the current search (populated by
    // PreparePendingAutoRuleMetadata before UpdateSearchQuery, cleared right after). Only computed when
    // an enabled auto-rule actually needs it, so the common case pays nothing.
    private static HashSet<string> _pendingAutoRuleProviders;
    private static int? _pendingAutoRuleBuild;

    // Cache of per-file ETL metadata keyed by "path|size|mtimeticks" so repeated searches of the same
    // file don't re-scan it.
    private static readonly Dictionary<string, (HashSet<string> providers, int? build)> _etlMetaCache = new();

    /// <summary>
    /// If any enabled auto-rule needs scanned metadata (providers / build), cheaply peek the ETL
    /// locations (capped quick-scan, cached per file) and stash the union of providers + the build for
    /// the upcoming <see cref="UpdateSearchQuery"/> to fold into the match context. No-op (and clears
    /// stale metadata) when no such rule is enabled. Call right before UpdateSearchQuery at search start.
    /// </summary>
    private static void PreparePendingAutoRuleMetadata()
    {
        _pendingAutoRuleProviders = null;
        _pendingAutoRuleBuild = null;
        try
        {
            if (!FindPluginCore.Searching.AutoRules.AutoRulesStore.AnyEnabledNeedsMetadata()) return;

            var providers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int? build = null;
            foreach (var loc in Locations ?? Enumerable.Empty<ISearchLocation>())
            {
                string path = "";
                try { path = loc?.GetName() ?? ""; } catch { }
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                var ext = System.IO.Path.GetExtension(path);

                if (ext.Equals(".etl", StringComparison.OrdinalIgnoreCase))
                {
                    var (p, b) = GetEtlMetaCached(path);   // providers + build
                    providers.UnionWith(p);
                    build ??= b;
                }
                else if (ext.Equals(".evtx", StringComparison.OrdinalIgnoreCase))
                {
                    providers.UnionWith(GetEvtxProvidersCached(path)); // providers only (no build in evtx)
                }
            }
            _pendingAutoRuleProviders = providers;
            _pendingAutoRuleBuild = build;
        }
        catch { /* best-effort; conditions needing metadata simply won't match */ }
    }

    private static void ClearPendingAutoRuleMetadata()
    {
        _pendingAutoRuleProviders = null;
        _pendingAutoRuleBuild = null;
    }

    private static (HashSet<string> providers, int? build) GetEtlMetaCached(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            var key = $"{path}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
            if (_etlMetaCache.TryGetValue(key, out var cached)) return cached;
            var meta = findneedle.ETWPlugin.EtlInfoExtractor.QuickScan(path);
            _etlMetaCache[key] = meta;
            return meta;
        }
        catch { return (new HashSet<string>(StringComparer.OrdinalIgnoreCase), null); }
    }

    private static readonly Dictionary<string, HashSet<string>> _evtxMetaCache = new();

    private static HashSet<string> GetEvtxProvidersCached(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            var key = $"{path}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
            if (_evtxMetaCache.TryGetValue(key, out var cached)) return cached;
            var providers = findneedle.Implementations.Locations.EventLogQueryLocation
                .EvtxMetaExtractor.QuickScanProviders(path);
            _evtxMetaCache[key] = providers;
            return providers;
        }
        catch { return new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
    }

    /// <summary>Build the auto-rule matching context from the current locations: their paths and the
    /// source kinds present (by location type + file extension), plus any provider/build metadata
    /// pre-scanned for this search (see <see cref="PreparePendingAutoRuleMetadata"/>).</summary>
    private static FindPluginCore.Searching.AutoRules.AutoRuleContext BuildAutoRuleContext(
        IEnumerable<ISearchLocation> locations)
    {
        var ctx = new FindPluginCore.Searching.AutoRules.AutoRuleContext();
        if (_pendingAutoRuleProviders != null) ctx.Providers = _pendingAutoRuleProviders;
        if (_pendingAutoRuleBuild.HasValue) ctx.Build = _pendingAutoRuleBuild;
        if (locations == null) return ctx;
        foreach (var loc in locations)
        {
            if (loc == null) continue;
            string name = "";
            try { name = loc.GetName() ?? ""; } catch { }
            if (!string.IsNullOrEmpty(name)) ctx.Paths.Add(name);

            var typeName = loc.GetType().Name;
            if (typeName.Contains("EventLog", StringComparison.OrdinalIgnoreCase))
                ctx.SourceTypes.Add(FindPluginCore.Searching.AutoRules.AutoRuleSourceKinds.EventLog);
            else if (typeName.Contains("Folder", StringComparison.OrdinalIgnoreCase))
                ctx.SourceTypes.Add(FindPluginCore.Searching.AutoRules.AutoRuleSourceKinds.Folder);

            // Refine by extension so ETW/EventLog/Zip files loaded via a folder location are detected.
            var ext = System.IO.Path.GetExtension(name);
            if (ext.Equals(".etl", StringComparison.OrdinalIgnoreCase))
                ctx.SourceTypes.Add(FindPluginCore.Searching.AutoRules.AutoRuleSourceKinds.Etw);
            else if (ext.Equals(".evtx", StringComparison.OrdinalIgnoreCase))
                ctx.SourceTypes.Add(FindPluginCore.Searching.AutoRules.AutoRuleSourceKinds.EventLog);
            else if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                ctx.SourceTypes.Add(FindPluginCore.Searching.AutoRules.AutoRuleSourceKinds.Zip);
        }
        return ctx;
    }

    public static void UpdateSearchQuery()
    {
        _workspaceCleared = false; // a new search establishes a fresh result set
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
            // Auto-add rules: fold in any registered auto-rules whose condition matches the current
            // locations (unless globally disabled or skipped for this one search). They're merged into
            // RulesConfigPaths so they flow through the normal rules→processors path below and show up
            // in the Rules page; LastAutoAddedRules records which ones were added (for the UI).
            query.RulesConfigPaths ??= new List<string>();
            var ctx = BuildAutoRuleContext(Locations);
            var autoPaths = FindPluginCore.Searching.AutoRules.AutoRulesStore
                .ResolveForSearch(ctx, SkipAutoRulesForNextSearch);
            LastAutoAddedRules = new List<string>();
            foreach (var p in autoPaths)
            {
                if (!query.RulesConfigPaths.Any(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase)))
                {
                    query.RulesConfigPaths.Add(p);
                    LastAutoAddedRules.Add(p);
                }
            }
            SkipAutoRulesForNextSearch = false; // one-shot opt-out

            // When field-extraction enrichment is ON, auto-attach the bundled "extract" enrichment rules
            // whose condition matches these locations — regardless of whether the user enabled them as an
            // auto-add rule. This makes the enrichment toggle self-sufficient: flip it on and the shipped
            // extract rules (e.g. DISM PID/TID/Provider) apply, instead of silently doing nothing until a
            // separate rule is enabled. Reuses AutoRuleResolver.Matches (the same condition matcher).
            bool enrichOn = EnrichmentOverride ?? ResultsViewerSettings.EnrichmentEnabled;
            if (enrichOn)
            {
                foreach (var lib in FindPluginCore.Searching.AutoRules.CommonRuleLibrary.Discover())
                {
                    if (string.IsNullOrEmpty(lib.RulePath) || !System.IO.File.Exists(lib.RulePath)) continue;
                    if (!RuleFileHasExtractEnrichment(lib.RulePath)) continue;
                    if (!FindPluginCore.Searching.AutoRules.AutoRuleResolver.Matches(lib.Condition, ctx)) continue;
                    if (!query.RulesConfigPaths.Any(x => string.Equals(x, lib.RulePath, StringComparison.OrdinalIgnoreCase)))
                    {
                        query.RulesConfigPaths.Add(lib.RulePath);
                        LastAutoAddedRules.Add(lib.RulePath);
                    }
                }
            }

            // Add RuleDSL processors for each rules config file. Keep references to the instances so the
            // "Active rules" page can read their per-run stats (matched count + tag counts) after search.
            LastRuleProcessors = new List<FindNeedleRuleDSL.FindNeedleRuleDSLPlugin>();
            if (query.RulesConfigPaths != null && query.RulesConfigPaths.Count > 0)
            {
                foreach (var rulesPath in query.RulesConfigPaths)
                {
                    if (System.IO.File.Exists(rulesPath))
                    {
                        var ruleDslProcessor = new FindNeedleRuleDSL.FindNeedleRuleDSLPlugin("*", rulesPath);
                        LastRuleProcessors.Add(ruleDslProcessor);
                        // Only rules with filter/enrichment sections need a Step3 processor (which forces
                        // the full result list to materialize). Output-only rules — e.g. a UML diagram —
                        // run via the rules-output path (Step4 / GenerateOutputsNow) instead, so adding
                        // them as processors would needlessly consolidate 1.4M rows on every search.
                        if (RuleFileHasProcessableSections(rulesPath))
                            enabledProcessors.Add(ruleDslProcessor);
                        System.Diagnostics.Debug.WriteLine($"Added RuleDSL processor for: {rulesPath}");
                    }
                }
            }

            query.Processors = enabledProcessors;

            SearchQueryUX.UpdateSearchQuery();
            // UpdateSearchQuery() swaps in a brand-new query object; UpdateAllParameters copies
            // processors/outputs/stats onto it but NOT RulesConfigPaths. Carry those over too —
            // otherwise the running query's LoadedRules stays null and Step4 rule outputs (UML)
            // never run, even though the RuleDSL processors were copied and run in Step3.
            if (SearchQueryUX.CurrentQuery != null && query != null)
                SearchQueryUX.CurrentQuery.RulesConfigPaths = query.RulesConfigPaths;
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
                // RuleDSL "extract" enrichment (off by default; the MCP run_search 'enrich' arg can
                // force it per run via EnrichmentOverride).
                nu.EnrichmentEnabled = EnrichmentOverride ?? ResultsViewerSettings.EnrichmentEnabled;
                // Apply per-run storage / estimate overrides (set by the CLI load hook). These let
                // the storage backend and the Auto-tier prediction be driven explicitly.
                if (StorageOverride.HasValue) nu.OverrideStorageType = StorageOverride.Value;
                if (RowEstimateOverride.HasValue) nu.EstimatedRowCountOverride = RowEstimateOverride.Value;
                // Lazy/Background modes defer the in-search FTS index build so the viewer opens
                // before the (potentially multi-minute) index finishes; Eager builds it in Step2.
                nu.DeferIndexBuild = EffectiveIndexingMode != FindPluginCore.Searching.IndexingMode.Eager;
                // Don't run output rules (UML diagrams, exports) on every search — that forces the full
                // result list to materialize and re-scans it each run. The UI generates outputs on demand
                // (GenerateRuleOutputs), keeping plain "view a log" searches on the fast lazy path.
                nu.DeferOutputs = true;
            }
        }
    }

    /// <summary>
    /// Run the (deferred) RuleDSL output rules / outputs now — e.g. generate the UML diagram on demand.
    /// Scans the current results once; safe to call off the UI thread. Updates
    /// <see cref="LastRuleOutputFiles"/> and notifies. Returns the files produced.
    /// </summary>
    /// <summary>The results view's current time range (From/To), pushed here by the viewer so deferred
    /// output generation (UML diagrams) can reuse it instead of asking for the range again. Null = no
    /// bound. Reset on Clear workspace / Clear filters.</summary>
    public static DateTime? OutputTimeFrom { get; set; }
    public static DateTime? OutputTimeTo { get; set; }

    public static List<string> GenerateRuleOutputs(System.Threading.CancellationToken ct = default)
    {
        if (SearchQueryUX.CurrentQuery is NuSearchQuery nu)
        {
            var files = nu.GenerateOutputsNow(ct, OutputTimeFrom, OutputTimeTo);
            LastRuleOutputFiles = files;
            NotifyStateChanged();
            return files;
        }
        return new List<string>();
    }

    /// <summary>
    /// True if a rule file needs a Step3 processor — i.e. it has an enrichment (or unknown-purpose)
    /// section that transforms the in-RAM result list. Filter and output sections do NOT: filtering is
    /// applied to outputs on demand (and to the viewer via the rule-view toggle), and output rules run
    /// via the on-demand output path. Registering a processor forces the full result list to consolidate
    /// on every search, so we only do it when there's enrichment work that actually needs it. Conservative:
    /// any parse problem or unknown shape returns true.
    /// </summary>
    internal static bool RuleFileHasProcessableSections(string path)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
            var root = doc.RootElement;
            if (!root.TryGetProperty("sections", out var secs) && !root.TryGetProperty("Sections", out secs))
                return true; // unknown shape → assume it needs processing
            if (secs.ValueKind != System.Text.Json.JsonValueKind.Array) return true;
            foreach (var s in secs.EnumerateArray())
            {
                string purpose = null;
                if (s.TryGetProperty("purpose", out var p) || s.TryGetProperty("Purpose", out p))
                    purpose = p.GetString();
                // Filter / output never need a Step3 processor (applied on demand / via the viewer toggle).
                if (string.Equals(purpose, "output", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(purpose, "filter", StringComparison.OrdinalIgnoreCase))
                    continue;
                // Extract-only enrichment is applied per-row during the scan (NuSearchQuery.EnrichBatch),
                // not over the consolidated list — so it does NOT need a processor and stays on the lazy
                // path. Any other enrichment (e.g. a "tag" action) still does. Unknown shape → processable.
                if (string.Equals(purpose, "enrichment", StringComparison.OrdinalIgnoreCase)
                    && SectionRulesAreAllExtract(s))
                    continue;
                return true;
            }
            return false; // only filter/output/extract-enrichment sections → no Step3 processor needed
        }
        catch { return true; }
    }

    /// <summary>True if the rule file has at least one enrichment section containing an "extract" action —
    /// i.e. it's a field-extraction enrichment rule the enrichment toggle should auto-attach.</summary>
    internal static bool RuleFileHasExtractEnrichment(string path)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("sections", out var secs) && !doc.RootElement.TryGetProperty("Sections", out secs))
                return false;
            if (secs.ValueKind != System.Text.Json.JsonValueKind.Array) return false;
            foreach (var s in secs.EnumerateArray())
            {
                string purpose = null;
                if (s.TryGetProperty("purpose", out var p) || s.TryGetProperty("Purpose", out p)) purpose = p.GetString();
                if (!string.Equals(purpose, "enrichment", StringComparison.OrdinalIgnoreCase)) continue;
                if (!s.TryGetProperty("rules", out var rules) && !s.TryGetProperty("Rules", out rules)) continue;
                if (rules.ValueKind != System.Text.Json.JsonValueKind.Array) continue;
                foreach (var rule in rules.EnumerateArray())
                {
                    if (!rule.TryGetProperty("action", out var action) && !rule.TryGetProperty("Action", out action)) continue;
                    string type = null;
                    if (action.TryGetProperty("type", out var t) || action.TryGetProperty("Type", out t)) type = t.GetString();
                    if (string.Equals(type, "extract", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
        }
        catch { /* unreadable → not an extract rule */ }
        return false;
    }

    /// <summary>True if every rule in the section has an "extract" action (so the whole section is handled
    /// in-scan, not by a Step3 processor). A section with no rules is vacuously all-extract.</summary>
    private static bool SectionRulesAreAllExtract(System.Text.Json.JsonElement section)
    {
        if (!section.TryGetProperty("rules", out var rules) && !section.TryGetProperty("Rules", out rules))
            return false;
        if (rules.ValueKind != System.Text.Json.JsonValueKind.Array) return false;
        foreach (var rule in rules.EnumerateArray())
        {
            if (!rule.TryGetProperty("action", out var action) && !rule.TryGetProperty("Action", out action))
                return false; // a rule without a single action (e.g. an actions array) → be conservative
            string type = null;
            if (action.TryGetProperty("type", out var t) || action.TryGetProperty("Type", out t))
                type = t.GetString();
            if (!string.Equals(type, "extract", StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    /// <summary>Whether any active rule can produce a UML diagram (a UML output rule is enabled) — used to
    /// show "UML diagram available" in the status bar before it's generated.</summary>
    public static bool HasUmlOutputRule
    {
        get
        {
            var paths = SearchQueryUX.CurrentQuery?.RulesConfigPaths;
            if (paths == null) return false;
            foreach (var p in paths) if (RuleFileHasUmlOutput(p)) return true;
            return false;
        }
    }

    private static bool RuleFileHasUmlOutput(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return false;
            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("sections", out var secs) && !doc.RootElement.TryGetProperty("Sections", out secs))
                return false;
            if (secs.ValueKind != System.Text.Json.JsonValueKind.Array) return false;
            foreach (var s in secs.EnumerateArray())
            {
                string purpose = s.TryGetProperty("purpose", out var p) ? p.GetString()
                               : s.TryGetProperty("Purpose", out var p2) ? p2.GetString() : null;
                if (!string.Equals(purpose, "output", StringComparison.OrdinalIgnoreCase)) continue;
                if (!s.TryGetProperty("rules", out var rules) && !s.TryGetProperty("Rules", out rules)) continue;
                if (rules.ValueKind != System.Text.Json.JsonValueKind.Array) continue;
                foreach (var r in rules.EnumerateArray())
                {
                    if ((r.TryGetProperty("enabled", out var en) || r.TryGetProperty("Enabled", out en))
                        && en.ValueKind == System.Text.Json.JsonValueKind.False) continue;
                    if (!r.TryGetProperty("action", out var act) && !r.TryGetProperty("Action", out act)) continue;
                    string type = act.TryGetProperty("type", out var ty) ? ty.GetString()
                                : act.TryGetProperty("Type", out var ty2) ? ty2.GetString() : null;
                    if (string.Equals(type, "uml", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }
        catch { return false; }
    }

    /// <summary>Generated diagram files (.mmd/.puml/.png) from the last search or on-demand generation.</summary>
    public static List<string> GeneratedDiagramFiles
    {
        get
        {
            var files = new List<string>();
            if (LastRuleOutputFiles != null) files.AddRange(LastRuleOutputFiles);
            if (SearchQueryUX.CurrentQuery is NuSearchQuery nq && nq.GeneratedRuleOutputFiles != null)
                files.AddRange(nq.GeneratedRuleOutputFiles);
            return files
                .Where(f => f.EndsWith(".mmd", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".puml", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .Where(System.IO.File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>Whether the current search has output rules/outputs to generate (drives the UI button).</summary>
    public static bool HasOutputRules =>
        (SearchQueryUX.CurrentQuery as NuSearchQuery)?.HasOutputRules ?? false;

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
        LastStats = null;        // drop the previous file's decode/stats so its warning banner clears
        // Stop any background index build from a previous search before we wipe/replace storage.
        CancelBackgroundIndexBuild();

        PreparePendingAutoRuleMetadata();
        UpdateSearchQuery();
        ClearPendingAutoRuleMetadata();
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
        {
            CaptureStats(nuDone, GetSearchStorage());
            // Surface any files written by RuleDSL output rules (UML diagrams, exports) so the
            // Processor Output page can list/render them — they're otherwise written silently.
            LastRuleOutputFiles = nuDone.GeneratedRuleOutputFiles
                .Where(System.IO.File.Exists).ToList();
        }
        else
            LastStats = x;
        try { PerfReport.SetSource(string.Join(", ", Locations.Select(l => l.GetName()))); } catch { /* label only */ }

        // Record a "last run" summary centrally so every entry point (Open log file, Run search,
        // cached search, etc.) updates the status strip — not just the menu Run-search action.
        try
        {
            var count = GetFilteredRowCount();
            LastRunSummary = $"{count:N0} result{(count == 1 ? "" : "s")}{(LastSearchReusedCache ? " (cached)" : " (scanned)")}";
        }
        catch { LastRunSummary = "done"; }

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
            // Per-rule enrichment cost from this scan (empty after a cache reuse). Captured before the
            // early-return so the Active rules page can show real matches + ms.
            try { if (nu != null) LastEnrichmentRuleStats = nu.EnrichmentRuleStats; }
            catch { /* best-effort */ }

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
        if (_workspaceCleared) return null; // cleared workspace has no stats — drops the decode banner
        // Prefer the captured run; fall back to the current query (covers legacy SearchQuery and
        // NuSearchQuery — GetSearchStatistics() is on the ISearchQuery interface).
        return LastStats ?? SearchQueryUX.CurrentQuery?.GetSearchStatistics();
    }

    /// <summary>
    /// If the most recent search hit a decode problem (e.g. a WPP ETL whose symbols/TMFs are missing),
    /// returns a human-readable headline + detail explaining WHY results are empty/partial, plus whether
    /// it was a hard failure (nothing decoded). Returns null when nothing's wrong. Drives the viewer's
    /// "why is this empty?" banner.
    /// </summary>
    public static (string headline, string detail, bool hardFailure, string? missingTmfs)? GetDecodeWarning()
    {
        var stats = GetStats();
        if (stats?.componentReports == null) return null;

        foreach (var list in stats.componentReports.Values)
        {
            foreach (var report in list)
            {
                if (report?.summary != "DecodeByFile" || report.metric == null) continue;
                foreach (var kv in report.metric)
                {
                    if (kv.Value is not IDictionary d) continue;
                    var method = (d.Contains("method") ? d["method"]?.ToString() : null) ?? "";
                    var missing = d.Contains("missingTmfs") ? d["missingTmfs"]?.ToString() : null;
                    var file = Path.GetFileName(kv.Key);

                    if (method.Contains("symbols missing", StringComparison.OrdinalIgnoreCase))
                    {
                        var detail =
                            $"“{file}” is a WPP trace, and the formatting symbols (TMF files) needed to turn its " +
                            "events into readable text aren't available — so almost nothing could be decoded.";
                        if (!string.IsNullOrEmpty(missing))
                            detail += $"\n\n{SummarizeTmfs(missing)}";
                        detail +=
                            "\n\nTo fix: open WPP symbol settings below, point it at the folder with the matching PDBs " +
                            "(or a symbol path / symbol server), click “Build TMFs from symbols,” then reopen this file.";
                        return ("Couldn’t decode this ETL — missing WPP symbols", detail, true, missing);
                    }
                    if (!string.IsNullOrEmpty(missing))
                    {
                        return ("Some events couldn’t be decoded — missing WPP symbols",
                            $"“{file}” has events with no matching WPP symbols (TMF), so they show as raw/unformatted.\n\n" +
                            $"{SummarizeTmfs(missing)}\n\nSet a symbol/TMF path in WPP symbol settings below and reopen for full text.",
                            false, missing);
                    }
                }
            }
        }
        return null;
    }

    /// <summary>Summarize the missing-TMF GUID list for the decode banner. Each GUID is 36 chars, so a
    /// raw comma-joined list of many wraps into a wall of text that buries the actionable message —
    /// show the count and the first few, and point at the Copy button for the rest (Copy still puts the
    /// full list on the clipboard).</summary>
    private static string SummarizeTmfs(string missing)
    {
        if (string.IsNullOrEmpty(missing)) return "";
        var guids = missing.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
        const int show = 4;
        if (guids.Length <= show)
            return $"Needs TMF ({guids.Length}): {missing}";
        var shown = string.Join(", ", guids.Take(show));
        return $"Needs TMF ({guids.Length}): {shown} — and {guids.Length - show} more (click Copy for the full list).";
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
                IsEditable = loc is KustoPlugin.Location.KustoLocation
                          or ADOPlugin.Location.AdoLocation
                          or GithubPlugin.Location.GithubIssuesLocation,
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

    /// <summary>Path of the cache .db currently being viewed (held open), or null. The cache page
    /// disables Delete for this one since the file is locked while open.</summary>
    public static string? OpenCacheDbPath { get; private set; }

    public static FindNeedlePluginLib.Interfaces.ISearchStorage? GetSearchStorage()
    {
        if (_workspaceCleared) return null; // workspace was cleared; don't expose the old query's rows
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
        _workspaceCleared = false; // viewing a cache is a fresh result set
        OpenCacheDbPath = dbPath;
        LastStats = null; // the cache has no live SearchStatistics
        NotifyStateChanged();
    }

    /// <summary>Drop any cache-viewing override so the next viewer load reads the live search again.</summary>
    private static void ClearOverrideStorage()
    {
        if (_overrideStorage == null) return;
        try { _overrideStorage.Dispose(); } catch { /* ignore */ }
        _overrideStorage = null;
        OpenCacheDbPath = null;
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
        LastStats = null;        // drop the previous file's decode/stats so its warning banner clears

        PreparePendingAutoRuleMetadata();
        UpdateSearchQuery();
        ClearPendingAutoRuleMetadata();
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
