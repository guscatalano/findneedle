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

namespace FindNeedleUX.Pages.NativeResultViewer;

/// <summary>
/// ViewModel for the native WinUI 3 result viewer.
/// Mirrors the feature set of the web (DataTables) viewer:
///   - global search, per-column filters, time-range filter, level filter
///   - per-level row coloring (with user-overridable colors)
///   - per-level visible counts
///   - column visibility toggle
///   - status text (visible/total + rate)
///   - export CSV
///   - load from MiddleLayerService
/// </summary>
public class NativeResultsPageViewModel : INotifyPropertyChanged
{
    // ----- backing data -----
    private readonly List<LogLine> _all = new();          // every loaded row, unfiltered
    private List<LogLine> _filtered = new();              // every row matching current filters/sort
    // RangeObservableCollection bulk-replaces via a single Reset notification. With pagination
    // we only ever publish one page (~100 rows) so even per-item Add wouldn't be expensive — but
    // ReplaceAll lets us swap the whole page atomically when filters or page change.
    public RangeObservableCollection<LogLine> Results { get; } = new();

    // ----- pagination -----
    private int _pageSize = 100;
    public int PageSize
    {
        get => _pageSize;
        set { if (_pageSize != value && value > 0) { _pageSize = value; PageOrSizeChanged(); } }
    }

    private int _currentPage = 1;     // 1-indexed
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

    // ----- sort state (applied to the full filtered set, not just the visible page) -----
    private string _sortColumn;        // null = no sort
    private bool _sortDescending;
    public string SortColumn => _sortColumn;
    public bool SortDescending => _sortDescending;

    public void ApplySort(string column, bool descending)
    {
        _sortColumn = string.IsNullOrEmpty(column) ? null : column;
        _sortDescending = descending;
        SortFiltered();
        CurrentPage = 1;  // sort always resets to page 1
        PublishCurrentPage();
    }

    private void SortFiltered()
    {
        if (_sortColumn == null) return;
        Comparison<LogLine> cmp = _sortColumn switch
        {
            "Index"    => (a, b) => a.Index.CompareTo(b.Index),
            "Time"     => (a, b) => a.LogTime.CompareTo(b.LogTime),
            "Provider" => (a, b) => string.Compare(a.Provider, b.Provider, StringComparison.OrdinalIgnoreCase),
            "TaskName" => (a, b) => string.Compare(a.TaskName, b.TaskName, StringComparison.OrdinalIgnoreCase),
            "Message"  => (a, b) => string.Compare(a.Message,  b.Message,  StringComparison.OrdinalIgnoreCase),
            "Source"   => (a, b) => string.Compare(a.Source,   b.Source,   StringComparison.OrdinalIgnoreCase),
            "Level"    => (a, b) => string.Compare(a.Level,    b.Level,    StringComparison.OrdinalIgnoreCase),
            _          => null
        };
        if (cmp == null) return;
        if (_sortDescending) { var inner = cmp; cmp = (a, b) => -inner(a, b); }
        _filtered.Sort(cmp);
    }

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

    // ----- status state -----
    private DateTime? _loadStartedUtc;

