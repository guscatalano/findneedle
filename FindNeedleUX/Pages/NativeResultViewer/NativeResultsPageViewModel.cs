using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using FindNeedleRuleDSL;
using FindNeedleUX;
using FindNeedleUX.Services;
using FindNeedleUX.Services.PagedLogSource;
using FindPluginCore.Diagnostics;

namespace FindNeedleUX.Pages.NativeResultViewer;

/// <summary>
/// ViewModel for the native WinUI 3 result viewer.
///
/// Backed by an <see cref="IPagedLogSource"/> so the underlying data set can live in memory,
/// in SQLite, or in a hybrid store — only the current page (~PageSize rows) is ever materialized
/// in <see cref="Results"/>. Filter / sort / level-counts all flow through the source.
/// </summary>
public class NativeResultsPageViewModel : INotifyPropertyChanged
{
    private IPagedLogSource _source = new InMemoryPagedSource(Array.Empty<LogLine>());

    // Captured on construction (the UI thread). Used to marshal the async-search result-apply back
    // onto the UI thread deterministically — mutating Results off the UI thread crashes the DataGrid,
    // e.g. when an async search's continuation overlaps a (UI-thread) page change.
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _uiDispatcher = TryGetDispatcher();

    private static Microsoft.UI.Dispatching.DispatcherQueue TryGetDispatcher()
    {
        // Throws (COMException) off a WinUI thread, e.g. in unit tests — fall back to null so
        // RunOnUiAsync applies inline there.
        try { return Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread(); }
        catch { return null; }
    }

    /// <summary>Test seam: inject a paged source directly (no MiddleLayerService/search needed).</summary>
    internal void SetSourceForTests(IPagedLogSource source) => _source = source;

    // ----- Rule view filter (opt-in): hide rows the active RuleDSL filter rules exclude -----
    // The native viewer normally shows the raw scanned rows; RuleDSL filter rules only gate the
    // pipeline's consolidated list. This toggle builds a one-off SQLite store containing just the rows
    // that pass the rules and points the viewer at it (so paging/counts/search all stay correct), and
    // reverts to the original storage when turned off.
    private FindPluginCore.Implementations.Storage.SqliteStorage _ruleFilteredStorage;
    private readonly List<(Regex match, Regex unmatch)> _excludeRules = new();
    private readonly List<(Regex match, Regex unmatch)> _includeRules = new();
    private readonly List<(Regex match, string replacement)> _redactRules = new();

    /// <summary>True while the viewer is showing the rule-filtered subset.</summary>
    public bool RuleFilterActive { get; private set; }

    /// <summary>
    /// Turn the rule view filter on/off. When on, builds a filtered store from the current results and
    /// swaps the viewer onto it; returns the kept row count, or -1 if the active rules contain no
    /// filter (exclude/include) rules to apply. Must be called on the UI thread.
    /// </summary>
    public async Task<int> SetRuleViewFilterAsync(bool on, IReadOnlyList<string> ruleFiles)
    {
        if (!on)
        {
            // Revert by re-running the normal load: it disposes the filtered store (via
            // ResetRuleViewFilter), rebuilds the source over the real search storage with the correct
            // in-memory/SQLite fallback, and re-derives levels — all the things a manual swap would miss.
            _currentPage = 1;
            await LoadResultsAsync();
            return -1;
        }

        CompileRuleFilters(ruleFiles);
        if (_excludeRules.Count == 0 && _includeRules.Count == 0 && _redactRules.Count == 0) return -1;

        bool redacting = _redactRules.Count > 0;
        var orig = MiddleLayerService.GetSearchStorage();
        var built = await Task.Run(() =>
        {
            var tmp = Path.Combine(Path.GetTempPath(), $"FN_ruleview_{Guid.NewGuid():N}.log");
            var store = new FindPluginCore.Implementations.Storage.SqliteStorage(tmp);
            int kept = 0;
            orig.GetFilteredResultsInBatches(batch =>
            {
                var keep = batch.Where(r => RulePass(r.GetSearchableData())).ToList();
                // Redact (mask) PII in-place on the kept rows before they're stored, so the masked
                // text is what the viewer shows, searches, and exports.
                if (redacting)
                    for (int i = 0; i < keep.Count; i++) keep[i] = new RedactingSearchResult(keep[i], _redactRules);
                if (keep.Count > 0) { store.AddFilteredBatch(keep); kept += keep.Count; }
            });
            return (store, kept);
        });

        try { _source?.Dispose(); } catch { /* ignore */ }
        _ruleFilteredStorage = built.store;
        _source = PagedLogSourceFactory.Create(built.store, Array.Empty<LogLine>());
        RuleFilterActive = true;
        _currentPage = 1;
        await AfterSourceSwapAsync();
        return built.kept;
    }

    private async Task AfterSourceSwapAsync()
    {
        TotalCount = _source.TotalCount;
        await ReloadFromSourceAsyncCore();
    }

    /// <summary>Drop any built rule-filtered store (called when a fresh search/load replaces results).</summary>
    private void ResetRuleViewFilter()
    {
        if (_ruleFilteredStorage != null)
        {
            try { _ruleFilteredStorage.Dispose(); } catch { /* ignore */ }
            _ruleFilteredStorage = null;
        }
        RuleFilterActive = false;
    }

    internal void CompileRuleFilters(IEnumerable<string> ruleFiles)
    {
        _excludeRules.Clear();
        _includeRules.Clear();
        _redactRules.Clear();
        var opts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        foreach (var file in ruleFiles ?? Enumerable.Empty<string>())
        {
            try
            {
                if (string.IsNullOrEmpty(file) || !File.Exists(file)) continue;
                var set = System.Text.Json.JsonSerializer.Deserialize<UnifiedRuleSet>(File.ReadAllText(file), opts);
                if (set?.Sections == null) continue;
                foreach (var section in set.Sections)
                    foreach (var rule in section.Rules ?? new())
                    {
                        if (!rule.Enabled || string.IsNullOrEmpty(rule.Match)) continue;
                        var type = rule.Action?.Type?.ToLowerInvariant();

                        if (type == "redact")
                        {
                            Regex rm;
                            try { rm = CompileRule(rule.Match); } catch { continue; }
                            var replacement = string.IsNullOrEmpty(rule.Action?.Replacement) ? "[REDACTED]" : rule.Action.Replacement;
                            _redactRules.Add((rm, replacement));
                            continue;
                        }

                        if (type != "exclude" && type != "include") continue;

                        Regex m;
                        try { m = CompileRule(rule.Match); } catch { continue; }
                        Regex u = null;
                        if (!string.IsNullOrEmpty(rule.Unmatch))
                            try { u = CompileRule(rule.Unmatch); } catch { u = null; }

                        (type == "exclude" ? _excludeRules : _includeRules).Add((m, u));
                    }
            }
            catch { /* skip an unparseable rule file */ }
        }
    }

