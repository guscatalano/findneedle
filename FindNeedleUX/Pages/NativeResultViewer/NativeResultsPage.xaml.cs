using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.WinUI.UI.Controls;
using FindNeedleUX;
using FindNeedleUX.Pages.NativeResultViewer;
using FindNeedleUX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace FindNeedleUX.Pages;

/// <summary>
/// Native WinUI 3 result viewer page
/// </summary>
public sealed partial class NativeResultsPage : Page
{
    private NativeResultsPageViewModel ViewModel { get; } = new();

    public NativeResultsPage()
    {
        this.InitializeComponent();
        LoadDataAsync();
        UpdateColumnVisibility();
    }

    private async void LoadDataAsync()
    {
        await ViewModel.LoadResultsCommand.ExecuteAsync(null);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.SearchText = SearchBox.Text;
    }

    private async void ExportCsvButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ExportToCsvAsync();
    }

    private void ClearFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearFilters();
        SearchBox.Text = "";
        FromDatePicker.Date = null;
        ToDatePicker.Date = null;
    }

    private void ColumnsButton_Click(object sender, RoutedEventArgs e)
    {
        // Show column visibility toggle panel
        var panel = new ColumnVisibilityPanel(ViewModel);
        panel.ShowAsync();
    }

    private void ApplyTimeFilterButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.FromDate = FromDatePicker.Date?.DateTime;
        ViewModel.ToDate = ToDatePicker.Date?.DateTime;
    }

    private void CopyJson_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is LogLine selectedLine)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(selectedLine, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            var package = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(json);
            global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        }
    }

    private static readonly Dictionary<string, string> _levelBrushKeys = new()
    {
        { "Catastrophic", "LevelCatastrophicColor" },
        { "Critical",     "LevelCriticalColor" },
        { "Error",        "LevelErrorColor" },
        { "Warning",      "LevelWarningColor" },
        { "Info",         "LevelInfoColor" },
        { "Verbose",      "LevelVerboseColor" },
        { "Debug",        "LevelDebugColor" }
    };

    private void ResultsGrid_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        if (e.Row.DataContext is not LogLine line) return;
        if (line.Level != null && _levelBrushKeys.TryGetValue(line.Level, out var key)
            && Resources.TryGetValue(key, out var brush) && brush is Microsoft.UI.Xaml.Media.Brush b)
        {
            e.Row.Background = b;
        }
    }

    private async void LevelColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.CommandParameter is string level)
        {
            var colorPicker = new ColorPicker();
            colorPicker.IsAlphaEnabled = false;
            
            // Set current color
            if (ViewModel.LevelColors.TryGetValue(level, out var currentColor))
            {
                colorPicker.Color = ParseColor(currentColor);
            }

            var dialog = new ContentDialog
            {
                Title = $"Set Color for {level}",
                Content = colorPicker,
                PrimaryButtonText = "OK",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                var color = colorPicker.Color;
                ViewModel.LevelColors[level] = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
                UpdateLevelColors();
            }
        }
    }

    private global::Windows.UI.Color ParseColor(string hex)
    {
        hex = hex.Replace("#", "").Replace("Transparent", "00FFFFFF");
        if (hex.Length == 6) hex = "FF" + hex; // Add alpha if missing
        if (hex.Length != 8) return global::Windows.UI.Color.FromArgb(255, 255, 255, 255);

        return global::Windows.UI.Color.FromArgb(
            (byte)Convert.ToInt32(hex.Substring(0, 2), 16),
            (byte)Convert.ToInt32(hex.Substring(2, 2), 16),
            (byte)Convert.ToInt32(hex.Substring(4, 2), 16),
            (byte)Convert.ToInt32(hex.Substring(6, 2), 16));
    }

    private void UpdateLevelColors()
    {
        // Update the static resources with new colors
        foreach (var kvp in ViewModel.LevelColors)
        {
            var key = $"Level{kvp.Key}Color";
            if (Resources.TryGetValue(key, out var existing) && existing is Microsoft.UI.Xaml.Media.SolidColorBrush brush)
            {
                brush.Color = ParseColor(kvp.Value);
            }
        }
        // Refresh grid rows by re-binding
        ResultsGrid.ItemsSource = null;
        ResultsGrid.ItemsSource = ViewModel.Results;
    }

    private void ColumnVisibilityCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.Tag is string colName)
        {
            ViewModel.ColumnVisibility[colName] = true;
            UpdateColumnVisibility();
        }
    }

    private void ColumnVisibilityCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.Tag is string colName)
        {
            ViewModel.ColumnVisibility[colName] = false;
            UpdateColumnVisibility();
        }
    }

    private void UpdateColumnVisibility()
    {
        foreach (var col in ResultsGrid.Columns)
        {
            if (ViewModel.ColumnVisibility.TryGetValue(col.Header.ToString(), out var visible))
            {
                col.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private void CloseColumnPanelButton_Click(object sender, RoutedEventArgs e)
    {
        // The XAML <Border x:Name="ColumnVisibilityPanel"> shadows the type name in this scope,
        // so resolve via FindName to avoid ambiguity with the helper class below.
        if (this.FindName("ColumnVisibilityPanel") is FrameworkElement panel)
            panel.Visibility = Visibility.Collapsed;
    }
}

/// <summary>
/// Simple column visibility toggle panel
/// </summary>
public class ColumnVisibilityPanel
{
    private readonly NativeResultsPageViewModel _viewModel;
    private readonly Dictionary<string, bool> _columnVisibility = new();

    public ColumnVisibilityPanel(NativeResultsPageViewModel viewModel)
    {
        _viewModel = viewModel;
        // Initialize with current state
        foreach (var kvp in viewModel.ColumnVisibility)
        {
            _columnVisibility[kvp.Key] = kvp.Value;
        }
    }

    public async Task ShowAsync()
    {
        var panel = new ContentDialog
        {
            Title = "Column Visibility",
            Content = CreateColumnTogglePanel(),
            CloseButtonText = "Close",
            XamlRoot = WindowUtil.GetMainWindow()?.Content?.XamlRoot
        };

        await panel.ShowAsync();

        // Apply changes after dialog closes
        foreach (var kvp in _columnVisibility)
        {
            _viewModel.ColumnVisibility[kvp.Key] = kvp.Value;
        }
        // UpdateColumnVisibility() will be called in the main class
    }

    private StackPanel CreateColumnTogglePanel()
    {
        var panel = new StackPanel { Spacing = 8 };

        var columns = new[] { "Index", "Time", "Provider", "TaskName", "Message", "Source", "Level" };
        
        foreach (var col in columns)
        {
            var cb = new CheckBox
            {
                Content = col,
                IsChecked = _columnVisibility.GetValueOrDefault(col, true),
                Margin = new Thickness(0, 4, 0, 0)
            };
            
            cb.Checked += (s, e) => _columnVisibility[col] = true;
            cb.Unchecked += (s, e) => _columnVisibility[col] = false;
            
            panel.Children.Add(cb);
        }

        return panel;
    }
}
