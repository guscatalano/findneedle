using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using FindNeedleUX.Pages.NativeResultViewer;
using FindNeedleUX.Services;
using FindPluginCore.GlobalConfiguration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FindNeedleUX.Pages;

/// <summary>
/// Dedicated settings page for the result viewer (time format + color theme + per-level colors).
/// All changes are persisted via <see cref="ResultsViewerSettings"/> and broadcast to any open
/// viewer via that service's <c>Changed</c> event.
/// </summary>
public sealed partial class ResultsViewerSettingsPage : Page
{
    /// <summary>One <see cref="LevelEntry"/> per known level, used by the editor list.</summary>
    public ObservableCollection<LevelEntry> Levels { get; } = new();

    private bool _suppressEvents;

    public ResultsViewerSettingsPage()
    {
        this.InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => FindNeedleUX.Services.Mcp.McpServerHost.StatusChanged -= OnMcpStatusChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _suppressEvents = true;
        try
        {
            // --- Time format ---
            SelectComboItemByTag(TimeFormatCombo, ResultsViewerSettings.TimeFormat);
            UpdateTimeFormatPreview();

            // --- Theme ---
            SelectComboItemByTag(ThemeCombo, ResultsViewerSettings.ThemeName);

            // --- Levels: start from the active theme preset, then layer per-level overrides. ---
            RebuildLevelEditor();

            // --- Storage backend ---
            SelectStorageInComboBox();

            // --- Cache reuse ---
            SelectCacheReuseMode();

            // --- Indexing mode ---
            SelectIndexingMode();

            // --- Search submit mode ---
            SelectSearchSubmitMode();

            // --- Search progress ---
            ShowStepHistoryCheck.IsChecked = ResultsViewerSettings.ShowStepHistory;

            // --- Row tags ---
            ColorTaggedRowsCheck.IsChecked = ResultsViewerSettings.ColorTaggedRows;

            // --- MCP server ---
            McpEnabledCheck.IsChecked = ResultsViewerSettings.McpServerEnabled;
            McpPortBox.Text = ResultsViewerSettings.McpServerPort.ToString();
            UpdateMcpStatus();

            // --- WPP / tracefmt TMF path ---
            TmfPathBox.Text = ResultsViewerSettings.TraceFormatSearchPath;
            FindNeedleUX.Services.Mcp.McpServerHost.StatusChanged -= OnMcpStatusChanged;
            FindNeedleUX.Services.Mcp.McpServerHost.StatusChanged += OnMcpStatusChanged;

            // --- Column defaults ---
            BuildColumnDefaultsCheckboxes();

            SettingsPathText.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FindNeedle", "viewer-settings.json");

            LevelEditor.ItemsSource = Levels;
        }
        finally { _suppressEvents = false; }
    }

    private void BuildColumnDefaultsCheckboxes()
    {
        ColumnDefaultsPanel.Children.Clear();
        var current = ResultsViewerSettings.ColumnVisibility;
        foreach (var col in NativeResultsPageViewModel.DefaultColumnNames)
        {
            var cb = new CheckBox
            {
                Content = col,
                IsChecked = current.TryGetValue(col, out var v) && v,
                Tag = col
            };
            cb.Checked   += ColumnDefault_Toggled;
            cb.Unchecked += ColumnDefault_Toggled;
            ColumnDefaultsPanel.Children.Add(cb);
        }
    }

