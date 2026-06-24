using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FindNeedlePluginLib.Interfaces;

namespace FindNeedleUX.Services.Mcp;

/// <summary>
/// Single entry point the MCP host calls to read and drive the app's one live workspace — the same
/// locations, search query, and active result viewer the user sees. There is no separate "session":
/// agent actions mutate the shared GUI state and show up immediately.
///
/// Two kinds of operation:
///   * Workspace (locations / rules / run search) go through <see cref="MiddleLayerService"/>,
///     marshaled onto the UI thread.
///   * Viewer (filter / sort / page / record / tag / select) are delegated to the registered
///     <see cref="IMcpViewerController"/> (the active <c>NativeResultsPage</c>). When no viewer is
///     registered these throw <see cref="McpNoViewerException"/> so the host can report
///     "no active view".
/// </summary>
public sealed class McpViewerBridge
{
    public static McpViewerBridge Instance { get; } = new();

    private McpViewerBridge() { }

    private IMcpViewerController _controller;
    private readonly object _sync = new();

    /// <summary>
    /// UI-thread dispatcher used to marshal workspace mutations. Set once at app startup (MainWindow)
    /// so workspace tools work even before any viewer is open.
    /// </summary>
    public Microsoft.UI.Dispatching.DispatcherQueue UiDispatcher { get; set; }

    /// <summary>Port the MCP server is listening on (0 if stopped). Set by <c>McpServerHost</c>.</summary>
    public int ServerPort { get; set; }

    /// <summary>
    /// Optional handler that runs a search the same way the Search button does (run + navigate to the
    /// viewer). Wired by the search page. When unset, <see cref="RunSearchAsync"/> falls back to a
    /// direct streaming search via <see cref="MiddleLayerService"/>.
    /// </summary>
    public Func<Task> RunSearchHandler { get; set; }

    // ----- viewer registration -----

    public void RegisterViewer(IMcpViewerController controller)
    {
        lock (_sync) _controller = controller;
    }

    public void UnregisterViewer(IMcpViewerController controller)
    {
        lock (_sync) { if (ReferenceEquals(_controller, controller)) _controller = null; }
    }

    public bool HasViewer { get { lock (_sync) return _controller != null; } }

    /// <summary>The registered viewer or null (no throw) — for best-effort calls like a post-search reload.</summary>
    private IMcpViewerController TryViewer { get { lock (_sync) return _controller; } }

    /// <summary>Rebind the open viewer (if any) to the current search's storage. Called after a re-run so
    /// the viewer doesn't keep reading the previous, now-disposed store ("connection open" errors).</summary>
    private async Task ReloadViewerAsync()
    {
        var v = TryViewer;
        if (v != null) { try { await v.ReloadAsync().ConfigureAwait(false); } catch { /* best-effort */ } }
    }

