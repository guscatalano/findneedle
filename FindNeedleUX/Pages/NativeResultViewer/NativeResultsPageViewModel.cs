using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
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

    public RangeObservableCollection<LogLine> Results { get; } = new();

    // ----- filter state -----
    private string _searchText = "";
    public string SearchText { get => _searchText; set => Set(ref _searchText, value, applyFilters: true); }

    private string _providerFilter = "";
    public string ProviderFilter { get => _providerFilter; set => Set(ref _providerFilter, value, applyFilters: true); }

    private string _taskNameFilter = "";
    public string TaskNameFilter { get => _taskNameFilter; set => Set(ref _taskNameFilter, value, applyFilters: true); }

    private string _messageFilter = "";
    public string MessageFilter { get => _messageFilter; set => Set(ref _messageFilter, value, applyFilters: true); }

    private string _sourceFilter = "";
    public string SourceFilter { get => _sourceFilter; set => Set(ref _sourceFilter, value, applyFilters: true); }

    private string _levelFilter = "";
    public string LevelFilter { get => _levelFilter; set => Set(ref _levelFilter, value, applyFilters: true); }

    private DateTime? _fromDate;
    public DateTime? FromDate { get => _fromDate; set => Set(ref _fromDate, value, applyFilters: true); }

    private DateTime? _toDate;
    public DateTime? ToDate { get => _toDate; set => Set(ref _toDate, value, applyFilters: true); }

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
        "Index", "Time", "Provider", "TaskName", "Message", "Source", "Level"
    };

    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ThemePresets =
        new Dictionary<string, IReadOnlyDictionary<string, string>>
    {
        ["Subtle"] = new Dictionary<string, string>
        {
            { "Catastrophic", "#33D32F2F" }, { "Critical", "#28E53935" },
            { "Error",        "#1FEF5350" }, { "Warning",  "#22FFA000" },
            { "Info",         "Transparent" },
            { "Verbose",      "#12808080" }, { "Debug",    "#14546E7A" }
        },
        ["Vivid"] = new Dictionary<string, string>
        {
            { "Catastrophic", "#FFB3B3" }, { "Critical", "#FFCCCC" },
            { "Error",        "#FFE1E1" }, { "Warning",  "#FFF4CC" },
            { "Info",         "Transparent" },
            { "Verbose",      "#F1F1F1" }, { "Debug",    "#EEF3FF" }
        },
        ["None"] = new Dictionary<string, string>
        {
            { "Catastrophic", "Transparent" }, { "Critical", "Transparent" },
            { "Error",        "Transparent" }, { "Warning",  "Transparent" },
            { "Info",         "Transparent" },
            { "Verbose",      "Transparent" }, { "Debug",    "Transparent" }
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

            // Discover levels and seed Levels/KnownLevelNames.
            using (PerfLog.Scope("viewer.native.levels"))
            {
                var distinct = _source.GetDistinctLevels();
                var levelSet = new HashSet<string>(distinct, StringComparer.OrdinalIgnoreCase);
                foreach (var def in DefaultLevelColors.Keys) levelSet.Add(def);

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

            // AutoHide needs to inspect a small sample of rows. Pull just the first page from the
            // source — for SQLite this is a single LIMIT 1000 query; for in-memory it's a cheap
            // slice. Either way, the cost is bounded regardless of total row count.
            if (!streaming)
            {
                using (PerfLog.Scope("viewer.native.autohide_sample"))
                {
                    var sample = _source.GetPage(FilterSpec.Empty, SortSpec.None, 0, 1000);
                    AutoHideEmptyColumnsFromSample(sample);
                }
            }

            using (PerfLog.Scope("viewer.native.first_page"))
                ReloadFromSource();
            IsStreaming = streaming;
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
            _refreshTimer.Interval = TimeSpan.FromMilliseconds(250);
            _refreshTimer.IsRepeating = false;
            _refreshTimer.Tick += (_, _) => OnLiveRefreshTick();
        }
    }

    /// <summary>
    /// Called from the SQLite writer thread when a batch lands. We marshal to the UI thread
    /// and (re)start a 250 ms timer; the timer's Tick handler does the actual refresh.
    /// Effect: a burst of rapid commits collapses into a single refresh after a quiet moment.
    /// </summary>
    private void OnStreamingRowsAvailable()
    {
        EnsureRefreshTimer();
        _dispatcher?.TryEnqueue(() =>
        {
            _refreshTimer?.Stop();
            _refreshTimer?.Start();
        });
    }

    private void OnLiveRefreshTick()
    {
        if (_source == null) return;
        var stillLoading = _source.IsLoading;
        // Re-query count and the visible page. If the user is on page 1 with default sort,
        // they see new rows immediately; otherwise just the count badge / level chips update.
        TotalCount = _source.TotalCount;
        ReloadFromSource();
        if (!stillLoading)
        {
            // Producer finished — final refresh, drop the spinner + Stop button.
            IsLoading = false;
            IsStreaming = false;
        }
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
    private void AutoHideEmptyColumnsFromSample(IList<LogLine> sample)
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
    public void ApplyFilters()
    {
        _currentPage = 1;
        ReloadFromSource();
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

        TotalFilteredCount = _source.GetFilteredCount(filters);
        UpdateLevelCountsFrom(_source.GetLevelCounts(filters));

        // Clamp page to valid range after filter changed total.
        if (_currentPage > TotalPages && TotalPages > 0) _currentPage = TotalPages;
        if (_currentPage < 1) _currentPage = 1;

        var offset = (_currentPage - 1) * _pageSize;
        var page = _source.GetPage(filters, sort, offset, _pageSize);
        Results.ReplaceAll(page);

        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(PageRangeText));
        UpdateStatus();
    }

    private FilterSpec BuildFilterSpec() => new(
        Search: _searchText ?? "",
        Provider: _providerFilter ?? "",
        TaskName: _taskNameFilter ?? "",
        Message: _messageFilter ?? "",
        Source: _sourceFilter ?? "",
        Level: _levelFilter ?? "",
        FromTime: _fromDate,
        ToTime: _toDate);

    public void ClearFilters()
    {
        SearchText = "";
        ProviderFilter = TaskNameFilter = MessageFilter = SourceFilter = LevelFilter = "";
        FromDate = null;
        ToDate = null;
    }

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

            var picker = new global::Windows.Storage.Pickers.FileSavePicker();
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(WindowUtil.GetMainWindow());
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
            picker.SuggestedFileName = $"findneedle-results-{DateTime.Now:yyyyMMdd-HHmmss}{ext}";
            picker.FileTypeChoices.Add(label, new[] { ext });

            var file = await picker.PickSaveFileAsync();
            if (file == null) return null;

            var visibleNames = Columns.Where(c => c.IsVisible).Select(c => c.Name).ToList();
            var filters = BuildFilterSpec();
            var sort = new SortSpec(_sortColumn, _sortDescending);

            // Pre-size the line buffer (header + body + maybe footer) so AddRange-style growth
            // doesn't double the underlying array ~20 times on a 500k-row export.
            var lines = new List<string>(_totalFilteredCount + 4);

            switch (format)
            {
                case ExportFormat.Csv:
                    lines.Add(string.Join(",", visibleNames.Select(EscapeCsv)));
                    _source.WalkAllFiltered(filters, sort, line =>
                    {
                        lines.Add(string.Join(",", visibleNames.Select(name => EscapeCsv(GetField(line, name)))));
                    });
                    break;

                case ExportFormat.Json:
                    // JSON array of objects. We emit one object per line + commas between them
                    // ourselves rather than building a single giant string with JsonSerializer —
                    // that would force the entire row collection into memory as a single string.
                    lines.Add("[");
                    bool first = true;
                    var jsonOpts = new System.Text.Json.JsonSerializerOptions { WriteIndented = false };
                    _source.WalkAllFiltered(filters, sort, line =>
                    {
                        var dict = new Dictionary<string, object>(visibleNames.Count);
                        foreach (var name in visibleNames) dict[name] = GetField(line, name) ?? "";
                        var entry = System.Text.Json.JsonSerializer.Serialize(dict, jsonOpts);
                        lines.Add(first ? "  " + entry : ", " + entry);
                        first = false;
                    });
                    lines.Add("]");
                    break;

                case ExportFormat.Xml:
                    lines.Add("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                    lines.Add("<rows>");
                    var xmlSb = new System.Text.StringBuilder(256);
                    _source.WalkAllFiltered(filters, sort, line =>
                    {
                        xmlSb.Clear();
                        xmlSb.Append("  <row>");
                        foreach (var name in visibleNames)
                        {
                            var val = GetField(line, name) ?? "";
                            xmlSb.Append('<').Append(name).Append('>');
                            xmlSb.Append(System.Security.SecurityElement.Escape(val));
                            xmlSb.Append("</").Append(name).Append('>');
                        }
                        xmlSb.Append("</row>");
                        lines.Add(xmlSb.ToString());
                    });
                    lines.Add("</rows>");
                    break;
            }

            await global::Windows.Storage.FileIO.WriteLinesAsync(file, lines);
            return file.Path;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Export error: {ex.Message}");
            return null;
        }
    }

    private static string GetField(LogLine line, string columnName) => columnName switch
    {
        "Index"    => line.Index.ToString(),
        "Time"     => line.Time,
        "Provider" => line.Provider,
        "TaskName" => line.TaskName,
        "Message"  => line.Message,
        "Source"   => line.Source,
        "Level"    => line.Level,
        _          => ""
    };

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

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