    private static Regex CompileRule(string pattern) =>
        new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>Blacklist-then-whitelist: a row is hidden if it matches any exclude rule, or (when
    /// include rules exist) if it matches none of them.</summary>
    internal bool RulePass(string data)
    {
        data ??= "";
        foreach (var (m, u) in _excludeRules)
            if (RuleHit(m, u, data)) return false;

        if (_includeRules.Count > 0)
        {
            bool any = false;
            foreach (var (m, u) in _includeRules)
                if (RuleHit(m, u, data)) { any = true; break; }
            if (!any) return false;
        }
        return true;
    }

    private static bool RuleHit(Regex match, Regex unmatch, string data)
    {
        try { if (match != null && !match.IsMatch(data)) return false; }
        catch (RegexMatchTimeoutException) { return false; }
        if (unmatch != null)
        {
            try { if (unmatch.IsMatch(data)) return false; }
            catch (RegexMatchTimeoutException) { /* treat as not-unmatched */ }
        }
        return true;
    }

    public RangeObservableCollection<LogLine> Results { get; } = new();

    // ----- filter state -----
    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;
            _searchText = value ?? "";
            ReparseSearch();
            OnPropertyChanged(nameof(SearchText));
            ApplyFilters();
        }
    }

    // The search box auto-detects a structured query (has a field operator / AND / OR) vs a plain
    // substring. A valid query → _parsedQuery (applied across both backends) with _effectiveSearch ""
    // so the substring path doesn't double-apply. A query that looks structured but won't parse →
    // SearchQueryError set, no filtering. Plain text → substring search as before.
    private FindPluginCore.Searching.Query.QueryNode _parsedQuery;
    private string _effectiveSearch = "";
    private string _searchQueryError = "";
    /// <summary>Parse error for a malformed structured query (empty when fine). Bound to a UI hint.</summary>
    public string SearchQueryError { get => _searchQueryError; private set => Set(ref _searchQueryError, value); }
    /// <summary>True while the search box holds a valid structured query (vs a plain substring).</summary>
    public bool SearchIsQuery => _parsedQuery != null;

    private void ReparseSearch()
    {
        var text = _searchText ?? "";
        if (FindPluginCore.Searching.Query.LogQuery.LooksStructured(text))
        {
            if (FindPluginCore.Searching.Query.LogQuery.TryParse(text, out var node, out var err))
            { _parsedQuery = node; _effectiveSearch = ""; SearchQueryError = ""; }
            else
            { _parsedQuery = null; _effectiveSearch = ""; SearchQueryError = err; }
        }
        else
        { _parsedQuery = null; _effectiveSearch = text; SearchQueryError = ""; }
    }

    private string _providerFilter = "";
    public string ProviderFilter { get => _providerFilter; set => Set(ref _providerFilter, value, applyFilters: true); }

    private string _taskNameFilter = "";
    public string TaskNameFilter { get => _taskNameFilter; set => Set(ref _taskNameFilter, value, applyFilters: true); }

    private string _messageFilter = "";
    public string MessageFilter { get => _messageFilter; set => Set(ref _messageFilter, value, applyFilters: true); }

    private string _sourceFilter = "";
    public string SourceFilter { get => _sourceFilter; set => Set(ref _sourceFilter, value, applyFilters: true); }

    // Multi-select OR-sets (null = unused; when set, takes precedence over the substring field above).
    private IReadOnlyList<string> _providerFilterSet;
    private IReadOnlyList<string> _taskNameFilterSet;
    private IReadOnlyList<string> _sourceFilterSet;
    public IReadOnlyList<string> ProviderFilterSet => _providerFilterSet;
    public IReadOnlyList<string> TaskNameFilterSet => _taskNameFilterSet;
    public IReadOnlyList<string> SourceFilterSet => _sourceFilterSet;

    /// <summary>Set the multi-select OR-set for one of Provider / TaskName / Source and re-filter.
    /// An empty/null list clears it (the column falls back to its substring filter).</summary>
    public void SetKnownFilterSet(string field, IReadOnlyList<string> values)
    {
        var v = (values != null && values.Count > 0) ? values : null;
        switch (field)
        {
            case "Provider": _providerFilterSet = v; break;
            case "TaskName": _taskNameFilterSet = v; break;
            case "Source":   _sourceFilterSet = v; break;
            default: return;
        }
        _currentPage = 1;
        ReloadFromSource();
    }

    private string _levelFilter = "";
    public string LevelFilter { get => _levelFilter; set => Set(ref _levelFilter, value, applyFilters: true); }

    private DateTime? _fromDate;
    public DateTime? FromDate
    {
        get => _fromDate;
        set { Set(ref _fromDate, value, applyFilters: true); MiddleLayerService.OutputTimeFrom = _fromDate; }
    }

    private DateTime? _toDate;
    public DateTime? ToDate
    {
        get => _toDate;
        set { Set(ref _toDate, value, applyFilters: true); MiddleLayerService.OutputTimeTo = _toDate; }
    }

    // ----- pagination -----
    private int _pageSize = 100;
    public int PageSize
    {
        get => _pageSize;
        set { if (_pageSize != value && value > 0) { _pageSize = value; PageOrSizeChanged(); } }
    }

    private int _currentPage = 1;
    public int CurrentPage
    {
        get => _currentPage;
        set
        {
            var clamped = Math.Max(1, Math.Min(value, TotalPages == 0 ? 1 : TotalPages));
            if (_currentPage != clamped) { _currentPage = clamped; PageOrSizeChanged(); }
        }
    }

    public int TotalFilteredCount { get => _totalFilteredCount; private set => Set(ref _totalFilteredCount, value); }
    private int _totalFilteredCount;

    public int TotalPages
    {
        get
        {
            if (_pageSize <= 0 || _totalFilteredCount == 0) return 0;
            return (_totalFilteredCount + _pageSize - 1) / _pageSize;
        }
    }

    public string PageRangeText
    {
        get
        {
            if (_totalFilteredCount == 0) return "0";
            int start = (_currentPage - 1) * _pageSize + 1;
            int end = Math.Min(_currentPage * _pageSize, _totalFilteredCount);
            return $"{start:N0}–{end:N0}";
        }
    }

    private void PageOrSizeChanged()
    {
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(PageSize));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(PageRangeText));
        PublishCurrentPage();
        UpdateStatus();
    }

    public void GoToPage(int page) => CurrentPage = page;
    public void FirstPage() => CurrentPage = 1;
    public void PrevPage() => CurrentPage = _currentPage - 1;
    public void NextPage() => CurrentPage = _currentPage + 1;
    public void LastPage() => CurrentPage = TotalPages == 0 ? 1 : TotalPages;

    // ----- sort state -----
    private string _sortColumn;
    private bool _sortDescending;
    public string SortColumn => _sortColumn;
    public bool SortDescending => _sortDescending;

    public void ApplySort(string column, bool descending)
    {
        _sortColumn = string.IsNullOrEmpty(column) ? null : column;
        _sortDescending = descending;
        _currentPage = 1;
        ReloadFromSource();
    }

    /// <summary>
    /// Seed the initial sort from the persisted <see cref="ResultsViewerSettings.DefaultSort"/> setting.
    /// Sets the sort state WITHOUT reloading (a fresh LoadResultsAsync queries with it anyway). Called at
    /// the start of each fresh load, so opening a new log honors the preference; a manual header click
    /// after that overrides until the next open.
    /// </summary>
    public void ApplyDefaultSortFromSettings()
    {
        switch (ResultsViewerSettings.DefaultSort)
        {
            case DefaultSortMode.TimeAscending:
                _sortColumn = "Time"; _sortDescending = false; break;
            case DefaultSortMode.TimeDescending:
                _sortColumn = "Time"; _sortDescending = true; break;
            default: // LoadOrder
                _sortColumn = null; _sortDescending = false; break;
        }
    }

    // ----- status state -----
    private int _totalCount;
    public int TotalCount { get => _totalCount; set { if (Set(ref _totalCount, value)) UpdateStatus(); } }

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => Set(ref _isLoading, value); }

    // True while a streaming search is still producing rows into our backing store. Bound to the
    // Stop button's visibility — once the producer signals completion, the button disappears.
    private bool _isStreaming;
    public bool IsStreaming { get => _isStreaming; set => Set(ref _isStreaming, value); }

    private string _statusText = "0 / 0 results";
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    // Timing breakdown of the most recent filter/query (count + level counts + page fetch).
    private string _lastFilterSummary = "⏱";
    public string LastFilterSummary { get => _lastFilterSummary; set => Set(ref _lastFilterSummary, value); }
    private string _lastFilterBreakdown = "Apply a filter or search to see timing.";
    public string LastFilterBreakdown { get => _lastFilterBreakdown; set => Set(ref _lastFilterBreakdown, value); }

    // Prominent "still loading" banner shown while a streaming search produces rows.
    private string _streamingBannerText = "Loading logs…";
    public string StreamingBannerText { get => _streamingBannerText; set => Set(ref _streamingBannerText, value); }

    private string ComposeStreamBanner() =>
        $"Loading logs — {_source?.TotalCount ?? TotalCount:N0} rows so far and rising. You can search and scroll now; results keep filling in.";

    // True while the substring-search (FTS) index is being built (lazy/background modes). Bound to a
    // toolbar indicator + Cancel button. Substring search uses the slower scan until it clears.
    private bool _isIndexing;
    public bool IsIndexing { get => _isIndexing; set => Set(ref _isIndexing, value); }

    private string _indexStatusText = "";
    public string IndexStatusText { get => _indexStatusText; set => Set(ref _indexStatusText, value); }

    // ----- per-level + per-column metadata -----
    public ObservableCollection<LevelEntry> Levels { get; } = new();
    public ObservableCollection<string> KnownLevelNames { get; } = new();
    public ObservableCollection<ColumnEntry> Columns { get; } = new();

    public IAsyncRelayCommand LoadResultsCommand { get; }

    public NativeResultsPageViewModel()
    {
        LoadResultsCommand = new AsyncRelayCommand(LoadResultsAsync);
        foreach (var name in DefaultColumnNames)
            Columns.Add(new ColumnEntry { Name = name, IsVisible = true });
    }

    public static readonly IReadOnlyList<string> DefaultColumnNames = new[]
    {
        "Index", "Time", "Provider", "TaskName", "Message", "Source", "Level",
        "ProcessId", "ProcessName", "ThreadId", "ActivityId",
        "EventId", "OpCode", "Keywords", "RelatedActivityId", "Channel", "ProviderGuid", "RecordId",
        "Raw Row"
    };

    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ThemePresets =
        new Dictionary<string, IReadOnlyDictionary<string, string>>
    {
        // Keyed by the real Level enum names (Catastrophic / Error / Warning / Info / Verbose / Unknown).
        // Info and Unknown are untinted (Transparent) so only the actionable severities stand out.
        ["Subtle"] = new Dictionary<string, string>
        {
            { "Catastrophic", "#33D32F2F" }, { "Error",   "#1FEF5350" },
            { "Warning",      "#22FFA000" }, { "Info",    "Transparent" },
            { "Verbose",      "#12808080" }, { "Unknown", "Transparent" }
        },
        ["Vivid"] = new Dictionary<string, string>
        {
            { "Catastrophic", "#FFB3B3" }, { "Error",   "#FFE1E1" },
            { "Warning",      "#FFF4CC" }, { "Info",    "Transparent" },
            { "Verbose",      "#F1F1F1" }, { "Unknown", "Transparent" }
        },
        // The familiar log/console look: red errors, yellow warnings, gray for trace levels.
        ["Classic"] = new Dictionary<string, string>
        {
            { "Catastrophic", "#66B71C1C" }, { "Error",   "#44F44336" },
            { "Warning",      "#44FFEB3B" }, { "Info",    "Transparent" },
            { "Verbose",      "#1F9E9E9E" }, { "Unknown", "Transparent" }
        },
        // Stronger translucent semantic tints (red/amber) — same meaning as Subtle, more presence.
        ["Bold"] = new Dictionary<string, string>
        {
            { "Catastrophic", "#59D50000" }, { "Error",   "#33F44336" },
            { "Warning",      "#3DFF9800" }, { "Info",    "Transparent" },
            { "Verbose",      "#1F607D8B" }, { "Unknown", "Transparent" }
        },
        // Cool blue/teal palette (aesthetic, translucent — works on light or dark).
        ["Ocean"] = new Dictionary<string, string>
        {
            { "Catastrophic", "#4601579B" }, { "Error",   "#300288D1" },
            { "Warning",      "#2A26A69A" }, { "Info",    "Transparent" },
            { "Verbose",      "#1F4DD0E1" }, { "Unknown", "Transparent" }
        },
        // Green / earth palette.
        ["Forest"] = new Dictionary<string, string>
        {
            { "Catastrophic", "#461B5E20" }, { "Error",   "#30388E3C" },
            { "Warning",      "#2A9E9D24" }, { "Info",    "Transparent" },
            { "Verbose",      "#1F689F38" }, { "Unknown", "Transparent" }
        },
        // Warm purple → orange palette.
        ["Sunset"] = new Dictionary<string, string>
        {
            { "Catastrophic", "#464A148C" }, { "Error",   "#30AD1457" },
            { "Warning",      "#2AEF6C00" }, { "Info",    "Transparent" },
            { "Verbose",      "#1FF06292" }, { "Unknown", "Transparent" }
        },
        // Neutral grayscale intensity ramp.
        ["Grayscale"] = new Dictionary<string, string>
        {
            { "Catastrophic", "#44424242" }, { "Error",   "#2C757575" },
            { "Warning",      "#249E9E9E" }, { "Info",    "Transparent" },
            { "Verbose",      "#16BDBDBD" }, { "Unknown", "Transparent" }
        },
        ["None"] = new Dictionary<string, string>
        {
            { "Catastrophic", "Transparent" }, { "Error",   "Transparent" },
            { "Warning",      "Transparent" }, { "Info",    "Transparent" },
            { "Verbose",      "Transparent" }, { "Unknown", "Transparent" }
        }
    };

    public const string DefaultThemeName = "Subtle";

    public static IReadOnlyDictionary<string, string> DefaultLevelColors => ThemePresets[DefaultThemeName];

    public void ApplyTheme(string themeName)
    {
        if (string.IsNullOrEmpty(themeName) || !ThemePresets.TryGetValue(themeName, out var theme)) return;
        foreach (var entry in Levels)
            entry.HexColor = theme.TryGetValue(entry.Level, out var hex) ? hex : "Transparent";
    }

    // ----- Loading -----
    public async Task LoadResultsAsync()
    {
        IsLoading = true;
        bool streaming = false;
        // A fresh search/load supersedes any rule-filtered view from the previous result set.
        ResetRuleViewFilter();
        // Seed the initial sort from the user's preference (load order by default) before the first
        // page is queried, so the log opens already sorted the way they asked.
        ApplyDefaultSortFromSettings();
        // Capture the UI dispatcher + create the live-refresh timer here, on the UI thread. The
        // streaming RowsAvailable callback runs on the producer (background) thread where
        // DispatcherQueue.GetForCurrentThread() is null — so without this the count would freeze at
        // the first page.
        EnsureRefreshTimer();
        using var perfScope = PerfLog.Scope("viewer.native.load");
        try
        {
            // Prefer the live source from a streaming search if one is in flight. Otherwise build
            // a fresh paged source over whatever storage the last search produced. The streaming
            // source is owned by MiddleLayerService — we attach/detach but don't dispose it.
            var sh = MiddleLayerService.CurrentStreamingSearch;
            if (sh?.Source is { IsLoading: true } liveSource)
            {
                try { if (_source != liveSource) _source?.Dispose(); } catch { /* ignore */ }
                _source = liveSource;
                streaming = true;

                // Detach any prior subscription before attaching, in case the same VM is reused.
                liveSource.RowsAvailable -= OnStreamingRowsAvailable;
                liveSource.RowsAvailable += OnStreamingRowsAvailable;
                PerfLog.Log("viewer.native.source", ("kind", "streaming"));
            }
            else
            {
                var storage = MiddleLayerService.GetSearchStorage();
                try { _source?.Dispose(); } catch { /* ignore */ }

                // SqliteStorage / HybridStorage paged sources don't read the fallback list — they
                // go straight to SQLite. Skip materializing all rows; for a 500k-row search,
                // GetLogLines() allocates half a million LogLine objects + corresponding string
                // copies on the UI thread (~hundreds of ms). For the InMemoryStorage / null path
                // we still need it, but we push the work to the threadpool.
                if (storage is FindPluginCore.Implementations.Storage.SqliteStorage
                    || storage is FindPluginCore.Implementations.Storage.HybridStorage)
                {
                    using (PerfLog.Scope("viewer.native.source_build", ("storage", storage.GetType().Name)))
                        _source = PagedLogSourceFactory.Create(storage, Array.Empty<LogLine>());
                }
                else
                {
                    using (PerfLog.Scope("viewer.native.getloglines_fallback"))
                    {
                        var fallback = await Task.Run(
                            () => (IList<LogLine>)(MiddleLayerService.GetLogLines() ?? new List<LogLine>()));
                        _source = PagedLogSourceFactory.Create(storage, fallback);
                    }
                }
            }

            // Discover levels (+ a small sample for AutoHide) OFF the UI thread. On a multi-million-row
            // SQLite table GetDistinctLevels is a full scan; running it (and the first-page reload
            // below) on the UI thread froze the viewer for several seconds on navigation-back — the
            // loading spinner's gif couldn't even animate. Query on the threadpool, publish on the UI.
            List<string> distinct;
            List<LogLine> sample = null;
            using (PerfLog.Scope("viewer.native.levels"))
            {
                var src = _source;
                bool needSample = !streaming;
                var r = await Task.Run(() =>
                {
                    var d = src.GetDistinctLevels();
                    var s = needSample ? src.GetPage(FilterSpec.Empty, SortSpec.None, 0, 1000) : null;
                    return (d, s);
                });
                distinct = r.d;
                sample = r.s;

                // Show only the levels actually present in the data — NOT every enum value. Force-adding
                // all of them surfaced "Catastrophic" (ETW Critical, usually absent) and "Unknown" (the
                // can't-determine-level sentinel) as 0-count chips, which reads as "these aren't real
                // levels." Keep only valid enum names (a stale/garbage name would match nothing and, via
                // a failed Enum.TryParse, silently drop the level filter).
                var levelSet = new HashSet<string>(
                    distinct.Where(n => Enum.TryParse<FindNeedlePluginLib.Level>(n, out _)),
                    StringComparer.OrdinalIgnoreCase);

                Levels.Clear();
                foreach (var level in levelSet.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
                {
                    var color = DefaultLevelColors.TryGetValue(level, out var c) ? c : "Transparent";
                    Levels.Add(new LevelEntry { Level = level, HexColor = color });
                }

                KnownLevelNames.Clear();
                foreach (var l in Levels.Select(x => x.Level)) KnownLevelNames.Add(l);

                TotalCount = _source.TotalCount;
            }

            if (sample != null)
            {
                using (PerfLog.Scope("viewer.native.autohide_sample"))
                    AutoHideEmptyColumnsFromSample(sample);
            }

            // ReloadFromSourceAsyncCore runs the count/level/page queries on the threadpool (vs the
            // synchronous ReloadFromSource) so the first page lands without blocking the UI thread.
            using (PerfLog.Scope("viewer.native.first_page"))
                await ReloadFromSourceAsyncCore();
            IsStreaming = streaming;
            if (streaming)
            {
                StreamingBannerText = ComposeStreamBanner();
                EnsureRefreshTimer();
                _refreshTimer?.Start(); // tick periodically through the load (don't wait for the first batch event)
            }
        }
        finally
        {
            // For streaming, IsLoading stays true until the producer signals completion via
            // RowsAvailable + IsLoading=false. For one-shot loads, flip it off now.
            if (!streaming) IsLoading = false;
        }
    }

    // ----- Streaming: live row refresh -----

    private Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer _refreshTimer;

    private void EnsureRefreshTimer()
    {
        _dispatcher ??= Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (_refreshTimer == null && _dispatcher != null)
        {
            _refreshTimer = _dispatcher.CreateTimer();
            _refreshTimer.Interval = TimeSpan.FromMilliseconds(300);
            // Repeating, NOT debounced. A debounced (restart-on-every-row) timer never fires while
            // rows pour in faster than the interval — the count froze mid-load (e.g. stuck at 18k)
            // and only jumped to the total at the end. A repeating tick refreshes periodically
            // throughout the load and stops itself once the producer finishes.
            _refreshTimer.IsRepeating = true;
            _refreshTimer.Tick += (_, _) => OnLiveRefreshTick();
        }
    }

    /// <summary>
    /// Called from the SQLite writer thread when a batch lands. Ensure the repeating refresh timer is
    /// running (it ticks on the UI thread until the producer signals completion). We do NOT restart it
    /// per batch — that would debounce it into never firing during a continuous load.
    /// </summary>
    private void OnStreamingRowsAvailable()
    {
        EnsureRefreshTimer();
        _dispatcher?.TryEnqueue(() =>
        {
            if (_refreshTimer != null && !_refreshTimer.IsRunning) _refreshTimer.Start();
        });
    }

    private bool _liveRefreshInFlight;

    /// <summary>True when any filter/search is narrowing the view.</summary>
    public bool HasActiveFilter() =>
        !string.IsNullOrEmpty(_searchText) || !string.IsNullOrEmpty(_providerFilter) ||
        !string.IsNullOrEmpty(_taskNameFilter) || !string.IsNullOrEmpty(_messageFilter) ||
        !string.IsNullOrEmpty(_sourceFilter) || !string.IsNullOrEmpty(_levelFilter) ||
        _fromDate.HasValue || _toDate.HasValue ||
        _providerFilterSet != null || _taskNameFilterSet != null || _sourceFilterSet != null;

    // Shown while streaming with a filter active: the live re-filter is paused (so the app isn't
    // constantly re-searching the growing table); the user clicks Refresh to fold in new matches.
    private bool _hasPendingRows;
    public bool HasPendingRows
    {
        get => _hasPendingRows;
        set { if (Set(ref _hasPendingRows, value)) OnPropertyChanged(nameof(PendingRowsVisibility)); }
    }
    public Microsoft.UI.Xaml.Visibility PendingRowsVisibility =>
        _hasPendingRows ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    private async void OnLiveRefreshTick()
    {
        if (_source == null) return;
        var stillLoading = _source.IsLoading;
        TotalCount = _source.TotalCount; // O(1) running counter — cheap on the UI thread

        if (stillLoading)
        {
            StreamingBannerText = ComposeStreamBanner();
            // With a filter active, DON'T keep re-running the (expensive) search as rows stream in —
            // that's the "constantly searching/reloading" churn. Pause and offer a manual Refresh.
            if (HasActiveFilter())
            {
                HasPendingRows = true;
                return;
            }

            // Once the visible page is full, newly-streamed rows land *past* it, so the on-screen rows
            // don't change — rebuilding them every tick only steals the DataGrid's selection/focus and
            // makes rows "glitch" while you click. Keep just the running total/page count fresh; leave
            // the visible rows alone until the load finishes (or the user pages/refreshes).
            if (Results.Count >= _pageSize)
            {
                TotalFilteredCount = TotalCount;
                OnPropertyChanged(nameof(TotalPages));
                OnPropertyChanged(nameof(PageRangeText));
                UpdateStatus();
                return;
            }
        }
        else
        {
            // Producer finished — stop the timer, do one final refresh below, drop spinner/Stop.
            _refreshTimer?.Stop();
            IsLoading = false;
            IsStreaming = false;
        }

        // No filter (or load just finished): refresh the visible page + counts OFF the UI thread so a
        // re-query during a heavy load never freezes the UI. Skip overlapping refreshes.
        if (_liveRefreshInFlight) return;
        _liveRefreshInFlight = true;
        try { await ReloadFromSourceAsyncCore(); HasPendingRows = false; }
        catch { /* transient read during a concurrent write — the next tick (or completion) retries */ }
        finally { _liveRefreshInFlight = false; }
    }

    /// <summary>Manually fold newly-loaded rows into the current (filtered) view, then keep streaming.</summary>
    public async void RefreshNow()
    {
        if (_liveRefreshInFlight) { HasPendingRows = false; return; }
        _liveRefreshInFlight = true;
        try { await ReloadFromSourceAsyncCore(); HasPendingRows = false; }
        catch { /* transient — user can click again */ }
        finally { _liveRefreshInFlight = false; }
    }

    /// <summary>Re-run the current filter (count + level counts + visible page) off the UI thread,
    /// preserving the current page, then publish on the UI thread. Used by the live streaming refresh.</summary>
    private async Task ReloadFromSourceAsyncCore(System.Threading.CancellationToken ct = default)
    {
        if (_source == null) return;
        var filters = BuildFilterSpec();
        var sort = new SortSpec(_sortColumn, _sortDescending);
        int pageSize = _pageSize;
        int curPage = _currentPage;

        var r = await Task.Run(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int total = _source.GetFilteredCount(filters); long tc = sw.ElapsedMilliseconds; sw.Restart();
            var levels = _source.GetLevelCounts(filters); long tl = sw.ElapsedMilliseconds; sw.Restart();
            int pages = Math.Max(1, (int)Math.Ceiling(total / (double)Math.Max(1, pageSize)));
            int page = Math.Clamp(curPage, 1, pages);
            var rows = _source.GetPage(filters, sort, (page - 1) * pageSize, pageSize);
            long tp = sw.ElapsedMilliseconds;
            return (total, levels, rows, page, tc, tl, tp);
        }).ConfigureAwait(false);

        if (ct.IsCancellationRequested) return; // a newer filter apply superseded this one

        await RunOnUiAsync(() =>
        {
            if (ct.IsCancellationRequested) return;
            TotalFilteredCount = r.total;
            UpdateLevelCountsFrom(r.levels);
            _currentPage = r.page;
            Results.ReplaceAll(r.rows);
            UpdateFilterTiming(r.tc, r.tl, r.tp);
            OnPropertyChanged(nameof(CurrentPage));
            OnPropertyChanged(nameof(TotalPages));
            OnPropertyChanged(nameof(PageRangeText));
            UpdateStatus();
        });
    }

    /// <summary>
    /// Detach from a live streaming source. Called from <c>NativeResultsPage.OnPageUnloaded</c>
    /// so the source doesn't keep firing into a dead VM if the user navigates away mid-load.
    /// </summary>
    public void DetachFromStreaming()
    {
        if (_source != null) _source.RowsAvailable -= OnStreamingRowsAvailable;
        try { _refreshTimer?.Stop(); } catch { /* ignore */ }
    }

    /// <summary>Drop all loaded data and show an empty view — called when the workspace is cleared.
    /// Runs synchronously so the source is detached before the owning storage is disposed (otherwise
    /// the viewer could read a closed SQLite connection). Filters are reset too, so the empty view
    /// reads as "No results" rather than "filters hid everything".</summary>
    public void ResetToEmpty()
    {
        DetachFromStreaming();
        ResetRuleViewFilter();
        ClearFiltersNoReload();
        try { _source?.Dispose(); } catch { /* ignore */ }
        _source = new InMemoryPagedSource(Array.Empty<LogLine>());
        Results.ReplaceAll(Array.Empty<LogLine>());
        IsStreaming = false;
        HasPendingRows = false;
        TotalCount = 0;
        TotalFilteredCount = 0;
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(PageRangeText));
    }

    /// <summary>Cancel the in-flight streaming search, if any.</summary>
    public void StopStreaming()
    {
        MiddleLayerService.CurrentStreamingSearch?.Stop();
    }

    /// <summary>
    /// Auto-hide columns where every loaded row is empty for that field. We only sample the
    /// first ~1000 rows (already in memory via the fallback list) — this is a UX nicety, not a
    /// hard guarantee. Plain-text logs typically have empty TaskName for every single row.
    /// </summary>
    internal void AutoHideEmptyColumnsFromSample(IList<LogLine> sample)
    {
        if (sample == null || sample.Count == 0) return;

        var getters = new Dictionary<string, Func<LogLine, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Provider"] = l => l.Provider,
            ["TaskName"] = l => l.TaskName,
            ["Source"]   = l => l.Source,
            ["Message"]  = l => l.Message,
        };

        int sampleSize = Math.Min(sample.Count, 1000);
        foreach (var col in Columns)
        {
            if (!getters.TryGetValue(col.Name, out var get)) continue;
            bool allEmpty = true;
            for (int i = 0; i < sampleSize; i++)
            {
                if (!string.IsNullOrWhiteSpace(get(sample[i]))) { allEmpty = false; break; }
            }
            if (allEmpty) col.IsVisible = false;
        }
    }

    // ----- Filtering -----

    /// <summary>UI-driven filter apply: runs the query off the UI thread with a short debounce and a
    /// busy flag (<see cref="IsApplyingFilter"/>) so a multi-second filter on a large set shows a loader
    /// instead of freezing the window. Rapid changes cancel the prior apply.</summary>
    public void ApplyFilters()
    {
        _currentPage = 1;
        _ = ApplyFiltersBusyAsync();
    }

    /// <summary>Synchronous filter apply — updates <see cref="TotalFilteredCount"/> immediately for
    /// programmatic callers that read the count right after (e.g. MCP set-filters). Not for the UI
    /// path (it would block the UI thread); UI changes use <see cref="ApplyFilters"/>.</summary>
    public void ApplyFiltersSync()
    {
        _currentPage = 1;
        ReloadFromSource();
    }

    private System.Threading.CancellationTokenSource _applyCts;
    private bool _isApplyingFilter;
    /// <summary>True while a filter change is being applied off the UI thread — the viewer shows the
    /// "Filtering…" loader so a slow query reads as "working" rather than "hung".</summary>
    public bool IsApplyingFilter { get => _isApplyingFilter; private set => Set(ref _isApplyingFilter, value); }

    private async Task ApplyFiltersBusyAsync()
    {
        _applyCts?.Cancel();
        var cts = _applyCts = new System.Threading.CancellationTokenSource();
        var ct = cts.Token;
        try
        {
            // Debounce: coalesce rapid changes (multi-select clicks, typing) and don't flash the loader
            // for queries that finish well under this.
            await Task.Delay(120, ct).ConfigureAwait(false);
            await RunOnUiAsync(() => { if (!ct.IsCancellationRequested) IsApplyingFilter = true; });
            await ReloadFromSourceAsyncCore(ct);
        }
        catch (System.OperationCanceledException) { /* superseded by a newer apply */ }
        finally
        {
            // Only the most-recent apply owns the flag; a cancelled one leaves it to its successor.
            if (ReferenceEquals(_applyCts, cts))
                await RunOnUiAsync(() => IsApplyingFilter = false);
        }
    }

    /// <summary>Reset filters to defaults without re-querying — the caller reloads. Used when a new file
    /// is opened so its results aren't hidden by the previous file's filter.</summary>
    public void ClearFiltersNoReload()
    {
        _searchText = "";
        ReparseSearch(); // drop any parsed query + error
        _providerFilter = "";
        _taskNameFilter = "";
        _messageFilter = "";
        _sourceFilter = "";
        _levelFilter = "";
        _fromDate = null;
        _toDate = null;
        _providerFilterSet = _taskNameFilterSet = _sourceFilterSet = null;
        MiddleLayerService.OutputTimeFrom = MiddleLayerService.OutputTimeTo = null;
        _currentPage = 1;
        HasPendingRows = false;
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(ProviderFilter));
        OnPropertyChanged(nameof(TaskNameFilter));
        OnPropertyChanged(nameof(MessageFilter));
        OnPropertyChanged(nameof(SourceFilter));
        OnPropertyChanged(nameof(LevelFilter));
        OnPropertyChanged(nameof(FromDate));
        OnPropertyChanged(nameof(ToDate));
    }

    /// <summary>
    /// Apply the current filters off the UI thread (count + level counts + first page), then publish
    /// the results back on the UI thread. Keeps a multi-second search — e.g. a 1-2 char term that
    /// falls back to a LIKE scan on a multi-million-row log — from freezing the UI. If
    /// <paramref name="ct"/> was cancelled because a newer search started, the stale result is dropped.
    /// </summary>
    public async Task ApplyFiltersAsync(System.Threading.CancellationToken ct)
    {
        var filters = BuildFilterSpec();
        var sort = new SortSpec(_sortColumn, _sortDescending);
        int pageSize = _pageSize;

        // The actual queries (COUNT, level counts, first page) run on a background thread. Awaiting
        // from the UI thread resumes back on it, so the publish below is UI-thread-safe.
        var snapshot = await Task.Run(() =>
        {
            var total  = _source.GetFilteredCount(filters);
            var levels = _source.GetLevelCounts(filters);
            var rows   = _source.GetPage(filters, sort, 0, pageSize);
            return (total, levels, rows);
        }).ConfigureAwait(false);

        if (ct.IsCancellationRequested) return; // a newer search superseded this one

        // Publish on the UI thread, always — Results/INotify mutations off-thread crash the DataGrid.
        await RunOnUiAsync(() =>
        {
            if (ct.IsCancellationRequested) return;
            _currentPage = 1;
            TotalFilteredCount = snapshot.total;
            UpdateLevelCountsFrom(snapshot.levels);
            Results.ReplaceAll(snapshot.rows);
            OnPropertyChanged(nameof(CurrentPage));
            OnPropertyChanged(nameof(TotalPages));
            OnPropertyChanged(nameof(PageRangeText));
            UpdateStatus();
        });
    }

    /// <summary>Run <paramref name="action"/> on the UI thread (inline if already there).</summary>
    private Task RunOnUiAsync(Action action)
    {
        var dq = _uiDispatcher;
        if (dq == null || dq.HasThreadAccess) { action(); return Task.CompletedTask; }
        var tcs = new TaskCompletionSource();
        if (!dq.TryEnqueue(() => { try { action(); } finally { tcs.TrySetResult(); } }))
            action(); // queue unavailable (shutting down) — best effort
        return tcs.Task;
    }

    /// <summary>
    /// Set the search term WITHOUT running the synchronous filter — the caller drives the async apply
    /// via <see cref="ApplyFiltersAsync"/>. Returns false if the term was unchanged.
    /// </summary>
    public bool SetSearchTextDeferred(string value)
    {
        value ??= "";
        if (string.Equals(_searchText, value, StringComparison.Ordinal)) return false;
        _searchText = value;
        ReparseSearch(); // keep the parsed-query / substring state in sync (MCP-driven set)
        OnPropertyChanged(nameof(SearchText));
        return true;
    }

    /// <summary>
    /// Hits the source: total count, level counts (for chips), and the current page's rows.
    /// All other state-keeping (Results, page metadata, status) flows from this.
    /// </summary>
    private void ReloadFromSource()
    {
        var filters = BuildFilterSpec();
        var sort = new SortSpec(_sortColumn, _sortDescending);

        // Time each stage so the user can see what made a filter slow (count vs level chips vs page).
        var sw = System.Diagnostics.Stopwatch.StartNew();
        TotalFilteredCount = _source.GetFilteredCount(filters);
        long tCount = sw.ElapsedMilliseconds; sw.Restart();
        UpdateLevelCountsFrom(_source.GetLevelCounts(filters));
        long tLevels = sw.ElapsedMilliseconds; sw.Restart();

        // Clamp page to valid range after filter changed total.
        if (_currentPage > TotalPages && TotalPages > 0) _currentPage = TotalPages;
        if (_currentPage < 1) _currentPage = 1;

        var offset = (_currentPage - 1) * _pageSize;
        var page = _source.GetPage(filters, sort, offset, _pageSize);
        long tPage = sw.ElapsedMilliseconds;
        Results.ReplaceAll(page);

        UpdateFilterTiming(tCount, tLevels, tPage);

        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(PageRangeText));
        UpdateStatus();
    }

    private void UpdateFilterTiming(long tCount, long tLevels, long tPage)
    {
        long total = tCount + tLevels + tPage;
        LastFilterSummary = $"⏱ {total} ms";

        var active = new List<string>();
        if (!string.IsNullOrEmpty(_searchText)) active.Add($"search \"{_searchText}\"");
        if (!string.IsNullOrEmpty(_providerFilter)) active.Add("provider");
        if (!string.IsNullOrEmpty(_taskNameFilter)) active.Add("task");
        if (!string.IsNullOrEmpty(_messageFilter)) active.Add("message");
        if (!string.IsNullOrEmpty(_sourceFilter)) active.Add("source");
        if (!string.IsNullOrEmpty(_levelFilter)) active.Add("level");
        if (_fromDate.HasValue || _toDate.HasValue) active.Add("time range");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Last filter: {total} ms total");
        sb.AppendLine($"  • Count query:   {tCount} ms");
        sb.AppendLine($"  • Level counts:  {tLevels} ms");
        sb.AppendLine($"  • Page fetch:    {tPage} ms");
        sb.AppendLine($"  • Rows matched:  {TotalFilteredCount:N0}");
        sb.AppendLine(!string.IsNullOrEmpty(_searchText)
            ? $"  • Text search:   on ({(IsIndexing ? "index still building — slower LIKE scan" : "using full-text index")})"
            : "  • Text search:   off");
        sb.Append($"  • Active filters: {(active.Count > 0 ? string.Join(", ", active) : "none")}");
        LastFilterBreakdown = sb.ToString();
    }

    private FilterSpec BuildFilterSpec() => new(
        Search: _effectiveSearch ?? "",   // "" when the box holds a structured query (see _parsedQuery)
        Provider: _providerFilter ?? "",
        TaskName: _taskNameFilter ?? "",
        Message: _messageFilter ?? "",
        Source: _sourceFilter ?? "",
        Level: _levelFilter ?? "",
        FromTime: _fromDate,
        ToTime: _toDate)
    {
        ProviderSet = _providerFilterSet,
        TaskNameSet = _taskNameFilterSet,
        SourceSet = _sourceFilterSet,
        Query = _parsedQuery,
    };

    public void ClearFilters()
    {
        SearchText = "";
        ProviderFilter = TaskNameFilter = MessageFilter = SourceFilter = LevelFilter = "";
        FromDate = null;
        ToDate = null;
        // Clear multi-select sets without an extra reload each (the property sets above already reload).
        _providerFilterSet = _taskNameFilterSet = _sourceFilterSet = null;
    }

    // ----- Headless drive hooks (used by the MCP viewer bridge) -----

    /// <summary>
    /// Set all filter fields at once and reload exactly once (each individual setter would trigger
    /// its own reload). A null argument leaves that field unchanged; pass "" to clear a text field.
    /// Returns the new filtered count. Must run on the UI thread.
    /// </summary>
    public int SetFiltersBulk(string search, string provider, string taskName, string message,
        string source, string level, DateTime? fromTime, DateTime? toTime,
        bool clearFromTime = false, bool clearToTime = false)
    {
        if (search != null)   { _searchText = search; ReparseSearch(); OnPropertyChanged(nameof(SearchText)); }
        if (provider != null) { _providerFilter = provider; OnPropertyChanged(nameof(ProviderFilter)); }
        if (taskName != null) { _taskNameFilter = taskName; OnPropertyChanged(nameof(TaskNameFilter)); }
        if (message != null)  { _messageFilter = message;   OnPropertyChanged(nameof(MessageFilter)); }
        if (source != null)   { _sourceFilter = source;     OnPropertyChanged(nameof(SourceFilter)); }
        if (level != null)    { _levelFilter = level;       OnPropertyChanged(nameof(LevelFilter)); }
        if (fromTime.HasValue || clearFromTime) { _fromDate = clearFromTime ? null : fromTime; OnPropertyChanged(nameof(FromDate)); }
        if (toTime.HasValue || clearToTime)     { _toDate = clearToTime ? null : toTime;       OnPropertyChanged(nameof(ToDate)); }

        ApplyFiltersSync(); // programmatic caller reads TotalFilteredCount below → must be synchronous
        return TotalFilteredCount;
    }

    /// <summary>Look up one row by its stable <see cref="LogLine.RowId"/>, ignoring filter/sort/page.</summary>
    public LogLine GetRecordByRowId(long rowId) => _source?.GetByRowId(rowId);

    /// <summary>A read-only snapshot of the rows currently shown on the active page.</summary>
    public IReadOnlyList<LogLine> CurrentPageRows() => Results;

    /// <summary>
    /// MCP get_context: locate a row's 0-based ordinal in the current filter+sort, then return the
    /// window [ordinal-before, ordinal+after]. Scans up to <paramref name="scanCap"/> rows (in chunks)
    /// to find the id; returns (-1, empty) if it isn't within that cap. The returned rows carry their
    /// filtered Index (set by the paged source).
    /// </summary>
    public (int index, List<LogLine> rows) GetContext(long rowId, int before, int after, int scanCap = 200_000)
    {
        if (_source == null) return (-1, new List<LogLine>());
        before = Math.Clamp(before, 0, 100);
        after = Math.Clamp(after, 0, 100);
        var filters = BuildFilterSpec();
        var sort = new SortSpec(_sortColumn, _sortDescending);
        int cap = Math.Min(_source.GetFilteredCount(filters), scanCap);
        int targetIndex = -1;
        const int chunk = 4000;
        for (int off = 0; off < cap && targetIndex < 0; off += chunk)
        {
            var page = _source.GetPage(filters, sort, off, Math.Min(chunk, cap - off));
            if (page.Count == 0) break;
            for (int i = 0; i < page.Count; i++)
                if (page[i].RowId == rowId) { targetIndex = off + i; break; }
        }
        if (targetIndex < 0) return (-1, new List<LogLine>());
        int start = Math.Max(0, targetIndex - before);
        var rows = _source.GetPage(filters, sort, start, before + after + 1);
        return (targetIndex, rows);
    }

    /// <summary>An arbitrary [offset, offset+limit) slice of the current filtered/sorted result.</summary>
    public List<LogLine> GetRows(int offset, int limit)
        => _source == null ? new List<LogLine>()
                           : _source.GetPage(BuildFilterSpec(), new SortSpec(_sortColumn, _sortDescending), offset, limit);

    /// <summary>
    /// Count rows in the current filtered set that also fall within [from, to] (for the MCP
    /// <c>histogram</c>). Intersects with whatever time filter is already active.
    /// </summary>
    public int CountInTimeRange(DateTime? from, DateTime? to)
    {
        if (_source == null) return 0;
        var f = BuildFilterSpec();
        // Tighten the existing time bounds with the bucket bounds (keep the more restrictive).
        DateTime? lo = Max(f.FromTime, from);
        DateTime? hi = Min(f.ToTime, to);
        return _source.GetFilteredCount(f with { FromTime = lo, ToTime = hi });
    }

    private static DateTime? Max(DateTime? a, DateTime? b)
        => a is null ? b : b is null ? a : (a.Value >= b.Value ? a : b);
    private static DateTime? Min(DateTime? a, DateTime? b)
        => a is null ? b : b is null ? a : (a.Value <= b.Value ? a : b);

    /// <summary>
    /// Min/max LogTime over the current filtered set (for the MCP <c>summary</c>). Two single-row
    /// queries via the source's sort, so it's cheap on any backend. Null when the set is empty.
    /// </summary>
    public (DateTime? min, DateTime? max) GetFilteredTimeRange()
    {
        if (_source == null) return (null, null);
        var f = BuildFilterSpec();
        var first = _source.GetPage(f, new SortSpec("Time", false), 0, 1);
        var last  = _source.GetPage(f, new SortSpec("Time", true), 0, 1);
        DateTime? min = first.Count > 0 ? first[0].LogTime : (DateTime?)null;
        DateTime? max = last.Count > 0 ? last[0].LogTime : (DateTime?)null;
        return (min, max);
    }

    /// <summary>The latest event time across the WHOLE loaded set (ignores the current filter). Used
    /// to anchor relative time presets ("last 1h" = the last hour of the log, so they work for
    /// historical logs, not just live ones). Null if there's no data.</summary>
    public DateTime? GetDataMaxTime()
    {
        if (_source == null) return null;
        var last = _source.GetPage(FilterSpec.Empty, new SortSpec("Time", true), 0, 1);
        return last.Count > 0 ? last[0].LogTime : (DateTime?)null;
    }

    /// <summary>Top distinct values of a field over the current filtered set (MCP <c>facets</c>).</summary>
    public FindNeedleUX.Services.Mcp.LogAnalysis.FacetResult GetFacets(string field, int limit, int sampleCap)
        => FindNeedleUX.Services.Mcp.LogAnalysis.Facets(_source, BuildFilterSpec(), field, limit, sampleCap);

    /// <summary>
    /// Facets for one known-value field computed over every OTHER active filter but ignoring this
    /// field's own selection — so the field's dropdown shows the values still reachable given the rest
    /// of the filters (cross-filter narrowing), without collapsing to only what's already picked.
    /// </summary>
    public FindNeedleUX.Services.Mcp.LogAnalysis.FacetResult GetFacetsExcludingField(string field, int limit, int sampleCap)
    {
        var spec = BuildFilterSpec();
        spec = field switch
        {
            "Provider" => spec with { Provider = "", ProviderSet = null },
            "TaskName" => spec with { TaskName = "", TaskNameSet = null },
            "Source"   => spec with { Source = "",   SourceSet = null },
            _ => spec,
        };

        // Fast path: an exact GROUP BY on the field's column (SQLite) / a tally (in-memory) — far
        // cheaper than scanning a 200k-row sample and materializing every column, so the known-value
        // dropdowns open quickly even on a multi-million-row load. Falls back to the sampling scan if
        // the source can't do it.
        try
        {
            var counts = _source?.GetFieldCounts(field, spec);
            if (counts != null && counts.Count > 0)
            {
                int total = 0;
                foreach (var c in counts.Values) total += c;
                var values = counts
                    .Where(kv => !string.IsNullOrEmpty(kv.Key))
                    .OrderByDescending(kv => kv.Value)                          // cap to the most common N…
                    .Take(limit <= 0 ? 1000 : limit)
                    .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)    // …then display alphabetically
                    .Select(kv => new FindNeedleUX.Services.Mcp.LogAnalysis.Facet(kv.Key, kv.Value))
                    .ToList();
                // Exact (whole filtered set, not a sample) → Scanned == Total, not truncated.
                return new FindNeedleUX.Services.Mcp.LogAnalysis.FacetResult(field, total, total, false, values);
            }
        }
        catch { /* fall back to the sampling scan below */ }

        return FindNeedleUX.Services.Mcp.LogAnalysis.Facets(_source, spec, field, limit, sampleCap);
    }

    /// <summary>Most common message templates over the current filtered set (MCP <c>top_patterns</c>).</summary>
    public FindNeedleUX.Services.Mcp.LogAnalysis.PatternResult GetTopPatterns(int limit, int sampleCap)
        => FindNeedleUX.Services.Mcp.LogAnalysis.TopPatterns(_source, BuildFilterSpec(), limit, sampleCap);

    /// <summary>Current level counts over the filtered set (for the MCP <c>summary</c>).</summary>
    public Dictionary<string, int> GetFilteredLevelCounts()
        => _source == null ? new Dictionary<string, int>() : new Dictionary<string, int>(_source.GetLevelCounts(BuildFilterSpec()));

    /// <summary>Exact distinct Source values (file/origin) with row counts — cheap (GROUP BY / tally),
    /// bounded by the number of loaded sources, not the row count. Used by the Sources dialog's
    /// by-type toggles instead of the O(rows) facet scan.</summary>
    public Dictionary<string, int> GetSourceCounts()
        => _source?.GetSourceCounts() ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    private void UpdateStatus()
    {
        StatusText = $"{_totalFilteredCount:N0} / {TotalCount:N0} results";
    }

    private void UpdateLevelCountsFrom(IDictionary<string, int> counts)
    {
        foreach (var entry in Levels)
            entry.Count = counts.TryGetValue(entry.Level, out var n) ? n : 0;
    }

    /// <summary>
    /// Slice <see cref="_filtered"/> into the current page and bulk-replace <see cref="Results"/>.
    /// (Used by pagination button clicks; ReloadFromSource handles full filter rebuilds.)
    /// </summary>
    /// <summary>Re-render the current page in place (e.g. after a row tag changes) without re-filtering.</summary>
    public void RefreshCurrentPage() => PublishCurrentPage();

    private void PublishCurrentPage()
    {
        var filters = BuildFilterSpec();
        var sort = new SortSpec(_sortColumn, _sortDescending);
        var offset = (_currentPage - 1) * _pageSize;
        var page = _source.GetPage(filters, sort, offset, _pageSize);
        Results.ReplaceAll(page);
    }

    // ----- CSV export -----
    /// <summary>Output format for <see cref="ExportAsync"/>.</summary>
    public enum ExportFormat { Csv, Json, Xml }

    /// <summary>Back-compat wrapper — older callers still call ExportCsvAsync().</summary>
    public Task<string> ExportCsvAsync() => ExportAsync(ExportFormat.Csv);

    /// <summary>
    /// Save the currently-filtered+sorted result set to a file the user picks. Rows are streamed
    /// from the paged source so we never materialise the whole set in one chunk. Output covers
    /// all visible columns (the user's column-visibility choice acts as a column filter on the
    /// export, same as the CSV behaviour).
    /// </summary>
    public async Task<string> ExportAsync(ExportFormat format)
    {
        try
        {
            var (ext, label, ftKey) = format switch
            {
                ExportFormat.Json => (".json", "JSON Files", "JSON"),
                ExportFormat.Xml  => (".xml",  "XML Files",  "XML"),
                _                 => (".csv",  "CSV Files",  "CSV"),
            };

            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(WindowUtil.GetMainWindow());
            var suggested = $"findneedle-results-{DateTime.Now:yyyyMMdd-HHmmss}{ext}";
            var path = FindNeedleUX.Services.Win32FileDialog.SaveFile(
                hWnd, suggested, new (string, string)[] { (label, "*" + ext) }, ext);
            if (path == null) return null;

            var visibleNames = Columns.Where(c => c.IsVisible).Select(c => c.Name).ToList();
            var filters = BuildFilterSpec();
            var sort = new SortSpec(_sortColumn, _sortDescending);

            var lines = FindNeedleUX.Services.ResultExporter.BuildLines(
                _source, filters, sort, visibleNames, ToExporterFormat(format), out _);

            await System.IO.File.WriteAllLinesAsync(path, lines).ConfigureAwait(false);
            return path;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Export error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Headless export: write the current filtered+sorted set (over the visible columns) to
    /// <paramref name="path"/> with no file picker. Used by the MCP <c>export</c> tool. Returns the
    /// number of data rows written, or -1 on error.
    /// </summary>
    public async Task<int> ExportToPathAsync(ExportFormat format, string path)
    {
        try
        {
            var visibleNames = Columns.Where(c => c.IsVisible).Select(c => c.Name).ToList();
            var filters = BuildFilterSpec();
            var sort = new SortSpec(_sortColumn, _sortDescending);
            var lines = FindNeedleUX.Services.ResultExporter.BuildLines(
                _source, filters, sort, visibleNames, ToExporterFormat(format), out var rowCount);
            await System.IO.File.WriteAllLinesAsync(path, lines).ConfigureAwait(false);
            return rowCount;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Export error: {ex.Message}");
            return -1;
        }
    }

    private static FindNeedleUX.Services.ResultExporter.Format ToExporterFormat(ExportFormat f) => f switch
    {
        ExportFormat.Json => FindNeedleUX.Services.ResultExporter.Format.Json,
        ExportFormat.Xml  => FindNeedleUX.Services.ResultExporter.Format.Xml,
        _                 => FindNeedleUX.Services.ResultExporter.Format.Csv,
    };

    // ----- INPC plumbing -----
    public event PropertyChangedEventHandler PropertyChanged;

    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string name = null, bool applyFilters = false)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        if (applyFilters) ApplyFilters();
        return true;
    }
}

