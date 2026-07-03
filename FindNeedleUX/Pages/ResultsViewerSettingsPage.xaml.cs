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
    private int _previewStep;

    public ResultsViewerSettingsPage()
    {
        this.InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => FindNeedleUX.Services.Mcp.McpServerHost.StatusChanged -= OnMcpStatusChanged;
    }

    /// <summary>Left-nav category switch: show only the selected category's panel.</summary>
    private void SettingsNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        // Panels may not exist yet if this fires during initial parse (IsSelected="True").
        if (PanelAppearance == null) return;
        // Picking a category exits any active search filter (programmatic clear: TextChanged ignores it).
        if (!string.IsNullOrEmpty(SettingsSearchBox?.Text)) SettingsSearchBox.Text = "";
        ApplyCategory((args.SelectedItem as NavigationViewItem)?.Tag as string);
    }

    /// <summary>Show only the selected category's panel (or all), un-hide any cards a prior search filter
    /// collapsed, and set the title.</summary>
    private void ApplyCategory(string tag)
    {
        if (PanelAppearance == null) return;
        bool all = tag == "all";
        PanelAppearance.Visibility   = all || tag == "appearance"   ? Visibility.Visible : Visibility.Collapsed;
        PanelGeneral.Visibility      = all || tag == "general"      ? Visibility.Visible : Visibility.Collapsed;
        PanelSearch.Visibility       = all || tag == "search"       ? Visibility.Visible : Visibility.Collapsed;
        PanelColumns.Visibility      = all || tag == "columns"      ? Visibility.Visible : Visibility.Collapsed;
        PanelDecoding.Visibility     = all || tag == "decoding"     ? Visibility.Visible : Visibility.Collapsed;
        PanelIntegrations.Visibility = all || tag == "integrations" ? Visibility.Visible : Visibility.Collapsed;
        PanelLogs.Visibility         = all || tag == "logs"         ? Visibility.Visible : Visibility.Collapsed;

        // A prior search filter may have collapsed individual cards — restore them.
        foreach (var e in _settingsCatalog) if (e.Card != null) e.Card.Visibility = Visibility.Visible;

        CategoryTitle.Text = tag switch
        {
            "all"          => "All settings",
            "general"      => "General",
            "search"       => "Search",
            "columns"      => "Columns",
            "decoding"     => "Decoding (WPP symbols)",
            "integrations" => "Integrations",
            "logs"         => "Logs",
            _              => "Appearance",
        };
    }

    // ----- Settings search (pane AutoSuggestBox: "type to find a setting") -----
    private sealed class SettingEntry
    {
        public string Title = "";
        public string Description = "";
        public string Category = "";   // display name, e.g. "Appearance"
        public Border Card;            // the card Border to scroll to
        public string Suggestion => $"{Title}  —  {Category}";
    }

    private readonly System.Collections.Generic.List<SettingEntry> _settingsCatalog = new();

    /// <summary>Walk each category panel once and record (title, description, category, card) for every
    /// card Border, so the search box can match + jump without any per-card manual tagging. New cards are
    /// picked up automatically.</summary>
    private void BuildSettingsCatalog()
    {
        _settingsCatalog.Clear();
        var panels = new (Panel panel, string category)[]
        {
            (PanelAppearance,   "Appearance"),
            (PanelGeneral,      "General"),
            (PanelSearch,       "Search"),
            (PanelColumns,      "Columns"),
            (PanelDecoding,     "Decoding"),
            (PanelIntegrations, "Integrations"),
            (PanelLogs,         "Logs"),
        };
        foreach (var (panel, category) in panels)
        {
            if (panel == null) continue;
            foreach (var card in panel.Children.OfType<Border>())
            {
                var texts = Descendants<TextBlock>(card).ToList();
                string title = texts.FirstOrDefault(t => t.FontWeight.Weight >= 600)?.Text
                               ?? texts.FirstOrDefault()?.Text;
                if (string.IsNullOrWhiteSpace(title)) continue;
                var descParts = texts.Select(t => t.Text)
                    .Concat(Descendants<CheckBox>(card).Select(c => c.Content?.ToString()));
                string description = string.Join(" ",
                    descParts.Where(s => !string.IsNullOrWhiteSpace(s) && s != title));
                _settingsCatalog.Add(new SettingEntry
                {
                    Title = title, Description = description, Category = category, Card = card
                });
            }
        }
    }

    private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        int n = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var d in Descendants<T>(child)) yield return d;
        }
    }

    private void SettingsSearch_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        ApplySettingsFilter(sender.Text);
    }

    // No dropdown suggestions — filtering is inline (the list itself narrows). These stay wired in XAML.
    private void SettingsSearch_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args) { }
    private void SettingsSearch_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        => ApplySettingsFilter(args.QueryText);

    /// <summary>Filter the settings cards live: show all panels, then hide every card whose title or
    /// description doesn't contain the query — so only matching settings remain. Empty query restores the
    /// currently-selected category.</summary>
    private void ApplySettingsFilter(string query)
    {
        var q = (query ?? "").Trim();
        if (q.Length == 0)
        {
            ApplyCategory((SettingsNav.SelectedItem as NavigationViewItem)?.Tag as string ?? "appearance");
            return;
        }

        // Show every category panel so matches from any category appear, then hide non-matching cards.
        PanelAppearance.Visibility = PanelGeneral.Visibility = PanelSearch.Visibility =
            PanelColumns.Visibility = PanelDecoding.Visibility = PanelIntegrations.Visibility =
            PanelLogs.Visibility = Visibility.Visible;

        int matches = 0;
        foreach (var e in _settingsCatalog)
        {
            if (e.Card == null) continue;
            bool match = (e.Title?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                      || (e.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
            e.Card.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
            if (match) matches++;
        }
        CategoryTitle.Text = matches == 0 ? $"No settings match “{q}”" : $"Search: “{q}”";
    }

    /// <summary>Switch to "All settings" (so every panel is visible), scroll the matched card into view,
    /// and briefly outline it so it's obvious which setting matched.</summary>
    private void JumpToSetting(SettingEntry e)
    {
        if (e?.Card == null) return;

        // "All settings" mode → every panel visible, so BringIntoView works for any category.
        var allItem = SettingsNav.MenuItems.OfType<NavigationViewItem>()
            .FirstOrDefault(i => (i.Tag as string) == "all");
        if (allItem != null) SettingsNav.SelectedItem = allItem;
        PanelAppearance.Visibility = PanelGeneral.Visibility = PanelSearch.Visibility =
            PanelColumns.Visibility = PanelDecoding.Visibility = PanelIntegrations.Visibility = Visibility.Visible;
        CategoryTitle.Text = e.Category;

        var card = e.Card;
        // Let the now-visible panels lay out before scrolling/highlighting.
        DispatcherQueue.TryEnqueue(() =>
        {
            card.StartBringIntoView();
            HighlightCard(card);
        });
    }

    private void HighlightCard(Border card)
    {
        if (card == null) return;
        var origBrush = card.BorderBrush;
        var origThickness = card.BorderThickness;
        if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var accent)
            && accent is Microsoft.UI.Xaml.Media.Brush brush)
        {
            card.BorderBrush = brush;
            card.BorderThickness = new Thickness(2);
        }
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(1600);
        timer.IsRepeating = false;
        timer.Tick += (s, _) =>
        {
            card.BorderBrush = origBrush;
            card.BorderThickness = origThickness;
            timer.Stop();
        };
        timer.Start();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _suppressEvents = true;
        try
        {
            // --- Loading animation ---
            PopulateLoaderCombos();
            SelectComboItemByTag(RobotWidthCombo, ResultsViewerSettings.RobotWide ? "Wide" : "Narrow");
            UpdateRobotOptionsVisibility();
            ShowPreviewFrame(0);

            // --- Time format ---
            PopulateTimeFormatCombo();
            SelectComboItemByTag(TimeFormatCombo, ResultsViewerSettings.TimeFormat);
            UpdateTimeFormatPreview();

            // --- Theme ---
            SelectComboItemByTag(ThemeCombo, ResultsViewerSettings.ThemeName);

            // --- Title bar color ---
            SelectComboItemByTag(TitleBarModeCombo, ResultsViewerSettings.TitleBarColorMode);
            UpdateTitleBarCustomPanel();

            // --- Drag and drop ---
            SelectComboItemByTag(DragDropModeCombo, ResultsViewerSettings.DragDropMode.ToString());

            // --- Scrollbar size ---
            SelectComboItemByTag(ScrollBarSizeCombo,
                ((int)ResultsViewerSettings.ScrollBarSize).ToString());

            // --- Default sort ---
            SelectComboItemByTag(DefaultSortCombo, ResultsViewerSettings.DefaultSort.ToString());

            // --- Known-value filter dropdown mode (per field) ---
            SelectComboItemByTag(KnownModeProviderCombo, ResultsViewerSettings.GetKnownFilterMode("Provider").ToString());
            SelectComboItemByTag(KnownModeTaskNameCombo, ResultsViewerSettings.GetKnownFilterMode("TaskName").ToString());
            SelectComboItemByTag(KnownModeSourceCombo,   ResultsViewerSettings.GetKnownFilterMode("Source").ToString());

            // --- Row text size ---
            SelectComboItemByTag(RowFontSizeCombo,
                ((int)ResultsViewerSettings.RowFontSize).ToString());

            // --- Row height (density) ---
            SelectComboItemByTag(RowHeightRatioCombo,
                ResultsViewerSettings.RowHeightRatio.ToString(System.Globalization.CultureInfo.InvariantCulture));

            // --- Event payload format ---
            SelectComboItemByTag(PayloadFormatCombo, ResultsViewerSettings.EtwPayloadFormat.ToString());
            PayloadCustomTemplateBox.Text = ResultsViewerSettings.EtwPayloadCustomTemplate;
            PayloadCustomPanel.Visibility =
                ResultsViewerSettings.EtwPayloadFormat == FindNeedlePluginUtils.StructuredLog.PayloadFormat.Custom
                    ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

            // --- Levels: start from the active theme preset, then layer per-level overrides. ---
            RebuildLevelEditor();

            // --- Storage backend ---
            SelectStorageInComboBox();

            // --- Cache reuse ---
            SelectCacheReuseMode();

            // --- Indexing mode ---
            SelectIndexingMode();
            IndexTimestampsCheck.IsChecked = ResultsViewerSettings.IndexTimestampsInSearch;
            ParallelIngestCheck.IsChecked = ResultsViewerSettings.ParallelIngest;

            // --- Search submit mode ---
            SelectSearchSubmitMode();

            // --- Progressive loading ---
            StreamWhileLoadingCheck.IsChecked = ResultsViewerSettings.StreamWhileLoading;

            // --- Search progress ---
            ShowStepHistoryCheck.IsChecked = ResultsViewerSettings.ShowStepHistory;

            // --- Row tags ---
            ColorTaggedRowsCheck.IsChecked = ResultsViewerSettings.ColorTaggedRows;
            ScrollToTopOnPageChangeCheck.IsChecked = ResultsViewerSettings.ScrollToTopOnPageChange;
            ShowWelcomeIntroCheck.IsChecked = ResultsViewerSettings.ShowWelcomeIntro;
            ShowStatusBarCheck.IsChecked = ResultsViewerSettings.ShowStatusBar;

            // --- MCP server ---
            McpEnabledCheck.IsChecked = ResultsViewerSettings.McpServerEnabled;
            McpPortBox.Text = ResultsViewerSettings.McpServerPort.ToString();
            UpdateMcpStatus();

            // --- File associations (supported types are declared in the packaged manifest) ---
            FileAssocList.Text = string.Join("   ", FindNeedleUX.Services.FileAssociations.Extensions);
            OpenWithCheck.IsChecked = ResultsViewerSettings.FileOpenWithEnabled;
            ContextMenuCheck.IsChecked = ResultsViewerSettings.FileContextMenuEnabled;
            if (FindNeedleUX.Services.FileIntegration.IsPackaged)
            {
                // Packaged (Store) build: "Open with" comes from the manifest and these per-user registry
                // toggles are virtualized, so disable them and explain.
                OpenWithCheck.IsEnabled = false;
                ContextMenuCheck.IsEnabled = false;
                FileIntegrationNote.Text = "This Store-installed copy registers \"Open with\" via its package. "
                    + "Use \"Manage defaults in Windows…\" to choose FindNeedle per file type.";
            }

            // --- WPP / tracefmt TMF path ---
            TmfPathBox.Text = ResultsViewerSettings.TraceFormatSearchPath;
            SymbolSourceBox.Text = ResultsViewerSettings.SymbolSourcePath;
            SymbolPathBox.Text = ResultsViewerSettings.SymbolPath;
            FindNeedleUX.Services.Mcp.McpServerHost.StatusChanged -= OnMcpStatusChanged;
            FindNeedleUX.Services.Mcp.McpServerHost.StatusChanged += OnMcpStatusChanged;

            // --- Column defaults ---
            BuildColumnDefaultsCheckboxes();

            SettingsPathText.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FindNeedle", "viewer-settings.json");

            LevelEditor.ItemsSource = Levels;

            // Index every card so the pane search box can find + jump to a setting.
            BuildSettingsCatalog();

            // Sync the panels to the selected category. SettingsNav_SelectionChanged bails during initial
            // XAML parse (panels not built yet), so apply it now — otherwise the nav highlights a category
            // but the panels still show their XAML defaults (only Appearance visible). Defaults to
            // "All settings" so opening Settings shows everything first.
            ApplyCategory((SettingsNav.SelectedItem as NavigationViewItem)?.Tag as string ?? "all");
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

        // Iterate the REAL Level enum (Catastrophic/Error/Warning/Info/Verbose/Unknown), in severity
        // order, so this editor lists exactly the levels the result viewer colors — the same set the
        // theme presets are keyed by, so the editor and the grid stay in sync.
        foreach (var levelName in Enum.GetNames(typeof(FindNeedlePluginLib.Level)))
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

    // Each option's label is fixed; its example is rendered from the option's own format string against
    // one sample timestamp, so every row is built the same way — consistent, and never a duplicate.
    private static readonly (string tag, string label)[] TimeFormatOptions =
    {
        ("yyyy-MM-dd HH:mm:ss",     "Standard"),
        ("yyyy-MM-dd HH:mm:ss.fff", "With milliseconds"),
        ("yyyy-MM-dd HH:mm:ss zzz", "With time zone"),
        ("MMM d HH:mm:ss",          "Friendly"),
        ("HH:mm:ss",                "Time only"),
        ("HH:mm:ss.fff",            "Time only + milliseconds"),
        ("g",                       "Locale short"),
        ("G",                       "Locale long"),
        ("o",                       "ISO 8601 round-trip"),
    };

    private void PopulateTimeFormatCombo()
    {
        // Fixed sample (with ms + a non-UTC-ambiguous local kind so zzz renders an offset).
        var sample = new DateTime(2026, 3, 1, 9, 20, 1, 123, DateTimeKind.Local);
        TimeFormatCombo.Items.Clear();
        foreach (var (tag, label) in TimeFormatOptions)
        {
            string example;
            try { example = sample.ToString(tag, System.Globalization.CultureInfo.CurrentCulture); }
            catch { example = tag; }
            TimeFormatCombo.Items.Add(new ComboBoxItem { Tag = tag, Content = $"{label} — {example}" });
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

    // ----- Drag and drop -----
    private void DragDropModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (DragDropModeCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag
            && Enum.TryParse<FindNeedleUX.Services.DragDropMode>(tag, out var mode))
            ResultsViewerSettings.DragDropMode = mode;
    }

    // ----- Title bar color -----
    private void UpdateTitleBarCustomPanel()
    {
        bool custom = ResultsViewerSettings.TitleBarColorMode == "Custom";
        TitleBarCustomPanel.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        var hex = ResultsViewerSettings.TitleBarCustomColor;
        TitleBarHexText.Text = hex;
        TitleBarSwatch.Background = HexToBrushConverter.Parse(hex);
    }

    private void TitleBarModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (TitleBarModeCombo.SelectedItem is ComboBoxItem item && item.Tag is string mode)
        {
            ResultsViewerSettings.TitleBarColorMode = mode;
            UpdateTitleBarCustomPanel();
        }
    }

    private async void TitleBarColor_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ColorPicker
        {
            IsAlphaEnabled = false,
            Color = HexToBrushConverter.ParseColor(ResultsViewerSettings.TitleBarCustomColor)
        };
        var dialog = new ContentDialog
        {
            Title = "Title bar color",
            Content = picker,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var c = picker.Color;
            ResultsViewerSettings.TitleBarCustomColor = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            UpdateTitleBarCustomPanel();
        }
    }

    // ----- Scrollbar size -----
    private void ScrollBarSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (ScrollBarSizeCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag
            && double.TryParse(tag, out var size))
            ResultsViewerSettings.ScrollBarSize = size;
    }

    // ----- Default sort -----
    private void DefaultSortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (DefaultSortCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag
            && Enum.TryParse<FindNeedleUX.Services.DefaultSortMode>(tag, out var mode))
            ResultsViewerSettings.DefaultSort = mode;
    }

    // ----- Known-value filter dropdown mode (per field) -----
    private void SetKnownMode(ComboBox combo, string field)
    {
        if (_suppressEvents) return;
        if (combo.SelectedItem is ComboBoxItem item && item.Tag is string tag
            && Enum.TryParse<FindNeedleUX.Services.KnownFilterMode>(tag, out var mode))
            ResultsViewerSettings.SetKnownFilterMode(field, mode);
    }
    private void KnownModeProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => SetKnownMode(KnownModeProviderCombo, "Provider");
    private void KnownModeTaskNameCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => SetKnownMode(KnownModeTaskNameCombo, "TaskName");
    private void KnownModeSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)   => SetKnownMode(KnownModeSourceCombo, "Source");

    // ----- Row text size -----
    private void RowFontSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (RowFontSizeCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag
            && double.TryParse(tag, out var size))
            ResultsViewerSettings.RowFontSize = size;
    }

    // ----- Row height (density) -----
    private void RowHeightRatioCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (RowHeightRatioCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag
            && double.TryParse(tag, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var ratio))
            ResultsViewerSettings.RowHeightRatio = ratio;
    }

    // ----- Event payload format -----
    private void PayloadFormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (PayloadFormatCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag
            && Enum.TryParse<FindNeedlePluginUtils.StructuredLog.PayloadFormat>(tag, out var fmt))
        {
            ResultsViewerSettings.EtwPayloadFormat = fmt;
            PayloadCustomPanel.Visibility = fmt == FindNeedlePluginUtils.StructuredLog.PayloadFormat.Custom
                ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        }
    }

    private void PayloadCustomTemplate_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        ResultsViewerSettings.EtwPayloadCustomTemplate = PayloadCustomTemplateBox.Text ?? "";
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

    private void IndexTimestampsCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        ResultsViewerSettings.IndexTimestampsInSearch = IndexTimestampsCheck.IsChecked == true;
    }

    private void ParallelIngestCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        ResultsViewerSettings.ParallelIngest = ParallelIngestCheck.IsChecked == true;
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

    private void ScrollToTopOnPageChangeCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        ResultsViewerSettings.ScrollToTopOnPageChange = ScrollToTopOnPageChangeCheck.IsChecked == true;
    }

    private void ShowWelcomeIntroCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        ResultsViewerSettings.ShowWelcomeIntro = ShowWelcomeIntroCheck.IsChecked == true;
    }

    private void ShowStatusBarCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        ResultsViewerSettings.ShowStatusBar = ShowStatusBarCheck.IsChecked == true;
    }

    private void ShowStepHistoryCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        ResultsViewerSettings.ShowStepHistory = ShowStepHistoryCheck.IsChecked == true;
    }

    private void StreamWhileLoadingCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        ResultsViewerSettings.StreamWhileLoading = StreamWhileLoadingCheck.IsChecked == true;
    }

    // Loading-animation choices grouped into categories so the dropdown stays short.
    private static readonly (string cat, (string tag, string label)[] items)[] LoaderCatalog =
    {
        ("Classic",    new[] { ("Spinner", "Spinner — ring"), ("Bar", "Progress bar") }),
        ("Industrial", new[] { ("Robot", "Robot"), ("Forge", "Forge"), ("Warehouse", "Warehouse"), ("Laundromat", "Laundromat") }),
        ("Nature",     new[] { ("Greenhouse", "Greenhouse"), ("Sea", "Sea"), ("Arctic", "Arctic"), ("Bio", "Bio"), ("Zen", "Zen") }),
        ("Cosmic",     new[] { ("Cosmic", "Cosmic"), ("Crystal", "Crystal") }),
        ("Other",      new[] { ("Haunted", "Haunted"), ("Library", "Library") }),
    };

    private void PopulateLoaderCombos()
    {
        _suppressEvents = true;
        try
        {
            LoaderCategoryCombo.Items.Clear();
            foreach (var (cat, _) in LoaderCatalog)
                LoaderCategoryCombo.Items.Add(new ComboBoxItem { Content = cat, Tag = cat });

            var current = ResultsViewerSettings.LoadingAnimation;
            int catIdx = 0;
            for (int i = 0; i < LoaderCatalog.Length; i++)
                if (LoaderCatalog[i].items.Any(it => string.Equals(it.tag, current, StringComparison.OrdinalIgnoreCase)))
                { catIdx = i; break; }

            LoaderCategoryCombo.SelectedIndex = catIdx;
            PopulateItemCombo(catIdx, current);
        }
        finally { _suppressEvents = false; }
    }

    private void PopulateItemCombo(int catIdx, string selectTag)
    {
        LoaderItemCombo.Items.Clear();
        var items = LoaderCatalog[catIdx].items;
        int sel = 0;
        for (int i = 0; i < items.Length; i++)
        {
            LoaderItemCombo.Items.Add(new ComboBoxItem { Content = items[i].label, Tag = items[i].tag });
            if (string.Equals(items[i].tag, selectTag, StringComparison.OrdinalIgnoreCase)) sel = i;
        }
        LoaderItemCombo.SelectedIndex = sel;
    }

    private void LoaderCategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        int idx = LoaderCategoryCombo.SelectedIndex;
        if (idx < 0) return;
        _suppressEvents = true;
        try { PopulateItemCombo(idx, LoaderCatalog[idx].items[0].tag); }
        finally { _suppressEvents = false; }
        ApplySelectedLoaderItem();
    }

    private void LoaderItemCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        ApplySelectedLoaderItem();
    }

    private void ApplySelectedLoaderItem()
    {
        if (LoaderItemCombo.SelectedItem is ComboBoxItem it && it.Tag is string tag)
        {
            ResultsViewerSettings.LoadingAnimation = tag;
            UpdateRobotOptionsVisibility();
            if (RobotLoader.IsAnimated(tag)) ShowPreviewFrame(0);
        }
    }

    private void RobotWidthCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (RobotWidthCombo.SelectedItem is ComboBoxItem item && item.Tag is string v)
            ResultsViewerSettings.RobotWide = v == "Wide";
        ShowPreviewFrame(_previewStep);
    }

    private void UpdateRobotOptionsVisibility()
    {
        if (RobotOptionsPanel == null) return;
        var mode = ResultsViewerSettings.LoadingAnimation;
        bool animated = RobotLoader.IsAnimated(mode);
        bool bar = mode == "Bar";

        // Theme options (shape + preview) for any animated theme.
        RobotOptionsPanel.Visibility = animated ? Visibility.Visible : Visibility.Collapsed;
        RobotPreviewBox.Visibility = animated ? Visibility.Visible : Visibility.Collapsed;
        if (animated) ShowPreviewFrame(_previewStep);   // refresh art for the newly-selected theme

        SpinnerPreviewRing.Visibility = (!animated && !bar) ? Visibility.Visible : Visibility.Collapsed;
        SpinnerPreviewRing.IsActive = (!animated && !bar);

        BarPreview.Visibility = bar ? Visibility.Visible : Visibility.Collapsed;
        BarPreview.IsIndeterminate = bar;
    }

    // ----- robot preview (step through manually with Prev/Next) -----
    private void ShowPreviewFrame(int step)
    {
        if (RobotPreviewImage == null) return;
        var frames = RobotLoader.FramesFor(ResultsViewerSettings.LoadingAnimation);
        int n = frames.Length;
        _previewStep = ((step % n) + n) % n; // wrap both directions
        RobotPreviewImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
            new Uri(RobotLoader.Uri(_previewStep)));
        RobotPreviewStepText.Text = $"Step {_previewStep + 1}/{n}: {frames[_previewStep]}";
    }

    private void RobotPreviewPrev_Click(object sender, RoutedEventArgs e) => ShowPreviewFrame(_previewStep - 1);
    private void RobotPreviewNext_Click(object sender, RoutedEventArgs e) => ShowPreviewFrame(_previewStep + 1);

    // ----- MCP server -----
    private void McpEnabledCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        // Setter broadcasts Changed → McpServerHost starts/stops accordingly.
        ResultsViewerSettings.McpServerEnabled = McpEnabledCheck.IsChecked == true;
    }

    // (Field-extraction enrichment toggle moved to the Rules → Field extraction tab.)

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

    private void SymbolSourceBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        ResultsViewerSettings.SymbolSourcePath = SymbolSourceBox.Text?.Trim() ?? "";
    }

    private void SymbolPathBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        ResultsViewerSettings.SymbolPath = SymbolPathBox.Text?.Trim() ?? "";
    }

    private void BrowseSymbolSource_Click(object sender, RoutedEventArgs e)
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(WindowUtil.GetMainWindow());
        var path = Win32FileDialog.PickFolder(hWnd);
        if (path != null)
        {
            SymbolSourceBox.Text = path;
            ResultsViewerSettings.SymbolSourcePath = path;
        }
    }

    private async void BuildTmfs_Click(object sender, RoutedEventArgs e)
    {
        // Persist current text first (in case the user didn't tab out of the boxes).
        ResultsViewerSettings.SymbolSourcePath = SymbolSourceBox.Text?.Trim() ?? "";
        ResultsViewerSettings.SymbolPath = SymbolPathBox.Text?.Trim() ?? "";

        BuildTmfsButton.IsEnabled = false;
        BuildTmfsStatus.Text = "Building TMFs from symbols…";
        var source = ResultsViewerSettings.SymbolSourcePath;
        var symPath = ResultsViewerSettings.SymbolPath;
        (int count, string log) result = (0, "");
        try
        {
            result = await System.Threading.Tasks.Task.Run(() => WppSymbolResolver.BuildTmfs(source, symPath));
        }
        catch (Exception ex) { result = (0, ex.Message); }

        // Re-apply env so the new TMF cache is on the search path immediately.
        TraceFormatConfig.Apply();

        BuildTmfsButton.IsEnabled = true;
        BuildTmfsStatus.Text = $"{result.count} TMF(s) in cache";
        _ = new ContentDialog
        {
            Title = "Build TMFs from symbols",
            Content = new ScrollViewer
            {
                MaxHeight = 380,
                Content = new TextBlock { Text = result.log, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 12, IsTextSelectionEnabled = true, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
            },
            CloseButtonText = "OK",
            XamlRoot = this.XamlRoot,
        }.ShowAsync();
    }

    private void OnMcpStatusChanged() => DispatcherQueue.TryEnqueue(UpdateMcpStatus);

    private void UpdateMcpStatus()
    {
        if (McpStatusText == null) return;
        McpStatusText.Text = "Status: " + FindNeedleUX.Services.Mcp.McpServerHost.Status;
    }

    // ----- File associations -----
    private void OpenWithCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        bool on = OpenWithCheck.IsChecked == true;
        ResultsViewerSettings.FileOpenWithEnabled = on;
        FindNeedleUX.Services.FileIntegration.SetOpenWith(on);
    }

    private void ContextMenuCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        bool on = ContextMenuCheck.IsChecked == true;
        ResultsViewerSettings.FileContextMenuEnabled = on;
        FindNeedleUX.Services.FileIntegration.SetContextMenu(on);
    }

    // ----- Diagnostics: send app logs to dev -----
    private async void SendLogs_Click(object sender, RoutedEventArgs e)
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(WindowUtil.GetMainWindow());
        var path = Win32FileDialog.SaveFile(hWnd,
            FindNeedleUX.Services.DiagnosticsBundle.SuggestedFileName(),
            new (string, string)[] { ("Zip archive", "*.zip") }, ".zip");
        if (path == null) return;

        SendLogsButton.IsEnabled = false;
        SendLogsStatus.Text = "Gathering logs…";
        try
        {
            int count = await System.Threading.Tasks.Task.Run(
                () => FindNeedleUX.Services.DiagnosticsBundle.Create(path));
            SendLogsStatus.Text = $"Saved {count} item(s) to the .zip.";
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { /* reveal is best-effort */ }
        }
        catch (Exception ex)
        {
            SendLogsStatus.Text = "Failed: " + ex.Message;
        }
        finally { SendLogsButton.IsEnabled = true; }
    }

    private async void ManageDefaultApps_Click(object sender, RoutedEventArgs e)
    {
        // Windows won't let an app silently set itself as the default handler (anti-hijack), so we
        // deep-link to the Default apps page where the user can choose Find Needle per file type.
        try { await global::Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:defaultapps")); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"LaunchUriAsync failed: {ex.Message}"); }
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