    private int _totalCount;
    public int TotalCount { get => _totalCount; set { if (Set(ref _totalCount, value)) UpdateStatus(); } }

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => Set(ref _isLoading, value); }

    private string _statusText = "0 / 0 results";
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

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

    // Theme presets. Colors are 8-digit ARGB so they alpha-blend over the
    // grid's row background — readable in both light and dark mode.
    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ThemePresets =
        new Dictionary<string, IReadOnlyDictionary<string, string>>
    {
        ["Subtle"] = new Dictionary<string, string>
        {
            { "Catastrophic", "#33D32F2F" }, // 20% red
            { "Critical",     "#28E53935" }, // 16% red
            { "Error",        "#1FEF5350" }, // 12% lighter red
            { "Warning",      "#22FFA000" }, // 13% amber
            { "Info",         "Transparent" },
            { "Verbose",      "#12808080" }, // ~7% gray
            { "Debug",        "#14546E7A" }  // ~8% blue-gray
        },
        ["Vivid"] = new Dictionary<string, string>
        {
            { "Catastrophic", "#FFB3B3" },
            { "Critical",     "#FFCCCC" },
            { "Error",        "#FFE1E1" },
            { "Warning",      "#FFF4CC" },
            { "Info",         "Transparent" },
            { "Verbose",      "#F1F1F1" },
            { "Debug",        "#EEF3FF" }
        },
        ["None"] = new Dictionary<string, string>
        {
            { "Catastrophic", "Transparent" },
            { "Critical",     "Transparent" },
            { "Error",        "Transparent" },
            { "Warning",      "Transparent" },
            { "Info",         "Transparent" },
            { "Verbose",      "Transparent" },
            { "Debug",        "Transparent" }
        }
    };

    public const string DefaultThemeName = "Subtle";

    /// <summary>Default per-level row backgrounds = the Subtle theme.</summary>
    public static IReadOnlyDictionary<string, string> DefaultLevelColors => ThemePresets[DefaultThemeName];

    /// <summary>Apply a named theme preset to every level. Does nothing if the name is unknown.</summary>
    public void ApplyTheme(string themeName)
    {
        if (string.IsNullOrEmpty(themeName) || !ThemePresets.TryGetValue(themeName, out var theme)) return;
        foreach (var entry in Levels)
        {
            if (theme.TryGetValue(entry.Level, out var hex)) entry.HexColor = hex;
            else entry.HexColor = "Transparent";
        }
    }

    // ----- Loading -----
    public async Task LoadResultsAsync()
    {
        IsLoading = true;
        _loadStartedUtc = DateTime.UtcNow;
        try
        {
            var lines = MiddleLayerService.GetLogLines() ?? new List<LogLine>();
            _all.Clear();
            _all.AddRange(lines);

            // Discover levels in the data set.
            var levelSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in _all)
                if (!string.IsNullOrEmpty(l.Level)) levelSet.Add(l.Level);

            // Merge defaults so users always see the standard levels even if absent.
            foreach (var def in DefaultLevelColors.Keys) levelSet.Add(def);

            Levels.Clear();
            foreach (var level in levelSet.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
            {
                var color = DefaultLevelColors.TryGetValue(level, out var c) ? c : "Transparent";
                Levels.Add(new LevelEntry { Level = level, HexColor = color });
            }

            KnownLevelNames.Clear();
            foreach (var l in Levels.Select(x => x.Level)) KnownLevelNames.Add(l);

            TotalCount = _all.Count;
            AutoHideEmptyColumns();
            ApplyFilters();
        }
        finally
        {
            IsLoading = false;
        }
        await Task.CompletedTask;
    }

    /// <summary>
    /// Hide any column whose values are 100% empty — typical for plain-text logs where the source
    /// format doesn't carry TaskName/MachineName/Username/OpCode. Plugins now return empty for
    /// fields they don't support (the LogLine constructor also normalizes the legacy
    /// "!NOT_SUPPORTED!" sentinel just in case). User can re-enable via Columns ▾.
    /// </summary>
    private void AutoHideEmptyColumns()
    {
        if (_all.Count == 0) return;

        var getters = new Dictionary<string, Func<LogLine, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Provider"] = l => l.Provider,
            ["TaskName"] = l => l.TaskName,
            ["Source"]   = l => l.Source,
            ["Message"]  = l => l.Message,
        };

        foreach (var col in Columns)
        {
            if (!getters.TryGetValue(col.Name, out var get)) continue;
            if (_all.All(line => string.IsNullOrWhiteSpace(get(line))))
                col.IsVisible = false;  // Don't auto-enable a column the user explicitly toggled off.
        }
    }

    // ----- Filtering -----

    // Optimized for large result sets:
    //   - One foreach pass instead of a chain of LINQ Where wrappers (no per-row delegate overhead).
    //   - All filter strings captured in locals once; no re-checking IsNullOrWhiteSpace per row.
    //   - StringComparison.OrdinalIgnoreCase substring match (no per-row ToLowerInvariant
    //     allocations — the previous Has() allocated a lower-cased copy of every field for every row).
    //   - Cheap predicates run first (date range, level exact match), expensive global search runs last.
    //   - Accumulate matches in a List<LogLine> then publish via RangeObservableCollection.ReplaceAll
    //     so the UI sees a single Reset notification instead of one CollectionChanged per row.
    //   - LevelCounts computed from the local list in the same pass we'd otherwise use.
    public void ApplyFilters()
    {
        // Snapshot filter state once. Empty strings become null so the inner loop only does
        // a null check (faster than IsNullOrWhiteSpace on every row).
        string search   = NullIfBlank(SearchText);
        string provider = NullIfBlank(ProviderFilter);
        string taskName = NullIfBlank(TaskNameFilter);
        string message  = NullIfBlank(MessageFilter);
        string source   = NullIfBlank(SourceFilter);
        string level    = NullIfBlank(LevelFilter);
        DateTime? from  = FromDate;
        DateTime? to    = ToDate;
        bool indexCouldMatchSearch = search != null && search.Length > 0 && IsAllAsciiDigit(search);

        var matches = new List<LogLine>(_all.Count);

        for (int i = 0; i < _all.Count; i++)
        {
            var line = _all[i];

            // Cheapest checks first.
            if (from.HasValue && line.LogTime < from.Value) continue;
            if (to.HasValue   && line.LogTime > to.Value)   continue;
            if (level != null && !string.Equals(line.Level, level, StringComparison.OrdinalIgnoreCase)) continue;

            // Per-column substring checks. Each is one OrdinalIgnoreCase Contains, no allocation.
            if (provider != null && !ContainsIgnoreCase(line.Provider, provider)) continue;
            if (taskName != null && !ContainsIgnoreCase(line.TaskName, taskName)) continue;
            if (message  != null && !ContainsIgnoreCase(line.Message,  message))  continue;
            if (source   != null && !ContainsIgnoreCase(line.Source,   source))   continue;

            // Global search last — most expensive (8 fields).
            if (search != null)
            {
                if (!ContainsIgnoreCase(line.Time,           search) &&
                    !ContainsIgnoreCase(line.Provider,       search) &&
                    !ContainsIgnoreCase(line.TaskName,       search) &&
                    !ContainsIgnoreCase(line.Message,        search) &&
                    !ContainsIgnoreCase(line.Source,         search) &&
                    !ContainsIgnoreCase(line.Level,          search) &&
                    !ContainsIgnoreCase(line.SearchableData, search) &&
                    !(indexCouldMatchSearch &&
                      line.Index.ToString().IndexOf(search, StringComparison.Ordinal) >= 0))
                {
                    continue;
                }
            }

            matches.Add(line);
        }

        _filtered = matches;
        SortFiltered();
        TotalFilteredCount = _filtered.Count;
        _currentPage = 1;
        PublishCurrentPage();
        UpdateLevelCountsFrom(_filtered);
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(PageRangeText));
        OnPropertyChanged(nameof(CurrentPage));
        UpdateStatus();
    }

    /// <summary>
    /// Slice <see cref="_filtered"/> into the current page and bulk-replace <see cref="Results"/>.
    /// The DataGrid only ever sees ~PageSize rows, so virtualization + scroll are essentially free.
    /// </summary>
    private void PublishCurrentPage()
    {
        int total = _filtered.Count;
        if (total == 0) { Results.ReplaceAll(System.Array.Empty<LogLine>()); return; }
        int start = (_currentPage - 1) * _pageSize;
        int len = Math.Min(_pageSize, total - start);
        if (start < 0 || start >= total || len <= 0)
        {
            Results.ReplaceAll(System.Array.Empty<LogLine>());
            return;
        }
        // GetRange copies into a new List<LogLine> — small (PageSize), one-shot.
        var page = _filtered.GetRange(start, len);
        Results.ReplaceAll(page);
    }

    private static string NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static bool ContainsIgnoreCase(string s, string needle) =>
        s != null && s.Length > 0 && s.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static bool IsAllAsciiDigit(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c < '0' || c > '9') return false;
        }
        return true;
    }

    public void ClearFilters()
    {
        SearchText = "";
        ProviderFilter = TaskNameFilter = MessageFilter = SourceFilter = LevelFilter = "";
        FromDate = null;
        ToDate = null;
    }

    private void UpdateStatus()
    {
        // Status reflects the FILTERED total, not the page slice — users care about how many
        // rows their filter matched, not which page they're on.
        StatusText = $"{_totalFilteredCount:N0} / {TotalCount:N0} results";
    }

    /// <summary>
    /// Iterates the supplied list directly instead of <see cref="Results"/> — saves one extra
    /// pass over the visible row set after a filter rebuild.
    /// </summary>
    private void UpdateLevelCountsFrom(IList<LogLine> visible)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < visible.Count; i++)
        {
            var lvl = visible[i].Level;
            var k = string.IsNullOrEmpty(lvl) ? "(none)" : lvl;
            counts.TryGetValue(k, out var c);
            counts[k] = c + 1;
        }
        foreach (var entry in Levels)
            entry.Count = counts.TryGetValue(entry.Level, out var n) ? n : 0;
    }

    // ----- CSV export -----
    public async Task<string> ExportCsvAsync()
    {
        try
        {
            var picker = new global::Windows.Storage.Pickers.FileSavePicker();
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(WindowUtil.GetMainWindow());
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
            picker.SuggestedFileName = $"findneedle-results-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
            picker.FileTypeChoices.Add("CSV Files", new[] { ".csv" });

            var file = await picker.PickSaveFileAsync();
            if (file == null) return null;

            // Export everything matching the current filters/sort (NOT just the current page).
            var visibleNames = Columns.Where(c => c.IsVisible).Select(c => c.Name).ToList();

            var lines = new List<string>(_filtered.Count + 1)
            {
                string.Join(",", visibleNames.Select(EscapeCsv))
            };
            foreach (var row in _filtered)
            {
                lines.Add(string.Join(",", visibleNames.Select(name => EscapeCsv(GetField(row, name)))));
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
    // Settable (not init-only) so the XAML compiler's generated type-info accepts it.
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
/// <see cref="ReplaceAll"/> mutates the underlying <c>Items</c> list directly and fires exactly
/// one <see cref="NotifyCollectionChangedAction.Reset"/> event plus the standard Count and indexer
/// PropertyChanged notifications. Bound DataGrid receives one Reset and re-renders the whole list,
/// far cheaper than N per-item Add notifications.
/// </summary>
public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IList<T> newItems)
    {
        // Items is the protected, non-observable inner list — bypass per-item events.
        Items.Clear();
        if (newItems != null)
        {
            // ObservableCollection<T>.Items is a List<T>; preallocate to skip resize churn.
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