public class LevelEntry : INotifyPropertyChanged
{
    public string Level { get; set; } = "";

    private string _hexColor = "Transparent";
    public string HexColor
    {
        get => _hexColor;
        set { if (_hexColor != value) { _hexColor = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HexColor))); } }
    }

    private int _count;
    public int Count
    {
        get => _count;
        set { if (_count != value) { _count = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count))); } }
    }

    public event PropertyChangedEventHandler PropertyChanged;
}

/// <summary>
/// One row in a "known value" filter dropdown (Provider / TaskName / Source): a distinct value plus
/// how many rows currently have it. <see cref="Display"/> shows the count alongside the value, like
/// SimpleEventViewer. The <see cref="IsAll"/> sentinel is the leading "(All)" entry that clears the
/// field's filter.
/// </summary>
public sealed class KnownFacetItem
{
    public string Value { get; init; } = "";
    public int Count { get; init; }
    public bool IsAll { get; init; }
    public string Display => IsAll ? "(All)" : $"{Value}  ({Count:N0})";
}

/// <summary>Pure label helpers for the known-value filter dropdowns (extracted so they're unit-testable
/// without a WinUI control). Mirrors SimpleEventViewer's summary style.</summary>
public static class KnownFilterLabel
{
    /// <summary>"Field: All" with nothing chosen, "Field: value" for one, "Field: value +N" for many.</summary>
    public static string Summarize(string field, System.Collections.Generic.IReadOnlyList<string> selected)
    {
        int n = selected?.Count ?? 0;
        return n == 0 ? $"{field}: All"
             : n == 1 ? $"{field}: {selected[0]}"
             : $"{field}: {selected[0]} +{n - 1}";
    }
}

public class ColumnEntry : INotifyPropertyChanged
{
    public string Name { get; set; } = "";

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible != value)
            {
                _isVisible = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
                VisibilityChanged?.Invoke(this);
            }
        }
    }

    public event Action<ColumnEntry> VisibilityChanged;
    public event PropertyChangedEventHandler PropertyChanged;
}

/// <summary>
/// <see cref="ObservableCollection{T}"/> that supports a single-shot bulk replacement.
/// </summary>
public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IList<T> newItems)
    {
        Items.Clear();
        if (newItems != null)
        {
            if (Items is List<T> list)
            {
                if (list.Capacity < newItems.Count) list.Capacity = newItems.Count;
            }
            for (int i = 0; i < newItems.Count; i++) Items.Add(newItems[i]);
        }
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