    /// <summary>
    /// Wait up to <paramref name="timeoutMs"/> for a result viewer to register (e.g. just after app
    /// launch or a search that opens the viewer). Returns true if one is registered by then. Lets an
    /// agent avoid the "no viewer" race without polling.
    /// </summary>
    public async Task<bool> WaitForViewerAsync(int timeoutMs)
    {
        if (timeoutMs <= 0) timeoutMs = 10_000;
        if (timeoutMs > 120_000) timeoutMs = 120_000;
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!HasViewer && Environment.TickCount64 < deadline)
            await Task.Delay(100).ConfigureAwait(false);
        return HasViewer;
    }

    /// <summary>Health/orientation snapshot. Never throws on the no-viewer path.</summary>
    public async Task<StatusDto> GetStatusAsync()
    {
        var dto = new StatusDto
        {
            ServerRunning = ServerPort > 0,
            Port = ServerPort,
            HasViewer = HasViewer,
            Locations = await RunOnUiAsync(() => MiddleLayerService.Locations.Count).ConfigureAwait(false),
        };
        if (dto.HasViewer)
        {
            try { var v = await GetViewAsync().ConfigureAwait(false); dto.Total = v.Total; dto.TotalFiltered = v.TotalFiltered; dto.Streaming = v.Streaming; dto.Loading = v.Loading; }
            catch (McpNoViewerException) { dto.HasViewer = false; } // viewer closed between the check and the call
        }
        return dto;
    }

    private IMcpViewerController Viewer
    {
        get
        {
            lock (_sync)
            {
                return _controller ?? throw new McpNoViewerException(
                    "No result viewer is open. Run a search to open one before using viewer tools.");
            }
        }
    }

    // ----- viewer ops (delegated) -----

    public Task<ViewStateDto> GetViewAsync() => Viewer.GetViewAsync();
    public Task<PageDto> GetPageAsync(int? offset, int limit) => Viewer.GetPageAsync(offset, limit);
    public Task<RecordDto> GetRecordAsync(long rowId) => Viewer.GetRecordAsync(rowId);
    public Task<SummaryDto> GetSummaryAsync() => Viewer.GetSummaryAsync();
    public Task<List<HistogramBucketDto>> GetHistogramAsync(int buckets) => Viewer.GetHistogramAsync(buckets);
    public Task<LogAnalysis.FacetResult> GetFacetsAsync(string field, int limit, int sampleCap) => Viewer.GetFacetsAsync(field, limit, sampleCap);
    public Task<LogAnalysis.PatternResult> GetTopPatternsAsync(int limit, int sampleCap) => Viewer.GetTopPatternsAsync(limit, sampleCap);
    public Task<int> SetFilterAsync(string search, string provider, string taskName, string message,
        string source, string level, string fromTime, string toTime)
        => Viewer.SetFilterAsync(search, provider, taskName, message, source, level, fromTime, toTime);
    public Task<int> ClearFiltersAsync() => Viewer.ClearFiltersAsync();
    public Task<int> SetRuleViewFilterAsync(bool on) => Viewer.SetRuleViewFilterAsync(on);
    public Task<bool> WaitForLoadAsync(int timeoutMs) => Viewer.WaitForLoadAsync(timeoutMs);
    public Task SetSortAsync(string column, bool descending) => Viewer.SetSortAsync(column, descending);
    public Task GoToPageAsync(int page) => Viewer.GoToPageAsync(page);
    public Task SetPageSizeAsync(int pageSize) => Viewer.SetPageSizeAsync(pageSize);
    public Task<bool> SelectRowAsync(long rowId) => Viewer.SelectRowAsync(rowId);
    public Task<bool> TagRowAsync(long rowId, string tag, string text) => Viewer.TagRowAsync(rowId, tag, text);
    public Task<bool> ClearTagAsync(long rowId) => Viewer.ClearTagAsync(rowId);
    public Task<List<TagCountDto>> GetTagCountsAsync() => Viewer.GetTagCountsAsync();
    public Task<List<RecordDto>> GetTaggedRowsAsync(string tag) => Viewer.GetTaggedRowsAsync(tag);
    public Task<ContextDto> GetContextAsync(long rowId, int before, int after) => Viewer.GetContextAsync(rowId, before, after);
    public Task SetDetailsModeAsync(string mode) => Viewer.SetDetailsModeAsync(mode);
    public Task<ExportResultDto> ExportAsync(string format, string destPath) => Viewer.ExportAsync(format, destPath);

    // ----- workspace ops (MiddleLayerService, marshaled to the UI thread) -----

    public Task<List<LocationDto>> ListLocationsAsync() => RunOnUiAsync(() =>
    {
        var list = new List<LocationDto>();
        foreach (var loc in MiddleLayerService.Locations)
        {
            string name, desc;
            try { name = loc.GetName(); } catch { name = "(unknown)"; }
            try { desc = loc.GetDescription(); } catch { desc = ""; }
            list.Add(new LocationDto
            {
                Name = name,
                Description = desc,
                IsEditable = loc is KustoPlugin.Location.KustoLocation,
            });
        }
        return list;
    });

    public Task<List<string>> ListRulesAsync() => RunOnUiAsync(() =>
        MiddleLayerService.SearchQueryUX?.CurrentQuery?.RulesConfigPaths is { } r
            ? new List<string>(r) : new List<string>());

    public Task AddFolderAsync(string path) => RunOnUiAsync(() =>
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required");
        MiddleLayerService.AddFolderLocation(path);
    });

    /// <summary>
    /// Replace the RuleDSL rule files applied to the search. Accepts absolute paths or bare names
    /// resolved against the app's CommonRules folder. Set on the current query's RulesConfigPaths
    /// (carried into the next search); auto-rules matching the locations are still folded in at
    /// run_search. Returns the resolved paths and any that couldn't be found.
    /// </summary>
    public Task<object> SetRulesAsync(IReadOnlyList<string> paths) => RunOnUiAsync<object>(() =>
    {
        var q = MiddleLayerService.SearchQueryUX?.CurrentQuery
            ?? throw new InvalidOperationException("no active search query");
        var resolved = new List<string>();
        var missing = new List<string>();
        foreach (var raw in paths ?? Array.Empty<string>())
        {
            var r = ResolveRulePath(raw);
            if (r != null) resolved.Add(r); else missing.Add(raw);
        }
        q.RulesConfigPaths = resolved;
        return new { set = resolved, missing };
    });

    /// <summary>Resolve a rule path: as-is, else under the AI-authored rules folder, else under the
    /// app's CommonRules folder, else under the app base. Returns null if it can't be found.</summary>
    private static string ResolveRulePath(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            if (File.Exists(raw)) return Path.GetFullPath(raw);
            // AI-authored rules (saved via save_rule) resolve by bare name too.
            var underAi = Path.Combine(AiRulesDir, Path.GetFileName(raw));
            if (File.Exists(underAi)) return underAi;
            var baseDir = AppContext.BaseDirectory;
            var underCommon = Path.Combine(baseDir, "CommonRules", raw);
            if (File.Exists(underCommon)) return underCommon;
            var underBase = Path.Combine(baseDir, raw);
            if (File.Exists(underBase)) return underBase;
        }
        catch { /* fall through to not-found */ }
        return null;
    }

    // ===================== AI rule authoring (save / validate / list / read / delete) =====================
    // A sandboxed folder so an agent can write rules over MCP without touching the shipped CommonRules.

    /// <summary>Folder for AI-authored rule files (sandbox). Created on first write.</summary>
    private static string AiRulesDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FindNeedle", "AIRules");

    private static string CommonRulesDir => Path.Combine(AppContext.BaseDirectory, "CommonRules");

    /// <summary>Normalize a requested rule name into a safe "<name>.rules.json" filename inside the
    /// sandbox (strips any directory components, rejects traversal, enforces the extension).</summary>
    private static string SafeRuleFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("rule name is required");
        var leaf = Path.GetFileName(name.Trim());           // drop any path components → no traversal
        if (string.IsNullOrEmpty(leaf)) throw new ArgumentException("invalid rule name");
        // Collapse to a known-good filename + the canonical .rules.json suffix.
        if (leaf.EndsWith(".rules.json", StringComparison.OrdinalIgnoreCase)) { /* keep */ }
        else if (leaf.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            leaf = leaf.Substring(0, leaf.Length - 5) + ".rules.json";
        else
            leaf = leaf + ".rules.json";
        if (leaf.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException($"rule name has invalid filename characters: '{name}'");
        return leaf;
    }

    /// <summary>Structurally validate a rule JSON document. Returns (valid, errors, warnings, summary).
    /// Checks it parses and matches one of the two shapes the engine understands: a sections file
    /// (filter/enrichment/output rules) or a UML participants/interaction file.</summary>
    private static (bool valid, List<string> errors, List<string> warnings, object summary) ValidateRuleStructured(string json)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(json)) { errors.Add("rule JSON is empty"); return (false, errors, warnings, null); }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { errors.Add($"JSON parse error: {ex.Message}"); return (false, errors, warnings, null); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { errors.Add("top level must be a JSON object"); return (false, errors, warnings, null); }

            bool hasSections = root.TryGetProperty("sections", out var sections) || root.TryGetProperty("Sections", out sections);
            bool hasParticipants = root.TryGetProperty("participants", out var parts) || root.TryGetProperty("Participants", out parts);

            int sectionCount = 0, ruleCount = 0; var purposes = new List<string>();
            if (hasSections)
            {
                if (sections.ValueKind != JsonValueKind.Array) errors.Add("'sections' must be an array");
                else foreach (var s in sections.EnumerateArray())
                {
                    sectionCount++;
                    var purpose = (s.TryGetProperty("purpose", out var p) || s.TryGetProperty("Purpose", out p)) ? p.GetString() : null;
                    if (string.IsNullOrEmpty(purpose)) errors.Add($"section #{sectionCount} is missing 'purpose'");
                    else
                    {
                        purposes.Add(purpose);
                        var known = new[] { "filter", "enrichment", "output", "uml" };
                        if (!known.Contains(purpose.ToLowerInvariant())) warnings.Add($"section #{sectionCount} has unusual purpose '{purpose}' (expected one of: {string.Join(", ", known)})");
                    }
                    if (!(s.TryGetProperty("rules", out var rs) || s.TryGetProperty("Rules", out rs)) || rs.ValueKind != JsonValueKind.Array)
                        errors.Add($"section #{sectionCount} ('{purpose}') is missing a 'rules' array");
                    else ruleCount += rs.GetArrayLength();
                }
            }
            else if (hasParticipants)
            {
                if (parts.ValueKind != JsonValueKind.Array) errors.Add("'participants' must be an array");
                if (!(root.TryGetProperty("rules", out var rs) || root.TryGetProperty("Rules", out rs)) || rs.ValueKind != JsonValueKind.Array)
                    errors.Add("a UML participants file needs a top-level 'rules' array");
                else ruleCount = rs.GetArrayLength();
            }
            else
            {
                errors.Add("rule file must have either a 'sections' array (filter/enrichment/output rules) or 'participants' + 'rules' (a UML interaction file)");
            }

            var summary = hasParticipants && !hasSections
                ? (object)new { kind = "uml-participants", rules = ruleCount }
                : new { kind = "sections", sections = sectionCount, rules = ruleCount, purposes = purposes.Distinct().ToArray() };
            return (errors.Count == 0, errors, warnings, summary);
        }
    }

    /// <summary>Validate rule JSON without writing it. Returns structured pass/fail + errors + a summary.</summary>
    public Task<object> ValidateRuleAsync(string json) => Task.FromResult<object>(BuildValidationResult(json));

    private static object BuildValidationResult(string json)
    {
        var (valid, errors, warnings, summary) = ValidateRuleStructured(json);
        return new { valid, errors, warnings, summary };
    }

    /// <summary>Validate then write a rule file into the AI sandbox. On invalid JSON nothing is written
    /// and the errors are returned so the agent can self-correct.</summary>
    public Task<object> SaveRuleAsync(string name, string json) => Task.FromResult<object>(DoSaveRule(name, json));

    private static object DoSaveRule(string name, string json)
    {
        var fileName = SafeRuleFileName(name);
        var (valid, errors, warnings, summary) = ValidateRuleStructured(json);
        if (!valid)
            return new { saved = false, name = fileName, errors, warnings, summary };

        Directory.CreateDirectory(AiRulesDir);
        var path = Path.Combine(AiRulesDir, fileName);
        File.WriteAllText(path, json);
        // Tip the caller toward the next step (point the search at it, then re-run).
        return new { saved = true, name = fileName, path, warnings, summary,
                     next = "Call set_rules with this name, then run_search(ignoreCache:true) to apply it." };
    }

    /// <summary>List rule files the agent can use: AI-authored (read/write/delete) and shipped CommonRules
    /// (read-only reference).</summary>
    public Task<object> ListRuleFilesAsync() => Task.FromResult<object>(DoListRuleFiles());

    private static object DoListRuleFiles()
    {
        var list = new List<object>();
        void Add(string dir, string source, bool editable)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.EnumerateFiles(dir, "*.rules.json").OrderBy(x => x))
            {
                long bytes = 0; try { bytes = new FileInfo(f).Length; } catch { }
                list.Add(new { name = Path.GetFileName(f), source, editable, bytes, path = f });
            }
        }
        Add(AiRulesDir, "ai", true);
        Add(CommonRulesDir, "common", false);
        return new { files = list };
    }

    /// <summary>Read a rule file's JSON (AI sandbox first, then CommonRules). Throws if not found.</summary>
    public Task<object> ReadRuleAsync(string name) => Task.FromResult<object>(DoReadRule(name));

    private static object DoReadRule(string name)
    {
        var leaf = Path.GetFileName((name ?? "").Trim());
        if (string.IsNullOrEmpty(leaf)) throw new ArgumentException("rule name is required");
        var ai = Path.Combine(AiRulesDir, leaf);
        var common = Path.Combine(CommonRulesDir, leaf);
        var path = File.Exists(ai) ? ai : File.Exists(common) ? common : null;
        if (path == null) throw new FileNotFoundException($"no rule file named '{leaf}' in the AI sandbox or CommonRules");
        return new { name = leaf, source = path == ai ? "ai" : "common", editable = path == ai, content = File.ReadAllText(path), path };
    }

    /// <summary>Delete an AI-authored rule file. Refuses to touch shipped CommonRules.</summary>
    public Task<object> DeleteRuleAsync(string name) => Task.FromResult<object>(DoDeleteRule(name));

    private static object DoDeleteRule(string name)
    {
        var leaf = Path.GetFileName((name ?? "").Trim());
        if (string.IsNullOrEmpty(leaf)) throw new ArgumentException("rule name is required");
        var path = Path.Combine(AiRulesDir, leaf);
        if (!File.Exists(path)) throw new FileNotFoundException($"no AI-authored rule named '{leaf}' (CommonRules files can't be deleted)");
        File.Delete(path);
        return new { deleted = true, name = leaf };
    }

    public Task AddKustoAsync(string cluster, string database, string kql, string authMode, long rowLimit)
        => RunOnUiAsync(() =>
    {
        if (string.IsNullOrWhiteSpace(cluster))  throw new ArgumentException("cluster is required");
        if (string.IsNullOrWhiteSpace(database)) throw new ArgumentException("database is required");
        if (string.IsNullOrWhiteSpace(kql))      throw new ArgumentException("query (kql) is required");
        var mode = Enum.TryParse<KustoPlugin.Location.KustoAuthMode>(authMode, ignoreCase: true, out var m)
            ? m : KustoPlugin.Location.KustoAuthMode.Interactive;
        MiddleLayerService.AddLocation(new KustoPlugin.Location.KustoLocation(cluster, database, kql, mode, rowLimit));
    });

    public Task ClearWorkspaceAsync() => RunOnUiAsync(() => MiddleLayerService.ClearWorkspace());

    public Task<object> SaveWorkspaceAsync(string path) => RunOnUiAsync<object>(() =>
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required");
        MiddleLayerService.SaveWorkspace(path);
        return new { ok = true, path };
    });

    public Task<object> OpenWorkspaceAsync(string path) => RunOnUiAsync<object>(() =>
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required");
        MiddleLayerService.OpenWorkspace(path);
        return new { ok = true, path, locations = MiddleLayerService.Locations.Count };
    });

    // ----- viewer settings (whitelisted; no blind reflection) -----
    private static readonly string[] SettingKeys =
    {
        "ThemeName", "PageSize", "TimeFormat", "EnrichmentEnabled", "StreamWhileLoading",
        "TitleBarColorMode", "TitleBarCustomColor", "DefaultSort", "FilterDock",
    };

    public Task<object> GetSettingAsync(string key) => RunOnUiAsync<object>(() =>
    {
        string val = key switch
        {
            "ThemeName" => ResultsViewerSettings.ThemeName,
            "PageSize" => ResultsViewerSettings.PageSize.ToString(),
            "TimeFormat" => ResultsViewerSettings.TimeFormat,
            "EnrichmentEnabled" => ResultsViewerSettings.EnrichmentEnabled.ToString(),
            "StreamWhileLoading" => ResultsViewerSettings.StreamWhileLoading.ToString(),
            "TitleBarColorMode" => ResultsViewerSettings.TitleBarColorMode,
            "TitleBarCustomColor" => ResultsViewerSettings.TitleBarCustomColor,
            "DefaultSort" => ResultsViewerSettings.DefaultSort.ToString(),
            "FilterDock" => ResultsViewerSettings.FilterDock.ToString(),
            _ => throw new ArgumentException($"unknown setting '{key}'. Known: {string.Join(", ", SettingKeys)}"),
        };
        return new { key, value = val };
    });

    public Task<object> SetSettingAsync(string key, string value) => RunOnUiAsync<object>(() =>
    {
        bool ParseBool() => bool.TryParse(value, out var b) ? b
            : throw new ArgumentException($"{key} expects true/false, got '{value}'");
        int ParseInt() => int.TryParse(value, out var n) ? n
            : throw new ArgumentException($"{key} expects an integer, got '{value}'");
        switch (key)
        {
            case "ThemeName": ResultsViewerSettings.ThemeName = value; break;
            case "PageSize": ResultsViewerSettings.PageSize = ParseInt(); break;
            case "TimeFormat": ResultsViewerSettings.TimeFormat = value; break;
            case "EnrichmentEnabled": ResultsViewerSettings.EnrichmentEnabled = ParseBool(); break;
            case "StreamWhileLoading": ResultsViewerSettings.StreamWhileLoading = ParseBool(); break;
            case "TitleBarColorMode": ResultsViewerSettings.TitleBarColorMode = value; break;
            case "TitleBarCustomColor": ResultsViewerSettings.TitleBarCustomColor = value; break;
            case "DefaultSort":
                ResultsViewerSettings.DefaultSort = Enum.Parse<DefaultSortMode>(value, ignoreCase: true); break;
            case "FilterDock":
                ResultsViewerSettings.FilterDock = Enum.Parse<FilterDock>(value, ignoreCase: true); break;
            default: throw new ArgumentException($"unknown setting '{key}'. Known: {string.Join(", ", SettingKeys)}");
        }
        return new { key, value };
    });

    public Task<bool> RemoveLocationAsync(string name) => RunOnUiAsync(() =>
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        int before = MiddleLayerService.Locations.Count;
        MiddleLayerService.RemoveLocationByName(name);
        return MiddleLayerService.Locations.Count < before;
    });

    public Task RunSearchAsync() => RunSearchAsync(false, null);

    /// <param name="ignoreCache">Force a fresh scan: set the cache-mode override to Never for this
    /// run (captured/restored on the UI thread) so a warm cache is rescanned and no reuse prompt
    /// appears. The search consumes the override synchronously at kickoff, so restoring afterward is
    /// safe.</param>
    /// <param name="enrich">Optional per-run enrichment override (null = use the persisted setting):
    /// true forces RuleDSL "extract" enrichment on for this run, false forces it off. Captured/restored
    /// on the UI thread like the cache override.</param>
    public async Task RunSearchAsync(bool ignoreCache, bool? enrich = null)
    {
        FindPluginCore.Searching.CacheReuseMode? prevOverride = null;
        bool? prevEnrich = null;
        if (ignoreCache || enrich.HasValue)
            (prevOverride, prevEnrich) = await RunOnUiAsync(() =>
            {
                var pc = MiddleLayerService.CacheModeOverride;
                var pe = MiddleLayerService.EnrichmentOverride;
                if (ignoreCache) MiddleLayerService.CacheModeOverride = FindPluginCore.Searching.CacheReuseMode.Never;
                if (enrich.HasValue) MiddleLayerService.EnrichmentOverride = enrich.Value;
                return (pc, pe);
            }).ConfigureAwait(false);
        try
        {
            var handler = RunSearchHandler;
            if (handler != null) { await handler().ConfigureAwait(false); }
            else
            {
                // Fallback: trigger a streaming search directly (no navigation). The active viewer, if
                // any, picks up the new results on its next load.
                await RunOnUiAsync(() => { MiddleLayerService.RunSearchStreaming(); }).ConfigureAwait(false);
            }
            // Rebind the (already-open) viewer to this search's new storage — without it, a viewer left
            // open from a previous run keeps reading the now-disposed store and throws "connection open".
            // If none is open yet, wait briefly for one to register (then it loads the new search itself).
            if (HasViewer) await ReloadViewerAsync().ConfigureAwait(false);
            else if (await WaitForViewerAsync(8_000).ConfigureAwait(false)) await ReloadViewerAsync().ConfigureAwait(false);
        }
        finally
        {
            if (ignoreCache || enrich.HasValue)
                await RunOnUiAsync(() =>
                {
                    if (ignoreCache) MiddleLayerService.CacheModeOverride = prevOverride;
                    if (enrich.HasValue) MiddleLayerService.EnrichmentOverride = prevEnrich;
                }).ConfigureAwait(false);
        }
    }

    public Task CancelSearchAsync() => RunOnUiAsync(() => MiddleLayerService.CurrentStreamingSearch?.Stop());

    /// <summary>
    /// Navigate the app to the native results page so a viewer registers (making viewer tools usable)
    /// and the current search is displayed. The navigation marshals to the UI thread itself. Returns
    /// whether a viewer became ready within <paramref name="timeoutMs"/>.
    /// </summary>
    public async Task<bool> OpenResultsViewerAsync(int timeoutMs)
    {
        // Already open: rebind it to the current search (don't just early-return, or it keeps showing
        // the previous run / reads a disposed store).
        if (HasViewer) { await ReloadViewerAsync().ConfigureAwait(false); return true; }
        await RunOnUiAsync(() => MainWindowActions.NavigateToNativeResultsPage()).ConfigureAwait(false);
        return await WaitForViewerAsync(timeoutMs).ConfigureAwait(false);
    }

    /// <summary>
    /// Run the (deferred) RuleDSL output rules / outputs now — e.g. generate the UML diagram — and
    /// return the produced files plus per-diagram generation stats (timing, rows in/matched,
    /// participants, interactions, size). Runs off the UI thread (the generation scans results). The
    /// total wall time is measured here; per-diagram timing comes from the generator itself.
    /// </summary>
    public Task<object> GenerateOutputsAsync() => Task.Run<object>(() =>
    {
        if (MiddleLayerService.SearchQueryUX?.CurrentQuery is not FindPluginCore.Searching.NuSearchQuery nu)
            return new { ok = false, reason = "no active search" };
        if (!nu.HasOutputRules)
            return new { ok = false, reason = "no output rules to generate (enable a UML/output rule first)" };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var files = MiddleLayerService.GenerateRuleOutputs();
        sw.Stop();

        var diagrams = (nu.GeneratedDiagramUsages ?? new List<FindNeedleRuleDSL.UmlDiagramUsage>())
            .Select(u => new
            {
                file = u.FilePath,
                title = u.Title,
                generationMs = u.GenerationMs,
                sourceRows = u.SourceRowCount,
                matchedRows = u.MatchedRowCount,
                participants = u.ParticipantCount,
                interactions = u.InteractionCount,
                lines = u.DiagramLineCount,
                chars = u.DiagramCharCount,
                rulesFired = u.Rules.Count(r => r.Count > 0),
                rulesTotal = u.Rules.Count,
            })
            .ToList();

        return new { ok = true, totalMs = sw.ElapsedMilliseconds, fileCount = files.Count, files, diagrams };
    });

    // ----- UI-thread marshaling -----

    internal Task RunOnUiAsync(Action action)
    {
        var dq = UiDispatcher;
        if (dq == null || dq.HasThreadAccess) { action(); return Task.CompletedTask; }
        var tcs = new TaskCompletionSource();
        if (!dq.TryEnqueue(() => { try { action(); tcs.TrySetResult(); } catch (Exception ex) { tcs.TrySetException(ex); } }))
            action();
        return tcs.Task;
    }

    internal Task<T> RunOnUiAsync<T>(Func<T> func)
    {
        var dq = UiDispatcher;
        if (dq == null || dq.HasThreadAccess) return Task.FromResult(func());
        var tcs = new TaskCompletionSource<T>();
        if (!dq.TryEnqueue(() => { try { tcs.TrySetResult(func()); } catch (Exception ex) { tcs.TrySetException(ex); } }))
            return Task.FromResult(func());
        return tcs.Task;
    }
}

/// <summary>Thrown by viewer tools when no result viewer is registered with the bridge.</summary>
public sealed class McpNoViewerException : Exception
{
    public McpNoViewerException(string message) : base(message) { }
}
