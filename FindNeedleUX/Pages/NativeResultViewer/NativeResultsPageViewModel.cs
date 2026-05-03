using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using FindNeedleUX;
using FindNeedleUX.Services;
using FindNeedlePluginLib;
using FindPluginCore;
using FindPluginCore.GlobalConfiguration;
using Microsoft.UI.Xaml;

namespace FindNeedleUX.Pages.NativeResultViewer;

/// <summary>
/// ViewModel for the native WinUI 3 result viewer
/// </summary>
public partial class NativeResultsPageViewModel : INotifyPropertyChanged
{
    private readonly ObservableCollection<LogLine> _allResults = new();
    private readonly ObservableCollection<LogLine> _filteredResults = new();
    
    private string _searchText = "";
    private DateTime? _fromDate;
    private DateTime? _toDate;
    private int _totalCount;
    private bool _isLoading;
    private string _statusText = "";

    public NativeResultsPageViewModel()
    {
        _filteredResults.CollectionChanged += (s, e) => OnPropertyChanged(nameof(Results));
        LoadResultsCommand = new AsyncRelayCommand(LoadResultsAsync);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<LogLine> Results => _filteredResults;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText != value)
            {
                _searchText = value;
                ApplyFilters();
                OnPropertyChanged();
            }
        }
    }

    public DateTime? FromDate
    {
        get => _fromDate;
        set
        {
            if (_fromDate != value)
            {
                _fromDate = value;
                ApplyFilters();
                OnPropertyChanged();
            }
        }
    }

    public DateTime? ToDate
    {
        get => _toDate;
        set
        {
            if (_toDate != value)
            {
                _toDate = value;
                ApplyFilters();
                OnPropertyChanged();
            }
        }
    }

    public int TotalCount
    {
        get => _totalCount;
        set
        {
            if (_totalCount != value)
            {
                _totalCount = value;
                UpdateStatusText();
                OnPropertyChanged();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (_isLoading != value)
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText != value)
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }
    }

    public IAsyncRelayCommand LoadResultsCommand { get; }

    // Level color management
    private readonly Dictionary<string, string> _levelColors = new()
    {
        { "Catastrophic", "#FFB3B3" },
        { "Critical", "#FFCCCC" },
        { "Error", "#FFE1E1" },
        { "Warning", "#FFF4CC" },
        { "Info", "Transparent" },
        { "Verbose", "#F1F1F1" },
        { "Debug", "#EEF3FF" }
    };

    public Dictionary<string, string> LevelColors => _levelColors;

    // Column visibility management
    private readonly Dictionary<string, bool> _columnVisibility = new()
    {
        { "Index", true },
        { "Time", true },
        { "Provider", true },
        { "TaskName", true },
        { "Message", true },
        { "Source", true },
        { "Level", true }
    };

    public Dictionary<string, bool> ColumnVisibility => _columnVisibility;

    private void OnPropertyChanged(string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void UpdateStatusText()
    {
        StatusText = $"{_filteredResults.Count} / {TotalCount} results";
    }

    /// <summary>
    /// Load results from MiddleLayerService
    /// </summary>
    public async Task LoadResultsAsync()
    {
        IsLoading = true;
        try
        {
            // Get all results from MiddleLayerService
            var lines = MiddleLayerService.GetLogLines();
            _allResults.Clear();
            
            foreach (var line in lines)
            {
                _allResults.Add(line);
            }
            
            TotalCount = _allResults.Count;
            
            // Apply current filters
            ApplyFilters();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Apply all filters (search, time range, per-column)
    /// </summary>
    private void ApplyFilters()
    {
        try
        {
            IEnumerable<LogLine> query = _allResults;

            // Search filter (all columns)
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var searchLower = SearchText.ToLowerInvariant();
                query = query.Where(line =>
                    line.Index.ToString().ToLowerInvariant().Contains(searchLower) ||
                    (line.Time?.ToLowerInvariant().Contains(searchLower) ?? false) ||
                    (line.Provider?.ToLowerInvariant().Contains(searchLower) ?? false) ||
                    (line.TaskName?.ToLowerInvariant().Contains(searchLower) ?? false) ||
                    (line.Message?.ToLowerInvariant().Contains(searchLower) ?? false) ||
                    (line.Source?.ToLowerInvariant().Contains(searchLower) ?? false) ||
                    (line.Level?.ToLowerInvariant().Contains(searchLower) ?? false));
            }

            // Time range filter — uses LogTime (DateTime) since LogLine.Time is the formatted string.
            if (FromDate.HasValue)
            {
                query = query.Where(line => line.LogTime >= FromDate.Value);
            }

            if (ToDate.HasValue)
            {
                query = query.Where(line => line.LogTime <= ToDate.Value);
            }

            _filteredResults.Clear();
            foreach (var item in query)
            {
                _filteredResults.Add(item);
            }
        }
        catch (Exception ex)
        {
            // Log error but don't crash
            System.Diagnostics.Debug.WriteLine($"Filter error: {ex.Message}");
        }
    }

    /// <summary>
    /// Export visible results to CSV
    /// </summary>
    public async Task ExportToCsvAsync()
    {
        try
        {
            var picker = new global::Windows.Storage.Pickers.FileSavePicker();
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(WindowUtil.GetMainWindow());
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
            picker.SuggestedFileName = $"findneedle-results-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
            picker.FileTypeChoices.Add("CSV Files", new[] { ".csv" });

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            var csvLines = new System.Collections.Generic.List<string>();

            // Header
            csvLines.Add("Index,Time,Provider,TaskName,Message,Source,Level");

            // Rows
            foreach (var line in _filteredResults)
            {
                var row = string.Join(",",
                    EscapeCsv(line.Index.ToString()),
                    EscapeCsv(line.Time),
                    EscapeCsv(line.Provider),
                    EscapeCsv(line.TaskName),
                    EscapeCsv(line.Message),
                    EscapeCsv(line.Source),
                    EscapeCsv(line.Level)
                );
                csvLines.Add(row);
            }

            await global::Windows.Storage.FileIO.WriteLinesAsync(file, csvLines);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Export error: {ex.Message}");
        }
    }

    private string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }

    /// <summary>
    /// Clear all filters
    /// </summary>
    public void ClearFilters()
    {
        SearchText = "";
        FromDate = null;
        ToDate = null;
    }
}