    private void ColumnDefault_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        if (sender is CheckBox cb && cb.Tag is string col)
            ResultsViewerSettings.SetColumnVisibility(col, cb.IsChecked == true);
    }

    private void ResetColumnDefaults_Click(object sender, RoutedEventArgs e)
    {
        ResultsViewerSettings.ClearColumnVisibility();
        _suppressEvents = true;
        try { BuildColumnDefaultsCheckboxes(); }
        finally { _suppressEvents = false; }
    }

    private void RebuildLevelEditor()
    {
        Levels.Clear();
        var theme = NativeResultsPageViewModel.ThemePresets.TryGetValue(ResultsViewerSettings.ThemeName, out var t)
            ? t : NativeResultsPageViewModel.ThemePresets[NativeResultsPageViewModel.DefaultThemeName];
        var overrides = ResultsViewerSettings.LevelColors;

        foreach (var levelName in NativeResultsPageViewModel.ThemePresets[NativeResultsPageViewModel.DefaultThemeName].Keys)
        {
            string hex = overrides.TryGetValue(levelName, out var ov) ? ov
                       : theme.TryGetValue(levelName, out var th) ? th
                       : "Transparent";
            Levels.Add(new LevelEntry { Level = levelName, HexColor = hex });
        }
    }

    private static void SelectComboItemByTag(ComboBox combo, string tag)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private void UpdateTimeFormatPreview()
    {
        var fmt = (TimeFormatCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? ResultsViewerSettings.DefaultTimeFormat;
        try { TimeFormatPreview.Text = "Preview: " + DateTime.Now.ToString(fmt); }
        catch { TimeFormatPreview.Text = "Preview: (invalid format)"; }
    }

    // ----- Time format -----
    private void TimeFormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateTimeFormatPreview();
        if (_suppressEvents) return;
        if (TimeFormatCombo.SelectedItem is ComboBoxItem item && item.Tag is string fmt)
            ResultsViewerSettings.TimeFormat = fmt;
    }

    // ----- Theme -----
    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (ThemeCombo.SelectedItem is ComboBoxItem item && item.Tag is string themeName)
        {
            // Switching theme clears all per-level overrides — the user's intent is "use this preset".
            ResultsViewerSettings.ClearLevelColors();
            ResultsViewerSettings.ThemeName = themeName;
            RebuildLevelEditor();
        }
    }

    // ----- Per-level color editor -----
    private async void PickColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string level) return;
        var entry = Levels.FirstOrDefault(x => string.Equals(x.Level, level, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return;

        var picker = new ColorPicker { IsAlphaEnabled = true, Color = HexToBrushConverter.ParseColor(entry.HexColor) };
        var dialog = new ContentDialog
        {
            Title = $"Color for {level}",
            Content = picker,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var c = picker.Color;
            var hex = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
            entry.HexColor = hex;
            ResultsViewerSettings.SetLevelColor(level, hex);
        }
    }

    private void ResetColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string level) return;

        // Clear this single level's override by saving the theme default in its place.
        var theme = NativeResultsPageViewModel.ThemePresets.TryGetValue(ResultsViewerSettings.ThemeName, out var t)
            ? t : NativeResultsPageViewModel.ThemePresets[NativeResultsPageViewModel.DefaultThemeName];
        var def = theme.TryGetValue(level, out var d) ? d : "Transparent";

        // SetLevelColor stores it as an override. Acceptable — user explicitly clicked reset.
        ResultsViewerSettings.SetLevelColor(level, def);

        var entry = Levels.FirstOrDefault(x => string.Equals(x.Level, level, StringComparison.OrdinalIgnoreCase));
        if (entry != null) entry.HexColor = def;
    }

    private void ResetAllColors_Click(object sender, RoutedEventArgs e)
    {
        ResultsViewerSettings.ClearLevelColors();
        RebuildLevelEditor();
    }

    // ----- Cache reuse -----
    private void SelectCacheReuseMode()
    {
        var current = ResultsViewerSettings.CacheReuseMode.ToString();
        foreach (var item in CacheReuseModeCombo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, current, StringComparison.OrdinalIgnoreCase))
            {
                CacheReuseModeCombo.SelectedItem = item;
                return;
            }
        }
    }

    private void CacheReuseModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (CacheReuseModeCombo.SelectedItem is ComboBoxItem item
            && item.Tag is string tag
            && Enum.TryParse<FindPluginCore.Searching.CacheReuseMode>(tag, ignoreCase: true, out var mode))
        {
            ResultsViewerSettings.CacheReuseMode = mode;
        }
    }

    // ----- Indexing mode -----
    private void SelectIndexingMode()
    {
        var current = ResultsViewerSettings.IndexingMode.ToString();
        foreach (var item in IndexingModeCombo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, current, StringComparison.OrdinalIgnoreCase))
            {
                IndexingModeCombo.SelectedItem = item;
                return;
            }
        }
    }

    private void IndexingModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (IndexingModeCombo.SelectedItem is ComboBoxItem item
            && item.Tag is string tag
            && Enum.TryParse<FindPluginCore.Searching.IndexingMode>(tag, ignoreCase: true, out var mode))
        {
            ResultsViewerSettings.IndexingMode = mode;
        }
    }

    // ----- Search submit mode -----
    private void SelectSearchSubmitMode()
    {
        var current = ResultsViewerSettings.SearchSubmitMode.ToString();
        foreach (var item in SearchSubmitModeCombo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, current, StringComparison.OrdinalIgnoreCase))
            {
                SearchSubmitModeCombo.SelectedItem = item;
                return;
            }
        }
    }

    private void SearchSubmitModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (SearchSubmitModeCombo.SelectedItem is ComboBoxItem item
            && item.Tag is string tag
            && Enum.TryParse<FindNeedleUX.Services.SearchSubmitMode>(tag, ignoreCase: true, out var mode))
        {
            ResultsViewerSettings.SearchSubmitMode = mode;
        }
    }

    private void ColorTaggedRowsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        ResultsViewerSettings.ColorTaggedRows = ColorTaggedRowsCheck.IsChecked == true;
    }

    private void ShowStepHistoryCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        ResultsViewerSettings.ShowStepHistory = ShowStepHistoryCheck.IsChecked == true;
    }

    // ----- MCP server -----
    private void McpEnabledCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        // Setter broadcasts Changed → McpServerHost starts/stops accordingly.
        ResultsViewerSettings.McpServerEnabled = McpEnabledCheck.IsChecked == true;
    }

    private void McpPortBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        if (int.TryParse(McpPortBox.Text, out var port) && port > 0 && port <= 65535)
            ResultsViewerSettings.McpServerPort = port;
        else
            McpPortBox.Text = ResultsViewerSettings.McpServerPort.ToString(); // revert invalid input
    }

    // ----- WPP / tracefmt TMF path -----
    private void TmfPathBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        ResultsViewerSettings.TraceFormatSearchPath = TmfPathBox.Text?.Trim() ?? "";
    }

    private void BrowseTmfPath_Click(object sender, RoutedEventArgs e)
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(WindowUtil.GetMainWindow());
        var path = Win32FileDialog.PickFolder(hWnd);
        if (path != null)
        {
            TmfPathBox.Text = path;
            ResultsViewerSettings.TraceFormatSearchPath = path;
        }
    }

    private void OnMcpStatusChanged() => DispatcherQueue.TryEnqueue(UpdateMcpStatus);

    private void UpdateMcpStatus()
    {
        if (McpStatusText == null) return;
        McpStatusText.Text = "Status: " + FindNeedleUX.Services.Mcp.McpServerHost.Status;
    }

    // ----- Storage backend -----
    private void SelectStorageInComboBox()
    {
        var current = findneedle.PluginSubsystem.PluginManager.GetSingleton().config?.SearchStorageType
                      ?? FindPluginCore.PluginSubsystem.StorageType.Auto;
        foreach (var item in StorageCombo.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string tag
                && Enum.TryParse<FindPluginCore.PluginSubsystem.StorageType>(tag, ignoreCase: true, out var parsed)
                && parsed == current)
            {
                StorageCombo.SelectedItem = item;
                return;
            }
        }
    }

    private void StorageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (StorageCombo.SelectedItem is not ComboBoxItem item) return;
        if (item.Tag is not string tag) return;
        if (!Enum.TryParse<FindPluginCore.PluginSubsystem.StorageType>(tag, ignoreCase: true, out var parsed)) return;

        var manager = findneedle.PluginSubsystem.PluginManager.GetSingleton();
        if (manager.config != null)
        {
            manager.config.SearchStorageType = parsed;
            manager.SaveToFile();
        }
    }
}
