using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.WinUI.UI.Controls;
using FindNeedleUX;
using FindNeedleUX.Pages.NativeResultViewer;
using FindNeedleUX.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace FindNeedleUX.Pages
{

/// <summary>
/// Native WinUI 3 result viewer page. Mirrors the feature set of the WebView2/DataTables viewer:
///   global search, per-column filters, time-range filter, level filter, level chips with counts,
///   per-level color customization, column visibility, sortable/reorderable/resizable columns
///   (DataGrid built-ins), row coloring by Level, row details with Copy as JSON, Help dialog,
///   Ctrl+F focus search, Esc close popover, Export CSV.
/// </summary>
public sealed partial class NativeResultsPage : Page, FindNeedleUX.Services.Mcp.IMcpViewerController
{
    private NativeResultsPageViewModel ViewModel { get; } = new();

    // Pre-built O(1) lookup from Level name -> LevelEntry, used by LoadingRow on every row that
    // gets virtualized in. Rebuilt whenever ViewModel.Levels changes (after load, after theme
    // change, after color edit).
    private readonly Dictionary<string, LevelEntry> _levelLookup =
        new(StringComparer.OrdinalIgnoreCase);

    // ----- Row tags -----
    // User-applied marks (right-click → Tag). Keyed by row *content* so a tag survives paging,
    // sorting and re-filtering (the grid re-materializes LogLine objects each page, and the row
    // Index is positional, not a stable identity). In-memory for the viewer session.
    // Bold/opaque — these paint a solid left stripe on the row (distinct from the pale, translucent
    // level row-tints), so a tag reads as its own marker rather than blending into the level color.
    private static readonly (string Name, string Hex)[] TagOptions =
    {
        ("Important", "#E53935"), // red
        ("Question",  "#FB8C00"), // orange
        ("Resolved",  "#43A047"), // green
        ("Note",      "#3949AB"), // indigo
        ("UML",       "#00897B"), // teal — auto-applied to rows that fed a UML diagram
    };
    private static readonly Dictionary<string, string> _tagColors =
        TagOptions.ToDictionary(t => t.Name, t => t.Hex, StringComparer.OrdinalIgnoreCase);
    // A row tag: a category (one of TagOptions, drives glyph color) plus an optional free-text note.
    private readonly record struct RowTag(string Name, string Text);

    // Keyed by the row's stable LogLine.RowId (SQLite Id for disk-backed searches, load-order
    // position for in-memory) so a tag survives paging, sorting and re-filtering, and so the MCP
    // bridge can tag/clear (and set the note) by the same id it hands an agent. In-memory for the
    // viewer session.
    private readonly Dictionary<long, RowTag> _rowTags = new();
    private bool _colorTaggedRows; // optional: also tint the whole row with the tag color

    public NativeResultsPage()
    {
        this.InitializeComponent();

        // Make the result-grid scrollbar as thick as the user configured. Override the WinUI
        // ScrollBarSize resource at page scope *before* the DataGrid realizes its template so its
        // scrollbars pick it up; MouseIndicator keeps the (thicker) bar shown rather than collapsing
        // to the thin auto-hide line. Re-applied live via OnSettingsChanged for an open viewer.
        ApplyScrollBarSize();
        ApplyRowFontSize();

        // Keep this page instance alive across Frame navigations. First switch from another
        // viewer still pays the construction cost (XAML + DataGrid init), but every subsequent
        // switch reuses the cached instance and feels instant. The page's Loaded event still
        // fires each time it's added back to the visual tree, so LoadResultsAsync re-runs and
        // picks up any new search results.
        this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;

        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
        KeyDown += OnPageKeyDown;

        foreach (var col in ViewModel.Columns)
            col.VisibilityChanged += OnColumnVisibilityChanged;

        // Re-evaluate the adaptive search-submit mode whenever the row count changes. A streaming
        // search opens the viewer before all rows have landed, so the count grows after load; without
        // this, a huge log could stay in (laggy) live-search mode because the seed ran while the
        // count was still small.
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Debounce for lazy index building: only act after the user pauses typing, so live search
        // keystrokes don't kick off the (expensive) index build on the 3rd character.
        _lazyIndexTimer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _lazyIndexTimer.Tick += LazyIndexTimer_Tick;

        // Debounce for live search: only run a search once typing pauses, so a burst of keystrokes
        // collapses into one (off-UI-thread) search instead of one per character.
        _searchDebounceTimer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchDebounceTimer.Tick += (_, _) => { _searchDebounceTimer.Stop(); _ = RunSearchAsync(); };
    }

    private readonly Microsoft.UI.Xaml.DispatcherTimer _lazyIndexTimer;
    private bool _lazyPromptShown; // don't re-prompt for the >30s warning every keystroke

    private readonly Microsoft.UI.Xaml.DispatcherTimer _searchDebounceTimer;
    private System.Threading.CancellationTokenSource _searchCts; // cancels the in-flight search

    // ----- Adaptive search submit (live keystrokes vs Enter-to-search) -----
    // Searching runs synchronously on the UI thread, so on a multi-million-row log a per-keystroke
    // search blocks typing. Auto mode keeps live search for small/fast logs but flips to
    // Enter-to-search once a search is slow (or the log is large), and flips back once it's fast.
    // The decision logic lives in (testable) SearchSubmitPolicy; the page just holds the state.
    private SearchSubmitMode _searchSubmitMode = SearchSubmitMode.Auto;
    private bool _autoEnterActive;             // in Auto: currently requiring Enter?

    /// <summary>Whether the current mode means "don't search until Enter".</summary>
    private bool RequireEnterToSearch() => SearchSubmitPolicy.RequireEnter(_searchSubmitMode, _autoEnterActive);

    /// <summary>Auto-tag rows that fed a UML diagram in the last search (row id → matching rule),
    /// so "which event-log lines did the diagram use" is visible right in the results grid. Never
    /// clobbers a user's own tag on the same row.</summary>
    private void SeedUmlRowTags()
    {
        try
        {
            var q = MiddleLayerService.SearchQueryUX.CurrentQuery as FindPluginCore.Searching.NuSearchQuery;
            var matched = q?.UmlMatchedRows;
            if (matched == null) return;
            foreach (var m in matched)
            {
                if (_rowTags.ContainsKey(m.RowId)) continue;
                _rowTags[m.RowId] = new RowTag("UML", string.IsNullOrWhiteSpace(m.RuleName) ? "used in diagram" : $"UML: {m.RuleName}");
            }
        }
        catch { /* tagging is best-effort */ }

        UpdateOutputFilesBanner();
    }

    /// <summary>Show a banner when this search produced rule output files (diagrams/exports), with a
    /// shortcut to the Processor Output page where they're listed and rendered.</summary>
    private void UpdateOutputFilesBanner()
    {
        try
        {
            var q = MiddleLayerService.SearchQueryUX.CurrentQuery as FindPluginCore.Searching.NuSearchQuery;
            var files = new List<string>();
            if (MiddleLayerService.LastRuleOutputFiles != null) files.AddRange(MiddleLayerService.LastRuleOutputFiles);
            if (q?.GeneratedRuleOutputFiles != null) files.AddRange(q.GeneratedRuleOutputFiles);
            var n = files.Where(System.IO.File.Exists)
                         .Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (n > 0)
            {
                var diagrams = q?.GeneratedDiagramUsages?.Count ?? 0;
                OutputFilesBannerText.Text = diagrams > 0
                    ? $"{n} output file(s) generated, including {diagrams} diagram(s)."
                    : $"{n} output file(s) generated by rules.";
                OutputFilesBanner.Visibility = Visibility.Visible;
            }
            else
            {
                OutputFilesBanner.Visibility = Visibility.Collapsed;
            }
        }
        catch { OutputFilesBanner.Visibility = Visibility.Collapsed; }
    }

    private void ViewOutputFiles_Click(object sender, RoutedEventArgs e)
    {
        try { this.Frame?.Navigate(typeof(FindNeedleUX.Pages.ProcessorOutputPage)); } catch { }
    }

    private void DismissOutputFilesBanner_Click(object sender, RoutedEventArgs e)
    {
        OutputFilesBanner.Visibility = Visibility.Collapsed;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        FindNeedlePluginLib.FlowProgress.Begin(FindNeedlePluginLib.FlowPhase.OpenViewer);
        FindNeedlePluginLib.FlowProgress.Updated -= OnFlowProgress;
        FindNeedlePluginLib.FlowProgress.Updated += OnFlowProgress;

        // Subscribe to settings changes per Loaded cycle. With NavigationCacheMode.Required,
        // the page survives navigation; Unloaded unsubscribes so a backgrounded viewer doesn't
        // react to settings changes, and re-subscribe here when it comes back to the foreground.
        // Idempotent: subtract-then-add is safe even if we accidentally double-subscribe.
        ResultsViewerSettings.Changed -= OnSettingsChanged;
        ResultsViewerSettings.Changed += OnSettingsChanged;

        // Re-evaluate the decode banner when a streaming load finishes — by then the new file's stats
        // are captured, so a stale "missing symbols" warning from a previous bad file clears (or the
        // new file's own warning appears).
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Apply persisted prefs BEFORE rendering so the first paint already has the user's choices.
        ApplyPersistedSettings();
        ApplyPersistedColumnDefaults();
        ApplyFiltersToggleState(ResultsViewerSettings.FiltersExpanded);
        ApplyDetailsMode(ResultsViewerSettings.DetailsMode);
        ApplyPersistedPageSize();
        FindNeedlePluginLib.FlowProgress.Begin(FindNeedlePluginLib.FlowPhase.LoadFirstPage);
        await ViewModel.LoadResultsCommand.ExecuteAsync(null);
        // After load, re-apply persisted level color overrides — LoadResultsAsync() repopulates
        // ViewModel.Levels from the theme defaults, which would clobber overrides otherwise.
        ApplyPersistedLevelOverrides();
        SeedUmlRowTags();
        LoadingOverlay.Visibility = Visibility.Collapsed;
        UpdateEmptyState();
        ApplyAllColumnVisibility();
        RebindGrid();
        // Reflect the initial sort (from the Default sort preference) on the column headers.
        SyncSortArrowsFromViewModel();
        // Now that rows are loaded, fill the known-value dropdowns (if shown).
        if (ShowKnownToggle?.IsChecked == true) _ = PopulateKnownFilterCombosAsync();

        // Tell MainWindow to hide the pre-nav spinner now that we're fully rendered.
        MainWindowActions.HideNavigationSpinner();

        // The search→view flow is fully done — clear the "Step X of N" status.
        FindNeedlePluginLib.FlowProgress.Updated -= OnFlowProgress;
        FindNeedlePluginLib.FlowProgress.Complete();

        // Reflect any in-flight background index build (Background mode starts it after the search),
        // and let a fresh load prompt again for the >30s warning.
        _lazyPromptShown = false;
        MiddleLayerService.StateChanged -= OnMiddleLayerStateChanged;
        MiddleLayerService.StateChanged += OnMiddleLayerStateChanged;
        MiddleLayerService.WorkspaceCleared -= OnWorkspaceCleared;
        MiddleLayerService.WorkspaceCleared += OnWorkspaceCleared;
        UpdateIndexingIndicator();

        // Now that the row count is known, seed the adaptive search-submit mode.
        RefreshSearchSubmitMode();

        // Explain an empty/partial result (e.g. a WPP ETL whose symbols are missing) instead of
        // leaving the user staring at a blank grid.
        UpdateDecodeBanner();
        UpdateStreamingIcon();

        // Become the live view the MCP bridge reads/drives. Last-loaded viewer wins.
        FindNeedleUX.Services.Mcp.McpViewerBridge.Instance.UiDispatcher ??= DispatcherQueue;
        FindNeedleUX.Services.Mcp.McpViewerBridge.Instance.RegisterViewer(this);

        // The grid's scrollbars exist in the visual tree now, so push the configured thickness onto
        // them (the constructor only set the resource, before the template realized).
        ApplyScrollBarSize();
        ApplyRowFontSize();

        UpdateRuleFilterToggleState();
        UpdateSourcesButtonState();
    }

    /// <summary>Enable the "Rule filter" toggle only when the last search applied rules and isn't still
    /// streaming, and reflect whether the filtered view is currently active.</summary>
    private void UpdateRuleFilterToggleState()
    {
        if (RuleFilterToggle == null) return;
        bool hasRules = (MiddleLayerService.LastRuleProcessors?.Count ?? 0) > 0;
        RuleFilterToggle.IsEnabled = hasRules && !ViewModel.IsStreaming;
        RuleFilterToggle.IsChecked = ViewModel.RuleFilterActive;
    }

    private async void RuleFilterToggle_Click(object sender, RoutedEventArgs e)
    {
        bool on = RuleFilterToggle.IsChecked == true;
        var ruleFiles = MiddleLayerService.LastRuleProcessors?
            .Select(p => p.RulesFilePath)
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new System.Collections.Generic.List<string>();

        if (on && ruleFiles.Count == 0) { RuleFilterToggle.IsChecked = false; return; }

        RuleFilterToggle.IsEnabled = false;
        LoadingOverlay.Visibility = Visibility.Visible;
        try
        {
            int kept = await ViewModel.SetRuleViewFilterAsync(on, ruleFiles);
            // No exclude/include rules among the active set — nothing to filter; revert the toggle.
            if (on && kept < 0) RuleFilterToggle.IsChecked = false;
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        UpdateEmptyState();
            RuleFilterToggle.IsEnabled = (MiddleLayerService.LastRuleProcessors?.Count ?? 0) > 0;
            RebindGrid();
        }
    }

    /// <summary>
    /// Reload results from the current search storage. Needed when the viewer is already the active
    /// page: with NavigationCacheMode.Required, re-navigating to it won't re-fire Loaded, so a fresh
    /// search (e.g. "Decode anyway") would otherwise keep showing the old, empty result set.
    /// </summary>
    public async System.Threading.Tasks.Task ReloadResultsAsync()
    {
        // A new file/search is replacing the current results — clear any filters from the previous file
        // so the new content isn't hidden by them.
        ResetFiltersForNewFile();
        LoadingOverlay.Visibility = Visibility.Visible;
        await ViewModel.LoadResultsCommand.ExecuteAsync(null);
        ApplyPersistedLevelOverrides();
        SeedUmlRowTags();
        LoadingOverlay.Visibility = Visibility.Collapsed;
        UpdateEmptyState();
        ApplyAllColumnVisibility();
        RebindGrid();
        // Reflect the initial sort (from the Default sort preference) on the column headers.
        SyncSortArrowsFromViewModel();
        // Now that rows are loaded, fill the known-value dropdowns (if shown).
        if (ShowKnownToggle?.IsChecked == true) _ = PopulateKnownFilterCombosAsync();
        RefreshSearchSubmitMode();
        UpdateDecodeBanner();
        UpdateStreamingIcon();
        UpdateSourcesButtonState();
    }

    /// <summary>Pick the inline progressive-loading indicator: the small animated theme gif, or a
    /// ProgressRing when the loader is the plain Spinner/Bar.</summary>
    /// <summary>Clear the filter inputs + VM filters when a new file loads. Order matters: reset the VM
    /// first so clearing each input sees "no change" and doesn't kick off a search.</summary>
    private void ResetFiltersForNewFile()
    {
        ViewModel.ClearFiltersNoReload();
        if (SearchBox != null) SearchBox.Text = "";
        if (ProviderFilterBox != null) ProviderFilterBox.Text = "";
        if (TaskNameFilterBox != null) TaskNameFilterBox.Text = "";
        if (MessageFilterBox != null) MessageFilterBox.Text = "";
        if (SourceFilterBox != null) SourceFilterBox.Text = "";
        if (LevelFilterCombo != null) LevelFilterCombo.SelectedItem = null;
        if (FromDatePicker != null) FromDatePicker.Date = null;
        if (ToDatePicker != null) ToDatePicker.Date = null;
        if (FromTimePicker != null) FromTimePicker.SelectedTime = null;
        if (ToTimePicker != null) ToTimePicker.SelectedTime = null;
    }

    private void UpdateStreamingIcon()
    {
        var mode = ResultsViewerSettings.LoadingAnimation;
        if (RobotLoader.IsAnimated(mode))
        {
            StreamGif.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                new Uri(RobotLoader.SmallUri(0, mode)));
            StreamGif.Visibility = Visibility.Visible;
            StreamRing.Visibility = Visibility.Collapsed;
            StreamRing.IsActive = false;
        }
        else
        {
            StreamGif.Visibility = Visibility.Collapsed;
            StreamRing.Visibility = Visibility.Visible;
            StreamRing.IsActive = true;
        }
    }

    /// <summary>
    /// Show/hide the warning banner that explains why results are empty or partial (currently:
    /// WPP ETL with missing symbols/TMFs). Pulled from the last search's decode stats.
    /// </summary>
    /// <summary>Show a centered empty-state over the grid when a finished load has zero rows, so the user
    /// sees *why* it's blank instead of an empty grid. Distinguishes "no rows at all" (empty source /
    /// nothing decoded) from "filters hid everything" (offers Clear filters). No-op while loading.</summary>
    private void UpdateEmptyState()
    {
        if (EmptyOverlay == null) return; // during initial parse
        bool loading = LoadingOverlay.Visibility == Visibility.Visible || ViewModel.IsStreaming;
        if (loading || ViewModel.TotalFilteredCount > 0)
        {
            EmptyOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        // "Filters hid everything" only when filters are actually active — not merely because rows
        // exist. (TotalCount is set before TotalFilteredCount during load, so inferring it from
        // TotalCount>0 would wrongly flash the filter message on an unfiltered, still-loading view.)
        bool filtersHidAll = ViewModel.HasActiveFilter() && ViewModel.TotalCount > 0;
        EmptyOverlayClearFilters.Visibility = filtersHidAll ? Visibility.Visible : Visibility.Collapsed;
        if (filtersHidAll)
        {
            EmptyOverlayTitle.Text = "No matching rows";
            EmptyOverlayText.Text = $"All {ViewModel.TotalCount:N0} loaded rows are hidden by the current filters.";
        }
        else
        {
            EmptyOverlayTitle.Text = "No results";
            var warn = MiddleLayerService.GetDecodeWarning();
            EmptyOverlayText.Text = warn?.headline is { Length: > 0 } h
                ? h + " — nothing was decoded from this source."
                : "This search returned no rows — the source was empty, or nothing matched/decoded.";
        }
        EmptyOverlay.Visibility = Visibility.Visible;
    }

    // Full detail text from the last decode warning — used by the copy button.
    private string? _decodeBannerCopyText;

    private void UpdateDecodeBanner()
    {
        var warn = MiddleLayerService.GetDecodeWarning();
        if (warn == null)
        {
            DecodeBanner.IsOpen = false;
            DecodeBanner.Visibility = Visibility.Collapsed;
            _decodeBannerCopyText = null;
            return;
        }
        DecodeBanner.Title = warn.Value.headline;
        DecodeBanner.Message = warn.Value.detail;
        DecodeBanner.Severity = warn.Value.hardFailure
            ? Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error
            : Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning;
        // "Decode anyway" only makes sense when ~nothing decoded — a partial decode already shows rows.
        DecodeBannerForce.Visibility = warn.Value.hardFailure ? Visibility.Visible : Visibility.Collapsed;
        // Build copy text: if there are specific GUIDs/TMF identifiers, put them first so they're
        // easy to paste into a symbol server query; follow with the full detail for context.
        _decodeBannerCopyText = string.IsNullOrEmpty(warn.Value.missingTmfs)
            ? warn.Value.detail
            : $"Missing TMF GUIDs: {warn.Value.missingTmfs}\n\n{warn.Value.detail}";
        DecodeBanner.Visibility = Visibility.Visible;
        DecodeBanner.IsOpen = true;
    }

    private void DecodeBannerCopy_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_decodeBannerCopyText)) return;
        var dp = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(_decodeBannerCopyText);
        global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
        // Brief button label change so the user knows it worked.
        if (sender is Button btn)
        {
            btn.Content = "Copied!";
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromSeconds(2);
            timer.IsRepeating = false;
            timer.Tick += (_, _) => { btn.Content = "Copy"; timer.Stop(); };
            timer.Start();
        }
    }

    private void DecodeBannerAction_Click(object sender, RoutedEventArgs e)
    {
        // Jump straight to the WPP symbol settings so the fix is one click away.
        MainWindowActions.NavigateToResultsViewerSettings();
    }

    private void DecodeAnyway_Click(object sender, RoutedEventArgs e)
    {
        // Force a full decode (no pre-scan/fail-fast) and reopen. MainWindow drives this through the
        // standard search-progress spinner, so the user gets live status + a cancel button.
        DecodeBanner.IsOpen = false;
        DecodeBanner.Visibility = Visibility.Collapsed;
        MainWindowActions.RerunWithFullDecode();
    }

    /// <summary>
    /// Pull the saved column-visibility defaults into the VM's <c>Columns</c> collection. The
    /// per-column auto-hide pass (run inside <c>LoadResultsAsync</c>) may further hide columns
    /// where 100% of values are empty, but won't auto-enable a column the user disabled here.
    /// </summary>
    private void ApplyPersistedColumnDefaults()
    {
        var prefs = ResultsViewerSettings.ColumnVisibility;
        foreach (var col in ViewModel.Columns)
            if (prefs.TryGetValue(col.Name, out var v)) col.IsVisible = v;
    }

    private bool _filtersExpanded = true;
    private FilterDock _filterDock = FilterDock.Top;

    private void ApplyFiltersToggleState(bool expanded)
    {
        _filtersExpanded = expanded;
        FiltersToggle.IsChecked = expanded;
        FiltersToggleGlyph.Text = expanded ? "▾" : "▸";
        RefreshFilterLayout();
    }

    // ----- Filter pane docking (Top / Left) -----
    private void FilterDockTop_Click(object sender, RoutedEventArgs e)  => SetFilterDock(FilterDock.Top);
    private void FilterDockLeft_Click(object sender, RoutedEventArgs e) => SetFilterDock(FilterDock.Left);

    private void SetFilterDock(FilterDock dock)
    {
        _filterDock = dock;
        RefreshFilterLayout();
        ResultsViewerSettings.FilterDock = dock; // persists + broadcasts (other open viewers re-lay-out)
    }

    /// <summary>
    /// Place the single FiltersPanel in the active host (top row or left column), switch its rows
    /// between horizontal (top) and vertical (left), and reflect the current expand/dock state in the
    /// toolbar. Re-parenting keeps the same control instances, so bindings/handlers stay intact.
    /// </summary>
    private void RefreshFilterLayout()
    {
        if (FiltersPanel == null) return; // not yet loaded
        bool left = _filterDock == FilterDock.Left;

        // Move FiltersPanel into the host for the current dock (detach from the other first).
        if (left && LeftFilterHost.Content != FiltersPanel)
        {
            TopFilterHost.Content = null;
            LeftFilterHost.Content = FiltersPanel;
        }
        else if (!left && TopFilterHost.Content != FiltersPanel)
        {
            LeftFilterHost.Content = null;
            TopFilterHost.Content = FiltersPanel;
        }

        // Rows stack vertically when docked left (narrow column), horizontally when on top.
        var orientation = left ? Orientation.Vertical : Orientation.Horizontal;
        if (TimeRowPanel   != null) TimeRowPanel.Orientation   = orientation;
        if (FilterRowPanel != null) FilterRowPanel.Orientation = orientation;
        if (LevelsRowPanel != null) LevelsRowPanel.Orientation = orientation;

        // Left dock: let the inputs fill the column (fixed widths leave a ragged right edge in a
        // narrow vertical stack). Top dock: restore the compact fixed widths for the horizontal row.
        void Field(Control c, double topWidth)
        {
            if (c == null) return;
            c.HorizontalAlignment = left ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            c.Width = left ? double.NaN : topWidth;
        }
        Field(FromDatePicker, 160); Field(ToDatePicker, 160);
        Field(ProviderFilterBox, 120); Field(TaskNameFilterBox, 140);
        Field(MessageFilterBox, 220);  Field(SourceFilterBox, 140);
        Field(LevelFilterCombo, 140);
        if (TimeRangeArrow != null) // the "→" reads sideways between stacked pickers
            TimeRangeArrow.Visibility = left ? Visibility.Collapsed : Visibility.Visible;

        // Host visibility follows the expand state; the inactive host stays collapsed.
        FiltersPanel.Visibility  = _filtersExpanded ? Visibility.Visible : Visibility.Collapsed;
        LeftFilterHost.Visibility = (left  && _filtersExpanded) ? Visibility.Visible : Visibility.Collapsed;
        TopFilterHost.Visibility  = (!left && _filtersExpanded) ? Visibility.Visible : Visibility.Collapsed;

        // Toolbar reflection.
        if (DockTopItem  != null) DockTopItem.IsChecked  = !left;
        if (DockLeftItem != null) DockLeftItem.IsChecked = left;
    }

    private DetailsMode _detailsMode = DetailsMode.Inrow;

    /// <summary>
    /// Applies one of the three row-detail modes (mutually exclusive):
    ///   Inrow       — DataGrid expands the row inline (RowDetailsTemplate) on selection.
    ///   BottomPanel — inline expand off; a persistent panel below the grid shows the selection.
    ///   Popup       — inline expand off, no panel; double-clicking a row opens a details dialog.
    /// </summary>
    private void ApplyDetailsMode(DetailsMode mode)
    {
        _detailsMode = mode;

        DetailsPanel.Visibility = mode == DetailsMode.BottomPanel ? Visibility.Visible : Visibility.Collapsed;
        if (mode == DetailsMode.BottomPanel)
            DetailsPanel.Height = ResultsViewerSettings.DetailsPanelHeight; // clamped in the setter

        // Inline row expand only in Inrow mode (otherwise the same data would show in two places).
        ResultsGrid.RowDetailsVisibilityMode = mode == DetailsMode.Inrow
            ? DataGridRowDetailsVisibilityMode.VisibleWhenSelected
            : DataGridRowDetailsVisibilityMode.Collapsed;

        if (mode == DetailsMode.BottomPanel) RefreshDetailsPanel();

        // Reflect in the toolbar.
        if (DetailsInrowItem != null) DetailsInrowItem.IsChecked = mode == DetailsMode.Inrow;
        if (DetailsPanelItem != null) DetailsPanelItem.IsChecked = mode == DetailsMode.BottomPanel;
        if (DetailsPopupItem != null) DetailsPopupItem.IsChecked = mode == DetailsMode.Popup;
    }

    private void SetDetailsMode(DetailsMode mode)
    {
        ApplyDetailsMode(mode);
        ResultsViewerSettings.DetailsMode = mode;
    }

    private void DetailsModeInrow_Click(object sender, RoutedEventArgs e)       => SetDetailsMode(DetailsMode.Inrow);
    private void DetailsModeBottomPanel_Click(object sender, RoutedEventArgs e) => SetDetailsMode(DetailsMode.BottomPanel);
    private void DetailsModePopup_Click(object sender, RoutedEventArgs e)       => SetDetailsMode(DetailsMode.Popup);

    private async void ResultsGrid_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (_detailsMode != DetailsMode.Popup) return;
        if (ResultsGrid.SelectedItem is LogLine line) await ShowRowPopupAsync(line);
    }

    private async System.Threading.Tasks.Task ShowRowPopupAsync(LogLine line)
    {
        // Same per-field grid as the bottom panel, plus a row-copy bar — kept in-content so the dialog
        // stays open after copying. Opens full-size so a huge Message has room (ContentDialog can't be
        // user-resized).
        var grid = new Grid();
        PopulateDetailsGrid(grid, line);

        var bar = RowCopyBar(line);

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(bar);
        content.Children.Add(new ScrollViewer
        {
            Content = grid,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });

        var dialog = new ContentDialog
        {
            Title = "Log entry details",
            FullSizeDesired = true,
            Content = content,
            CloseButtonText = "Close",
            XamlRoot = this.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    /// <summary>A "Copy as JSON / CSV / XML" button row for one row (whole-row copy).</summary>
    private StackPanel RowCopyBar(LogLine line)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        Button Make(string label, Func<LogLine, string> fmt)
        {
            var b = new Button { Content = label, FontSize = 12 };
            b.Click += (_, __) => CopyToClipboard(fmt(line));
            return b;
        }
        bar.Children.Add(Make("Copy as JSON", RowAsJson));
        bar.Children.Add(Make("Copy as CSV", RowAsCsv));
        bar.Children.Add(Make("Copy as XML", RowAsXml));
        return bar;
    }

    // ----- Details panel resize -----
    //
    // Custom drag-to-resize since WinUI 3 doesn't ship a built-in GridSplitter and pulling in
    // CommunityToolkit.Sizers for this one control isn't worth the dependency. The grip
    // captures the pointer on press, tracks Y deltas, and rewrites DetailsPanel.Height
    // (clamped). On release we save the height so it persists across sessions.
    private bool _detailsResizing;
    private double _detailsResizeStartY;
    private double _detailsResizeStartHeight;

    private void DetailsPanelResizer_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        try { ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.SizeNorthSouth); }
        catch { /* not all hosts allow cursor changes */ }
    }

    private void DetailsPanelResizer_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_detailsResizing) return; // keep the resize cursor while dragging
        try { ProtectedCursor = null; } catch { }
    }

    private void DetailsPanelResizer_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is not UIElement el) return;
        var pt = e.GetCurrentPoint(this);
        if (!pt.Properties.IsLeftButtonPressed) return;
        _detailsResizing = true;
        _detailsResizeStartY = pt.Position.Y;
        _detailsResizeStartHeight = double.IsNaN(DetailsPanel.Height)
            ? DetailsPanel.ActualHeight
            : DetailsPanel.Height;
        el.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void DetailsPanelResizer_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_detailsResizing) return;
        var currentY = e.GetCurrentPoint(this).Position.Y;
        var deltaUp = _detailsResizeStartY - currentY; // dragging up = grow the panel
        var newHeight = _detailsResizeStartHeight + deltaUp;
        if (newHeight < ResultsViewerSettings.MinDetailsPanelHeight)
            newHeight = ResultsViewerSettings.MinDetailsPanelHeight;
        if (newHeight > ResultsViewerSettings.MaxDetailsPanelHeight)
            newHeight = ResultsViewerSettings.MaxDetailsPanelHeight;
        DetailsPanel.Height = newHeight;
        e.Handled = true;
    }

    private void DetailsPanelResizer_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_detailsResizing) return;
        _detailsResizing = false;
        if (sender is UIElement el) el.ReleasePointerCapture(e.Pointer);
        try { ProtectedCursor = null; } catch { }
        // Persist whatever height the user settled on.
        ResultsViewerSettings.DetailsPanelHeight = DetailsPanel.Height;
        e.Handled = true;
    }

    private void DetailsPanelResizer_PointerCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_detailsResizing)
        {
            _detailsResizing = false;
            try { ProtectedCursor = null; } catch { }
            ResultsViewerSettings.DetailsPanelHeight = DetailsPanel.Height;
        }
    }

    private void ApplyPersistedPageSize()
    {
        var size = ResultsViewerSettings.PageSize;
        ViewModel.PageSize = size;
        foreach (var item in PageSizeCombo.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string tag && int.TryParse(tag, out var n) && n == size)
            {
                PageSizeCombo.SelectedItem = item;
                return;
            }
        }
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        ResultsViewerSettings.Changed -= OnSettingsChanged;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        MiddleLayerService.StateChanged -= OnMiddleLayerStateChanged;
        MiddleLayerService.WorkspaceCleared -= OnWorkspaceCleared;
        FindNeedlePluginLib.FlowProgress.Updated -= OnFlowProgress;
        _lazyIndexTimer.Stop();
        ViewModel.DetachFromStreaming();
        FindNeedleUX.Services.Mcp.McpViewerBridge.Instance.UnregisterViewer(this);
    }

    /// <summary>Show the unified "Step X of N · …" flow status in the page's loading overlay.</summary>
    private void OnFlowProgress(string label, int step, int total)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!string.IsNullOrEmpty(label)) LoadingOverlayText.Text = label;
        });
    }

    // ----- Deferred search-index (lazy/background) UX -----

    private void OnMiddleLayerStateChanged() => UpdateIndexingIndicator();

    /// <summary>The workspace was cleared — empty the grid. Runs synchronously when already on the UI
    /// thread (the normal case: menu + MCP both clear on the UI thread) so the source detaches before
    /// the cleared storage is disposed.</summary>
    private void OnWorkspaceCleared()
    {
        void Reset()
        {
            ViewModel.ResetToEmpty();
            RebindGrid();
            UpdateDecodeBanner();
            UpdateStreamingIcon();
            UpdateEmptyState();
        }
        if (DispatcherQueue.HasThreadAccess) Reset();
        else DispatcherQueue.TryEnqueue(Reset);
    }

    /// <summary>Show/refresh the "building search index… N%" indicator from the current build state.</summary>
    private void UpdateIndexingIndicator()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var task = MiddleLayerService.CurrentIndexBuild;
            bool running = task != null && !task.IsCompleted && !MiddleLayerService.IsSearchIndexBuilt;
            ViewModel.IsIndexing = running;
            if (running)
            {
                int done = MiddleLayerService.IndexBuildIndexed, total = MiddleLayerService.IndexBuildTotal;
                // Show row-count progress so it's clearly advancing (not stuck); search is slower meanwhile.
                ViewModel.IndexStatusText = total > 0
                    ? $"Building search index… {done:N0} / {total:N0} ({Math.Min(100, (int)(done * 100L / total))}%)"
                    : "Building search index… starting…";
            }
        });
    }

    private void CancelIndexingButton_Click(object sender, RoutedEventArgs e)
    {
        MiddleLayerService.CancelBackgroundIndexBuild();
        ViewModel.IsIndexing = false;
    }

    private void LazyIndexTimer_Tick(object sender, object e)
    {
        _lazyIndexTimer.Stop();
        _ = TryLazyBuildIndexAsync();
    }

    /// <summary>
    /// Lazy mode: the first time the user runs a real substring search, kick off the index build so
    /// subsequent searches are fast. The current search already returned correct results via the LIKE
    /// fallback, so we don't block or refresh it. If the build is predicted to take a while, ask first.
    /// </summary>
    private async System.Threading.Tasks.Task TryLazyBuildIndexAsync()
    {
        if (MiddleLayerService.EffectiveIndexingMode != FindPluginCore.Searching.IndexingMode.Lazy) return;
        var term = (SearchBox.Text ?? "").Trim();
        if (term.Length < 3) return;                       // trigram index needs >= 3 chars
        if (MiddleLayerService.IsSearchIndexBuilt) return; // already fast
        var inFlight = MiddleLayerService.CurrentIndexBuild;
        if (inFlight != null && !inFlight.IsCompleted) return; // already building

        long predMs = MiddleLayerService.PredictSearchIndexMs();
        if (predMs > 30000 && !_lazyPromptShown)
        {
            _lazyPromptShown = true;
            var dialog = new ContentDialog
            {
                Title = "Build search index?",
                Content = $"Fast substring search needs an index that will take about {predMs / 1000} seconds " +
                          "to build for this log. Build it now? Until it's ready, search keeps working with a slower scan.",
                PrimaryButtonText = "Build now",
                CloseButtonText = "Keep using slower search",
                XamlRoot = this.XamlRoot
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        }

        MiddleLayerService.StartBackgroundIndexBuild();
        UpdateIndexingIndicator();
    }

    private void StopStreamingButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.StopStreaming();
    }

    private void RefreshResults_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.RefreshNow();
    }

    private FindNeedlePluginUtils.StructuredLog.PayloadFormat _appliedPayloadFormat =
        ResultsViewerSettings.EtwPayloadFormat;
    private string _appliedPayloadTemplate = ResultsViewerSettings.EtwPayloadCustomTemplate;

    private void OnSettingsChanged()
    {
        // Settings page edited the prefs while the viewer is open — reapply.
        DispatcherQueue.TryEnqueue(() =>
        {
            ApplyPersistedSettings();
            ApplyPersistedLevelOverrides();
            ApplyScrollBarSize();
            ApplyRowFontSize();
            // The payload format is baked into each LogLine's message when the page is built, so a
            // change needs a re-fetch (rebuilds the rows from storage with the new render).
            if (ResultsViewerSettings.EtwPayloadFormat != _appliedPayloadFormat
                || ResultsViewerSettings.EtwPayloadCustomTemplate != _appliedPayloadTemplate)
            {
                _appliedPayloadFormat = ResultsViewerSettings.EtwPayloadFormat;
                _appliedPayloadTemplate = ResultsViewerSettings.EtwPayloadCustomTemplate;
                ViewModel.RefreshNow();
            }
            else
            {
                RebindGrid();
            }
        });
    }

    private double _appliedRowFontSize = -1;
    private double _appliedRowHeightRatio = -1;

    /// <summary>Apply the user's configured row text size to the grid, scaling the (fixed, for
    /// scroll perf) row height to match so larger text isn't clipped. Idempotent: OnSettingsChanged
    /// fires for any preference, so skip the (re-layout-triggering) FontSize/RowHeight writes when
    /// neither the size nor the row-height ratio changed — otherwise every unrelated setting tweak
    /// re-lays-out the grid.</summary>
    private void ApplyRowFontSize()
    {
        if (ResultsGrid == null) return;
        double size = ResultsViewerSettings.RowFontSize;
        double ratio = ResultsViewerSettings.RowHeightRatio;
        if (size == _appliedRowFontSize && ratio == _appliedRowHeightRatio) return;
        _appliedRowFontSize = size;
        _appliedRowHeightRatio = ratio;
        // The grid's own FontSize does NOT cascade to cell text — each column owns its font. Set it
        // per text column so the row text actually resizes (the caller rebinds the grid so any
        // realized cells re-render at the new size).
        ResultsGrid.FontSize = size;
        foreach (var col in ResultsGrid.Columns)
            if (col is DataGridTextColumn tc) tc.FontSize = size;
        // Keep the row height proportional to the text. The ratio is a user-selectable density preset
        // (default 1.9); floor at 26px so rows never collapse below the comfortable baseline.
        ResultsGrid.RowHeight = Math.Max(26, Math.Ceiling(size * ratio));
    }

    /// <summary>Apply the user's configured scrollbar thickness. Overrides the WinUI
    /// <c>ScrollBarSize</c> resource at page scope (picked up when scrollbars (re)template) and, for an
    /// already-realized grid, pushes the size onto the live scrollbars so the change shows immediately.</summary>
    private void ApplyScrollBarSize()
    {
        double size = ResultsViewerSettings.ScrollBarSize;
        Resources["ScrollBarSize"] = size;
        if (ResultsGrid == null) return;
        try
        {
            foreach (var sb in FindDescendants<Microsoft.UI.Xaml.Controls.Primitives.ScrollBar>(ResultsGrid))
            {
                if (sb.Orientation == Orientation.Vertical) { sb.Width = size; sb.MinWidth = size; }
                else { sb.Height = size; sb.MinHeight = size; }
                sb.IndicatorMode = Microsoft.UI.Xaml.Controls.Primitives.ScrollingIndicatorMode.MouseIndicator;
            }
        }
        catch { /* visual tree not realized yet — the resource override covers the next template pass */ }
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var d in FindDescendants<T>(child)) yield return d;
        }
    }

    private void ApplyPersistedSettings()
    {
        TimeFormatConverter.Format = ResultsViewerSettings.TimeFormat;
        ViewModel.ApplyTheme(ResultsViewerSettings.ThemeName);
        RefreshSearchSubmitMode();
        _filterDock = ResultsViewerSettings.FilterDock;
        RefreshFilterLayout();
        _colorTaggedRows = ResultsViewerSettings.ColorTaggedRows;
        // "Show known" toggle: set visibility now; combos get populated after the data finishes loading.
        if (ShowKnownToggle != null)
        {
            _suppressKnownCombo = true;
            ShowKnownToggle.IsChecked = ResultsViewerSettings.ShowKnownValues;
            _suppressKnownCombo = false;
            ApplyKnownFilterVisibility(ResultsViewerSettings.ShowKnownValues);
        }
    }

    private void ApplyPersistedLevelOverrides()
    {
        var overrides = ResultsViewerSettings.LevelColors;
        if (overrides.Count == 0) { RebuildLevelLookup(); return; }
        foreach (var entry in ViewModel.Levels)
        {
            if (overrides.TryGetValue(entry.Level, out var hex))
                entry.HexColor = hex;
        }
        RebuildLevelLookup();
    }

    /// <summary>Rebuild the Level-name dictionary used by <see cref="ResultsGrid_LoadingRow"/>.</summary>
    private void RebuildLevelLookup()
    {
        _levelLookup.Clear();
        foreach (var entry in ViewModel.Levels)
            _levelLookup[entry.Level] = entry;
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
        => this.Frame?.Navigate(typeof(ResultsViewerSettingsPage));

    /// <summary>Flag the Sources button (red ⚠) when any loaded location reported an error, so a failed
    /// online source / unreadable file is visible without opening the dialog. Online locations format
    /// their description as "… — ERROR: message".</summary>
    private void UpdateSourcesButtonState()
    {
        if (SourcesButton == null) return;
        var errors = new System.Collections.Generic.List<string>();
        foreach (var loc in MiddleLayerService.Locations)
        {
            string desc;
            try { desc = loc.GetDescription() ?? ""; } catch { continue; }
            int i = desc.IndexOf("ERROR:", StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
            {
                string name; try { name = loc.GetName(); } catch { name = "(location)"; }
                errors.Add($"{name}: {desc.Substring(i + "ERROR:".Length).Trim()}");
            }
        }
        bool any = errors.Count > 0;
        SourcesWarnGlyph.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(SourcesButton, any
            ? "A source failed to load:\n" + string.Join("\n", errors)
            : "See which locations and rule files this search loaded");
    }

    // ----- "Sources" dialog: which locations + rule files this search loaded -----
    private async void ShowLoadedSources_Click(object sender, RoutedEventArgs e)
    {
        // Collect once, then render + build the copyable text/JSON from the same data.
        var locInfo = new System.Collections.Generic.List<(string name, string description)>();
        foreach (var loc in MiddleLayerService.Locations)
        {
            string name, desc;
            try { name = loc.GetName(); } catch { name = "(unknown)"; }
            try { desc = loc.GetDescription(); } catch { desc = ""; }
            locInfo.Add((name, desc));
        }
        // Auto-added rules are recorded at search time (independent of the query's RulesConfigPaths,
        // which can be reset) — so they show reliably even when the query lists none. Merge them in and
        // tag which ones were applied automatically.
        var autoAdded = new System.Collections.Generic.HashSet<string>(
            MiddleLayerService.LastAutoAddedRules ?? new System.Collections.Generic.List<string>(),
            StringComparer.OrdinalIgnoreCase);
        var rules = new System.Collections.Generic.List<string>(
            MiddleLayerService.SearchQueryUX?.CurrentQuery?.RulesConfigPaths
            ?? new System.Collections.Generic.List<string>());
        foreach (var a in autoAdded)
            if (!rules.Any(x => string.Equals(x, a, StringComparison.OrdinalIgnoreCase))) rules.Add(a);

        var panel = new StackPanel { Spacing = 10 };

        // Copy buttons (kept inside the content so the dialog stays open after copying).
        var copyRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var copyText = new Button { Content = "Copy as text" };
        copyText.Click += (_, __) => CopyToClipboard(SourcesAsText(locInfo, rules, autoAdded));
        var copyJson = new Button { Content = "Copy as JSON" };
        copyJson.Click += (_, __) => CopyToClipboard(SourcesAsJson(locInfo, rules, autoAdded));
        copyRow.Children.Add(copyText);
        copyRow.Children.Add(copyJson);
        panel.Children.Add(copyRow);

        panel.Children.Add(SourcesHeader($"Locations ({locInfo.Count})"));
        if (locInfo.Count == 0)
            panel.Children.Add(SourcesNote("No locations loaded."));
        else
            foreach (var (name, desc) in locInfo)
                panel.Children.Add(SourcesItem(name, desc));

        panel.Children.Add(SourcesHeader($"Rules ({rules.Count})"));
        if (rules.Count == 0)
            panel.Children.Add(SourcesNote("No rule files loaded."));
        else
            foreach (var r in rules)
                panel.Children.Add(SourcesItem(
                    System.IO.Path.GetFileName(r) + (autoAdded.Contains(r) ? "   — auto-added" : ""), r));

        var dialog = new ContentDialog
        {
            Title = "Loaded sources",
            Content = new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 460,
            },
            CloseButtonText = "Close",
            XamlRoot = this.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private static string SourcesAsText(
        System.Collections.Generic.List<(string name, string description)> locs,
        System.Collections.Generic.List<string> rules,
        System.Collections.Generic.HashSet<string> autoAdded)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Locations ({locs.Count}):");
        if (locs.Count == 0) sb.AppendLine("  (none)");
        foreach (var (name, desc) in locs)
        {
            sb.AppendLine($"  - {name}");
            if (!string.IsNullOrWhiteSpace(desc) && desc != name) sb.AppendLine($"      {desc}");
        }
        sb.AppendLine($"Rules ({rules.Count}):");
        if (rules.Count == 0) sb.AppendLine("  (none)");
        foreach (var r in rules)
            sb.AppendLine($"  - {r}{(autoAdded.Contains(r) ? "   (auto-added)" : "")}");
        return sb.ToString();
    }

    private static string SourcesAsJson(
        System.Collections.Generic.List<(string name, string description)> locs,
        System.Collections.Generic.List<string> rules,
        System.Collections.Generic.HashSet<string> autoAdded)
    {
        var payload = new
        {
            locations = locs.ConvertAll(l => new { name = l.name, description = l.description }),
            rules = rules.ConvertAll(r => new { path = r, autoAdded = autoAdded.Contains(r) }),
        };
        return System.Text.Json.JsonSerializer.Serialize(payload,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    private static TextBlock SourcesHeader(string text) => new()
    {
        Text = text,
        FontWeight = global::Microsoft.UI.Text.FontWeights.SemiBold,
        FontSize = 14,
        Margin = new Thickness(0, 4, 0, 0),
    };

    private static TextBlock SourcesNote(string text) => new()
    {
        Text = text,
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        Margin = new Thickness(8, 0, 0, 0),
    };

    /// <summary>A bullet line: bold-ish title + a dimmed, wrapping, selectable detail (path/description).</summary>
    private static FrameworkElement SourcesItem(string title, string detail)
    {
        var sp = new StackPanel { Margin = new Thickness(8, 0, 0, 0), Spacing = 1 };
        sp.Children.Add(new TextBlock { Text = "• " + title, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        if (!string.IsNullOrWhiteSpace(detail) && detail != title)
            sp.Children.Add(new TextBlock
            {
                Text = detail,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                Margin = new Thickness(12, 0, 0, 0),
            });
        return sp;
    }

    private void FiltersToggle_Click(object sender, RoutedEventArgs e)
    {
        var expanded = FiltersToggle.IsChecked == true;
        ApplyFiltersToggleState(expanded);
        ResultsViewerSettings.FiltersExpanded = expanded;
    }

    private void ResultsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DetailsPanel.Visibility == Visibility.Visible) RefreshDetailsPanel();
    }

    /// <summary>
    /// Rebuild the panel's label/value grid from the currently selected row. Cheap — there are
    /// only ~10 fields and we run on selection change. Skipping when nothing is selected leaves
    /// the placeholder text in place.
    /// </summary>
    private void RefreshDetailsPanel()
    {
        DetailsPanelGrid.Children.Clear();
        DetailsPanelGrid.RowDefinitions.Clear();

        if (ResultsGrid.SelectedItem is not LogLine line)
        {
            DetailsPanelGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var placeholder = new TextBlock
            {
                Text = "Select a row above to see its full details here.",
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                FontStyle = global::Windows.UI.Text.FontStyle.Italic,
                Margin = new Thickness(0, 4, 0, 4),
            };
            Grid.SetRow(placeholder, 0);
            Grid.SetColumnSpan(placeholder, 2);
            DetailsPanelGrid.Children.Add(placeholder);
            return;
        }

        PopulateDetailsGrid(DetailsPanelGrid, line);
    }

    /// <summary>The (label, value) pairs shown for a row — shared by the panel and the popup.</summary>
    private System.Collections.Generic.IEnumerable<(string label, string value)> RowFields(LogLine line)
    {
        var vis = ResultsViewerSettings.DetailFieldVisibility;
        bool Show(string f) => !vis.TryGetValue(f, out var b) || b;

        if (Show("Index"))       yield return ("Index",       line.Index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (Show("Time"))        yield return ("Time",        line.Time);
        if (Show("Provider"))    yield return ("Provider",    line.Provider);
        if (Show("TaskName"))    yield return ("TaskName",    line.TaskName);
        if (Show("Message"))     yield return ("Message",     line.Message);
        if (Show("Source"))      yield return ("Source",      line.Source);
        if (Show("Level"))       yield return ("Level",       line.Level);
        if (Show("MachineName")) yield return ("MachineName", line.MachineName);
        if (Show("Username"))    yield return ("Username",    line.Username);
        if (Show("OpCode"))      yield return ("OpCode",      line.OpCode);
        if (Show("ProcessId"))   yield return ("ProcessId",   line.ProcessId);
        if (Show("ProcessName")) yield return ("ProcessName", line.ProcessName);
        if (Show("ThreadId"))    yield return ("ThreadId",    line.ThreadId);
        if (Show("ActivityId"))  yield return ("ActivityId",  line.ActivityId);
        if (Show("EventId"))     yield return ("EventId",     line.EventId);
        if (Show("Keywords"))    yield return ("Keywords",    line.Keywords);
        if (Show("RelatedActivityId")) yield return ("RelatedActivityId", line.RelatedActivityId);
        if (Show("Channel"))     yield return ("Channel",     line.Channel);
        if (Show("ProviderGuid")) yield return ("ProviderGuid", line.ProviderGuid);
        if (Show("RecordId"))    yield return ("RecordId",    line.RecordId);
        if (Show("Tag") && _rowTags.TryGetValue(line.RowId, out var rt))
            yield return ("Tag", string.IsNullOrEmpty(rt.Text) ? rt.Name : $"{rt.Name} — {rt.Text}");
    }

    /// <summary>
    /// Fill a label / value / per-field-Copy grid with a row's fields. Shared by the bottom panel and
    /// the double-click popup so both behave identically. Values wrap and are selectable; a very long
    /// value gets its own bounded scroller so one giant field can't dominate.
    /// </summary>
    private void PopulateDetailsGrid(Grid g, LogLine line)
    {
        g.Children.Clear();
        g.RowDefinitions.Clear();
        g.ColumnDefinitions.Clear();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });               // Copy
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });           // label
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // value
        g.ColumnSpacing = 8;
        g.RowSpacing = 6;

        foreach (var (label, value) in RowFields(line))
        {
            int row = g.RowDefinitions.Count;
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var copy = new Button
            {
                Content = new FontIcon { Glyph = "", FontSize = 14 }, // Copy (Segoe MDL2 Assets)
                Padding = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Top,
            };
            var captured = value ?? "";
            copy.Click += (_, __) => CopyToClipboard(captured);
            ToolTipService.SetToolTip(copy, $"Copy {label}");
            Grid.SetRow(copy, row); Grid.SetColumn(copy, 0);
            g.Children.Add(copy);

            var k = new TextBlock
            {
                Text = label,
                FontWeight = global::Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Top,
            };
            Grid.SetRow(k, row); Grid.SetColumn(k, 1);
            g.Children.Add(k);

            var text = new TextBlock
            {
                Text = value ?? "",
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                VerticalAlignment = VerticalAlignment.Top,
            };
            // Large field: cap its height and let it scroll on its own.
            FrameworkElement valueElement = (value?.Length ?? 0) > 1500
                ? new ScrollViewer
                {
                    Content = text,
                    MaxHeight = 220,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                }
                : text;
            Grid.SetRow(valueElement, row); Grid.SetColumn(valueElement, 2);
            g.Children.Add(valueElement);
        }

        AppendStructuredData(g, line);
        AppendReformattedMessage(g, line);
    }

    /// <summary>Append a "Reformatted" section when an enabled message-reformat rule applies to this
    /// row's Message — breaks a dense one-line blob (e.g. a DISM line) into readable named fields.
    /// Generic: driven by <see cref="MessageReformatCatalog"/>, which the user can edit/extend.</summary>
    private void AppendReformattedMessage(Grid g, LogLine line)
    {
        MessageReformatResult r;
        try { r = MessageReformatCatalog.Apply(line.Message); }
        catch { return; }
        if (r == null || r.Fields.Count == 0) return;

        int headerRow = g.RowDefinitions.Count;
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new TextBlock
        {
            Text = $"Reformatted — {r.RuleName} ({r.Fields.Count})",
            FontWeight = global::Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 10, 0, 2),
        };
        Grid.SetRow(header, headerRow); Grid.SetColumn(header, 0); Grid.SetColumnSpan(header, 3);
        g.Children.Add(header);

        foreach (var (field, value) in r.Fields)
        {
            int row = g.RowDefinitions.Count;
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var copy = new Button
            {
                Content = new FontIcon { Glyph = "", FontSize = 14 },
                Padding = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Top,
            };
            var captured = value ?? "";
            copy.Click += (_, __) => CopyToClipboard(captured);
            ToolTipService.SetToolTip(copy, $"Copy {field}");
            Grid.SetRow(copy, row); Grid.SetColumn(copy, 0);
            g.Children.Add(copy);

            var k = new TextBlock
            {
                Text = field,
                FontStyle = global::Windows.UI.Text.FontStyle.Italic,
                VerticalAlignment = VerticalAlignment.Top,
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetRow(k, row); Grid.SetColumn(k, 1);
            g.Children.Add(k);

            var v = new TextBlock
            {
                Text = value ?? "",
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                VerticalAlignment = VerticalAlignment.Top,
            };
            Grid.SetRow(v, row); Grid.SetColumn(v, 2);
            g.Children.Add(v);
        }
    }

    /// <summary>Append the event's parsed structured payload (StructuredData JSON) as a labeled
    /// "Event data" section of key/value rows, so it's readable instead of buried in the message.</summary>
    private void AppendStructuredData(Grid g, LogLine line)
    {
        if (string.IsNullOrWhiteSpace(line.StructuredData)) return;
        Dictionary<string, string> pairs;
        try
        {
            pairs = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(line.StructuredData);
        }
        catch { return; }
        if (pairs == null || pairs.Count == 0) return;

        // Section header spanning all columns.
        int headerRow = g.RowDefinitions.Count;
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new TextBlock
        {
            Text = $"Event data ({pairs.Count})",
            FontWeight = global::Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 10, 0, 2),
        };
        Grid.SetRow(header, headerRow); Grid.SetColumn(header, 0); Grid.SetColumnSpan(header, 3);
        g.Children.Add(header);

        foreach (var kv in pairs)
        {
            int row = g.RowDefinitions.Count;
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var copy = new Button
            {
                Content = new FontIcon { Glyph = "", FontSize = 14 },
                Padding = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Top,
            };
            var captured = kv.Value ?? "";
            copy.Click += (_, __) => CopyToClipboard(captured);
            ToolTipService.SetToolTip(copy, $"Copy {kv.Key}");
            Grid.SetRow(copy, row); Grid.SetColumn(copy, 0);
            g.Children.Add(copy);

            var k = new TextBlock
            {
                Text = kv.Key,
                FontStyle = global::Windows.UI.Text.FontStyle.Italic,
                VerticalAlignment = VerticalAlignment.Top,
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetRow(k, row); Grid.SetColumn(k, 1);
            g.Children.Add(k);

            var v = new TextBlock
            {
                Text = kv.Value ?? "",
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                VerticalAlignment = VerticalAlignment.Top,
            };
            Grid.SetRow(v, row); Grid.SetColumn(v, 2);
            g.Children.Add(v);
        }
    }

    /// <summary>Show a flyout of checkboxes to pick which fields the details panel displays. The choice
    /// is persisted (ResultsViewerSettings) and shared with the double-click popup via RowFields.</summary>
    private void DetailsFields_Click(object sender, RoutedEventArgs e)
    {
        var vis = ResultsViewerSettings.DetailFieldVisibility;
        var flyout = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom };
        foreach (var field in ResultsViewerSettings.DetailFieldNames)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = field,
                IsChecked = !vis.TryGetValue(field, out var b) || b,
            };
            var captured = field;
            item.Click += (_, __) =>
            {
                ResultsViewerSettings.SetDetailFieldVisibility(captured, item.IsChecked);
                RefreshDetailsPanel();
            };
            flyout.Items.Add(item);
        }
        flyout.ShowAt(DetailsFieldsButton);
    }

    private void DetailsPanelCopyJson_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is LogLine line) CopyToClipboard(RowAsJson(line));
    }

    private void DetailsPanelCopyCsv_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is LogLine line) CopyToClipboard(RowAsCsv(line));
    }

    private void DetailsPanelCopyXml_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is LogLine line) CopyToClipboard(RowAsXml(line));
    }

    // ----- Pagination -----
    private void FirstPage_Click(object sender, RoutedEventArgs e) => ViewModel.FirstPage();
    private void PrevPage_Click(object sender, RoutedEventArgs e)  => ViewModel.PrevPage();
    private void NextPage_Click(object sender, RoutedEventArgs e)  => ViewModel.NextPage();
    private void LastPage_Click(object sender, RoutedEventArgs e)  => ViewModel.LastPage();

    private void PageJump_Click(object sender, RoutedEventArgs e) => JumpFromTextBox();
    private void PageJumpBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == global::Windows.System.VirtualKey.Enter) { JumpFromTextBox(); e.Handled = true; }
    }
    private void JumpFromTextBox()
    {
        if (int.TryParse(PageJumpBox.Text, out var p))
        {
            ViewModel.GoToPage(p);
            PageJumpBox.Text = "";
        }
    }

    private void PageSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PageSizeCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag
            && int.TryParse(tag, out var size))
        {
            ViewModel.PageSize = size;
            ResultsViewerSettings.PageSize = size;
        }
    }

    // ----- Sort (handled manually so the FULL filtered set is sorted, not just the page) -----
    private void ResultsGrid_Sorting(object sender, DataGridColumnEventArgs e)
    {
        var col = e.Column;
        var name = col.Header?.ToString();
        if (string.IsNullOrEmpty(name)) return;

        // Toggle direction. CommunityToolkit DataGrid leaves the column with the previous arrow
        // until we set it explicitly; first click = Ascending, second = Descending.
        bool descending = col.SortDirection == DataGridSortDirection.Ascending;

        // Clear arrows on all other columns; set the new one.
        foreach (var c in ResultsGrid.Columns)
            if (!ReferenceEquals(c, col)) c.SortDirection = null;
        col.SortDirection = descending ? DataGridSortDirection.Descending : DataGridSortDirection.Ascending;

        ViewModel.ApplySort(name, descending);
    }

    /// <summary>
    /// Set the DataGrid column-header sort arrows to match the ViewModel's current sort state (the
    /// VM is the source of truth — it's seeded from the Default sort preference on each fresh load).
    /// A null/empty sort column (load order) clears all arrows.
    /// </summary>
    private void SyncSortArrowsFromViewModel()
    {
        var name = ViewModel.SortColumn;
        bool descending = ViewModel.SortDescending;
        foreach (var c in ResultsGrid.Columns)
        {
            if (!string.IsNullOrEmpty(name)
                && string.Equals(c.Header?.ToString(), name, StringComparison.OrdinalIgnoreCase))
                c.SortDirection = descending ? DataGridSortDirection.Descending : DataGridSortDirection.Ascending;
            else
                c.SortDirection = null;
        }
    }

    /// <summary>
    /// Force the DataGrid to re-render rows. Used after a theme change (LoadingRow re-runs) or
    /// a Time-format change (the Binding Converter is consulted again).
    /// </summary>
    private void RebindGrid()
    {
        ResultsGrid.ItemsSource = null;
        ResultsGrid.ItemsSource = ViewModel.Results;
    }

    /// <summary>
    /// Re-realize the visible rows (so per-row appearance like the tag glyph re-renders) while
    /// keeping the current selection. Used after a row tag changes — DataGridRow.Header set in place
    /// isn't reliably re-rendered, so we rebind, but restore the selection so it doesn't reset.
    /// </summary>
    private void RerenderRowsPreservingView()
    {
        var sel = ResultsGrid.SelectedItem;
        ResultsGrid.ItemsSource = null;
        ResultsGrid.ItemsSource = ViewModel.Results;
        if (sel != null) ResultsGrid.SelectedItem = sel;
    }

    // ----- Keyboard shortcuts -----
    private void OnPageKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        var ctrl = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(global::Windows.System.VirtualKey.Control)
                    & global::Windows.UI.Core.CoreVirtualKeyStates.Down)
                    == global::Windows.UI.Core.CoreVirtualKeyStates.Down;
        if (ctrl && e.Key == global::Windows.System.VirtualKey.F)
        {
            SearchBox.Focus(FocusState.Programmatic);
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }
    }

    // ----- Search + filter inputs -----
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (RequireEnterToSearch())
        {
            // Enter-to-search mode: don't run a search mid-typing. Just surface the hint so the
            // user knows their edits aren't applied until they press Enter.
            UpdateSearchHint(hasPendingEdit: !string.Equals(SearchBox.Text, ViewModel.SearchText ?? "", StringComparison.Ordinal));
            return;
        }
        // Live: debounce so a burst of keystrokes runs one (background) search, not one per char.
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        // The user is about to search — start preparing the fast-search (FTS) index now, so the
        // first search is fast (or at least the build is already underway). Lazy mode only; no-op if
        // the index is already built or a build is in flight. It runs in the background and is
        // cancellable, and search keeps working (via the slower scan) until it's ready — the
        // "Building search index…" indicator explains the slowness meanwhile.
        if (MiddleLayerService.EffectiveIndexingMode != FindPluginCore.Searching.IndexingMode.Lazy) return;
        if (MiddleLayerService.IsSearchIndexBuilt) return;
        var inFlight = MiddleLayerService.CurrentIndexBuild;
        if (inFlight != null && !inFlight.IsCompleted) return;

        MiddleLayerService.StartBackgroundIndexBuild();
        UpdateIndexingIndicator();
    }

    private void SearchBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == global::Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            _searchDebounceTimer.Stop();
            _ = RunSearchAsync(); // commit now, regardless of mode
        }
    }

    /// <summary>
    /// Run the search box text against the filter OFF the UI thread, so even a multi-second LIKE scan
    /// on a huge log never freezes the window. Cancels any in-flight search (only the latest applies),
    /// shows a busy ring, times it so Auto mode can adapt, then nudges the lazy index build.
    /// </summary>
    private async Task RunSearchAsync()
    {
        if (!ViewModel.SetSearchTextDeferred(SearchBox.Text))
            return; // term unchanged — nothing to do (e.g. Enter pressed without edits)

        _searchCts?.Cancel();
        var cts = new System.Threading.CancellationTokenSource();
        _searchCts = cts;

        SetSearchBusy(true);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await ViewModel.ApplyFiltersAsync(cts.Token);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Search failed: {ex.Message}");
        }
        finally
        {
            sw.Stop();
            // Only the most recent search updates shared UI state (busy ring, adaptive mode).
            if (ReferenceEquals(_searchCts, cts))
            {
                SetSearchBusy(false);
                if (_searchSubmitMode == SearchSubmitMode.Auto)
                    _autoEnterActive = SearchSubmitPolicy.NextAutoEnterState(_autoEnterActive, sw.ElapsedMilliseconds);
                UpdateSearchHint(hasPendingEdit: false, lastSearchMs: sw.ElapsedMilliseconds);

                // Lazy mode: nudge the index build now that typing has paused (faster next search).
                if (MiddleLayerService.EffectiveIndexingMode == FindPluginCore.Searching.IndexingMode.Lazy
                    && !MiddleLayerService.IsSearchIndexBuilt)
                {
                    _lazyIndexTimer.Stop();
                    _lazyIndexTimer.Start();
                }
            }
        }
    }

    private void SetSearchBusy(bool busy)
    {
        if (SearchBusyRing == null) return;
        SearchBusyRing.IsActive = busy;
        SearchBusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Show/hide the "Press Enter to search" hint based on the effective submit mode.</summary>
    private void UpdateSearchHint(bool hasPendingEdit, long lastSearchMs = -1)
    {
        if (SearchHint == null) return;
        if (RequireEnterToSearch())
        {
            SearchHint.Visibility = Visibility.Visible;
            SearchHint.Text = hasPendingEdit ? "↵ Press Enter to search" : "↵ Enter to search";
        }
        else
        {
            SearchHint.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Read the persisted submit mode and, for Auto, seed Enter-to-search when the log is large and
    /// the FTS index isn't built yet (so the very first character doesn't pay a multi-second search).
    /// Measured latency in <see cref="RunSearchFromBox"/> refines it from there.
    /// </summary>
    private void RefreshSearchSubmitMode()
    {
        _searchSubmitMode = ResultsViewerSettings.SearchSubmitMode;
        _autoEnterActive = SearchSubmitPolicy.ShouldSeedEnterToSearch(
            _searchSubmitMode, ViewModel.TotalCount, MiddleLayerService.IsSearchIndexBuilt);
        UpdateSearchHint(hasPendingEdit: false);
    }

    private void OnViewModelPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NativeResultsPageViewModel.TotalCount))
            DispatcherQueue.TryEnqueue(RefreshSearchSubmitMode);
        else if (e.PropertyName == nameof(NativeResultsPageViewModel.TotalFilteredCount))
            DispatcherQueue.TryEnqueue(UpdateEmptyState);
        // When a streaming load finishes, re-check the decode banner — the new file's stats are now
        // captured, so a stale "missing symbols" warning clears (or the new file's own appears) — and
        // enable the Rule filter toggle (it was disabled while the load was still streaming).
        else if (e.PropertyName == nameof(NativeResultsPageViewModel.IsStreaming) && !ViewModel.IsStreaming)
            DispatcherQueue.TryEnqueue(() => { UpdateDecodeBanner(); UpdateRuleFilterToggleState(); UpdateSourcesButtonState(); UpdateEmptyState(); });
    }
    private void ProviderFilter_TextChanged(object sender, TextChangedEventArgs e) => ViewModel.ProviderFilter = ProviderFilterBox.Text;
    private void TaskNameFilter_TextChanged(object sender, TextChangedEventArgs e) => ViewModel.TaskNameFilter = TaskNameFilterBox.Text;
    private void MessageFilter_TextChanged(object sender, TextChangedEventArgs e)  => ViewModel.MessageFilter  = MessageFilterBox.Text;
    private void SourceFilter_TextChanged(object sender, TextChangedEventArgs e)   => ViewModel.SourceFilter   = SourceFilterBox.Text;
    private void LevelFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => ViewModel.LevelFilter = (LevelFilterCombo.SelectedItem as string) ?? "";

    // ---- "Show known" value dropdowns (Provider / TaskName / Source) ----
    // Each field has three controls: a free-text box, a single-select combo, and a multi-select
    // dropdown. Which combo/multi shows depends on the per-field KnownFilterMode setting.
    // Guards handlers while we populate/select programmatically so they don't re-apply filters.
    private bool _suppressKnownCombo;

    private async void ShowKnownToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressKnownCombo) return; // programmatic set during ApplyPersistedSettings
        bool on = ShowKnownToggle.IsChecked == true;
        ResultsViewerSettings.ShowKnownValues = on;
        ApplyKnownFilterVisibility(on);
        if (on) await PopulateKnownFilterCombosAsync();
    }

    /// <summary>Swap each free-text filter box for its known-value control (single combo or multi
    /// dropdown, per the field's setting) — or back to the text box.</summary>
    private void ApplyKnownFilterVisibility(bool knownOn)
    {
        SetKnownFieldVisibility(knownOn, "Provider", ProviderFilterBox, ProviderFilterCombo, ProviderFilterMulti);
        SetKnownFieldVisibility(knownOn, "TaskName", TaskNameFilterBox, TaskNameFilterCombo, TaskNameFilterMulti);
        SetKnownFieldVisibility(knownOn, "Source",   SourceFilterBox,   SourceFilterCombo,   SourceFilterMulti);
        // Returning to text mode: make the boxes reflect the active filter (idempotent — same value
        // re-set is a no-op in the VM) so the visible control matches reality after a combo pick.
        if (!knownOn)
        {
            if (ProviderFilterBox != null) ProviderFilterBox.Text = ViewModel.ProviderFilter ?? "";
            if (TaskNameFilterBox != null) TaskNameFilterBox.Text = ViewModel.TaskNameFilter ?? "";
            if (SourceFilterBox != null) SourceFilterBox.Text = ViewModel.SourceFilter ?? "";
        }
    }

    private void SetKnownFieldVisibility(bool knownOn, string field, TextBox box, ComboBox combo, DropDownButton multi)
    {
        bool isMulti = ResultsViewerSettings.GetKnownFilterMode(field) == KnownFilterMode.Multi;
        if (box != null) box.Visibility = knownOn ? Visibility.Collapsed : Visibility.Visible;
        if (combo != null) combo.Visibility = (knownOn && !isMulti) ? Visibility.Visible : Visibility.Collapsed;
        if (multi != null) multi.Visibility = (knownOn && isMulti) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Fill each known-value control from the loaded set's distinct values (off the UI thread).</summary>
    private async System.Threading.Tasks.Task PopulateKnownFilterCombosAsync()
    {
        await FillKnownFieldAsync("Provider", ProviderFilterCombo, ProviderFilterMultiList, ProviderFilterMulti,
                                  ViewModel.ProviderFilter, ViewModel.ProviderFilterSet);
        await FillKnownFieldAsync("TaskName", TaskNameFilterCombo, TaskNameFilterMultiList, TaskNameFilterMulti,
                                  ViewModel.TaskNameFilter, ViewModel.TaskNameFilterSet);
        await FillKnownFieldAsync("Source",   SourceFilterCombo,   SourceFilterMultiList,   SourceFilterMulti,
                                  ViewModel.SourceFilter, ViewModel.SourceFilterSet);
    }

    private async System.Threading.Tasks.Task FillKnownFieldAsync(string field, ComboBox combo, ListView multiList,
        DropDownButton multiButton, string current, System.Collections.Generic.IReadOnlyList<string> currentSet)
    {
        var values = new List<string>();
        try
        {
            var facets = await System.Threading.Tasks.Task.Run(() => ViewModel.GetFacets(field, 1000, 200000));
            if (facets?.Values != null)
                values = facets.Values.Select(v => v.Value)
                    .Where(s => !string.IsNullOrEmpty(s) && s != "(none)").ToList();
        }
        catch { /* facet failure → empty control, not a crash */ }

        _suppressKnownCombo = true;
        if (combo != null)
        {
            combo.Items.Clear();
            foreach (var v in values) combo.Items.Add(v);
            combo.SelectedItem = values.FirstOrDefault(v => string.Equals(v, current, StringComparison.OrdinalIgnoreCase));
        }
        if (multiList != null)
        {
            multiList.Items.Clear();
            foreach (var v in values) multiList.Items.Add(v);
            multiList.SelectedItems.Clear();
            if (currentSet != null)
                foreach (var v in values)
                    if (currentSet.Contains(v, StringComparer.OrdinalIgnoreCase)) multiList.SelectedItems.Add(v);
            UpdateMultiButtonLabel(field, multiButton, multiList);
        }
        _suppressKnownCombo = false;
    }

    private static void UpdateMultiButtonLabel(string field, DropDownButton button, ListView list)
    {
        if (button == null) return;
        int n = list?.SelectedItems.Count ?? 0;
        button.Content = n == 0 ? $"{field} (any)" : $"{field} ({n})";
    }

    private void ProviderFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressKnownCombo) return;
        ViewModel.ProviderFilter = (ProviderFilterCombo.SelectedItem as string) ?? "";
    }
    private void TaskNameFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressKnownCombo) return;
        ViewModel.TaskNameFilter = (TaskNameFilterCombo.SelectedItem as string) ?? "";
    }
    private void SourceFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressKnownCombo) return;
        ViewModel.SourceFilter = (SourceFilterCombo.SelectedItem as string) ?? "";
    }

    private void ProviderFilterMultiList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => ApplyMultiSelection("Provider", ProviderFilterMulti, ProviderFilterMultiList);
    private void TaskNameFilterMultiList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => ApplyMultiSelection("TaskName", TaskNameFilterMulti, TaskNameFilterMultiList);
    private void SourceFilterMultiList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => ApplyMultiSelection("Source", SourceFilterMulti, SourceFilterMultiList);

    private void ApplyMultiSelection(string field, DropDownButton button, ListView list)
    {
        UpdateMultiButtonLabel(field, button, list);
        if (_suppressKnownCombo) return;
        var values = list.SelectedItems.OfType<string>().ToList();
        ViewModel.SetKnownFilterSet(field, values);
    }

    private void FromDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
        => UpdateFromBound();
    private void ToDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
        => UpdateToBound();
    private void FromTimePicker_SelectedTimeChanged(TimePicker sender, TimePickerSelectedValueChangedEventArgs args)
        => UpdateFromBound();
    private void ToTimePicker_SelectedTimeChanged(TimePicker sender, TimePickerSelectedValueChangedEventArgs args)
        => UpdateToBound();

    // Combine the date + optional time pickers into the filter bound. No date → no bound. The "From"
    // bound floors to the start of the day when no time is given; the "To" bound extends to the end of
    // the day (or the end of the selected minute) so the range is inclusive of what the user picked.
    private void UpdateFromBound()
    {
        var d = FromDatePicker?.Date?.DateTime;
        if (d == null) { ViewModel.FromDate = null; return; }
        var t = FromTimePicker?.SelectedTime;
        ViewModel.FromDate = t.HasValue ? d.Value.Date + t.Value : d.Value.Date;
    }

    private void UpdateToBound()
    {
        var d = ToDatePicker?.Date?.DateTime;
        if (d == null) { ViewModel.ToDate = null; return; }
        var t = ToTimePicker?.SelectedTime;
        ViewModel.ToDate = t.HasValue
            ? d.Value.Date + t.Value + TimeSpan.FromSeconds(59.999) // include the whole selected minute
            : d.Value.Date.AddDays(1).AddMilliseconds(-1);          // end of the selected day
    }

    private void ClearTimeRange_Click(object sender, RoutedEventArgs e)
    {
        FromDatePicker.Date = null;
        ToDatePicker.Date = null;
        FromTimePicker.SelectedTime = null;
        ToTimePicker.SelectedTime = null;
    }

    private void ResetColumnFilters_Click(object sender, RoutedEventArgs e)
    {
        ProviderFilterBox.Text = TaskNameFilterBox.Text = MessageFilterBox.Text = SourceFilterBox.Text = "";
        LevelFilterCombo.SelectedItem = null;
        ViewModel.LevelFilter = "";
    }

    private void ClearFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        ProviderFilterBox.Text = TaskNameFilterBox.Text = MessageFilterBox.Text = SourceFilterBox.Text = "";
        LevelFilterCombo.SelectedItem = null;
        FromDatePicker.Date = null;
        ToDatePicker.Date = null;
        FromTimePicker.SelectedTime = null;
        ToTimePicker.SelectedTime = null;
        ViewModel.ClearFilters();
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
        => RunExport(NativeResultViewer.NativeResultsPageViewModel.ExportFormat.Csv);

    private void ExportJson_Click(object sender, RoutedEventArgs e)
        => RunExport(NativeResultViewer.NativeResultsPageViewModel.ExportFormat.Json);

    private void ExportXml_Click(object sender, RoutedEventArgs e)
        => RunExport(NativeResultViewer.NativeResultsPageViewModel.ExportFormat.Xml);

    private async void RunExport(NativeResultViewer.NativeResultsPageViewModel.ExportFormat format)
    {
        var path = await ViewModel.ExportAsync(format);
        if (path != null)
        {
            var dlg = new ContentDialog
            {
                Title = "Export complete",
                Content = $"Saved {ViewModel.TotalFilteredCount:N0} rows to:\n{path}",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            _ = dlg.ShowAsync();
        }
    }

    // ----- Help dialog -----
    private async void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        var stack = new StackPanel { Spacing = 6 };
        void Bullet(string s) => stack.Children.Add(new TextBlock { Text = "• " + s, TextWrapping = TextWrapping.Wrap });
        Bullet("Top searchbox — case-insensitive, all columns. Ctrl+F to focus.");
        Bullet("Per-column filters — type Provider/TaskName/Message/Source. Level is a dropdown.");
        Bullet("Time range — pick From/To; rows outside are hidden. Clear to remove.");
        Bullet("Columns ▾ — toggle which columns are visible.");
        Bullet("Drag column headers to reorder; drag the right edge to resize.");
        Bullet("Click column headers to sort.");
        Bullet("Level chips — click to edit that level's row background color.");
        Bullet("Click a row to expand details; use Copy as JSON to copy the full LogLine.");
        Bullet("Export CSV — saves the currently visible (filtered) rows, only currently visible columns.");
        Bullet("Details modes (toolbar): Inrow expand, bottom panel, or double-click popup.");
        var dialog = new ContentDialog
        {
            Title = "Filtering & navigation",
            Content = stack,
            CloseButtonText = "Close",
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private void OnColumnVisibilityChanged(ColumnEntry entry) => ApplyColumnVisibility(entry);

    private void ApplyAllColumnVisibility()
    {
        foreach (var entry in ViewModel.Columns) ApplyColumnVisibility(entry);
    }

    private void ApplyColumnVisibility(ColumnEntry entry)
    {
        var col = ResultsGrid.Columns.FirstOrDefault(c => string.Equals(c.Header?.ToString(), entry.Name, StringComparison.OrdinalIgnoreCase));
        if (col != null) col.Visibility = entry.IsVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    // ----- Level color editor -----
    private async void LevelChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string level) return;
        var entry = ViewModel.Levels.FirstOrDefault(x => string.Equals(x.Level, level, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return;

        var picker = new ColorPicker { IsAlphaEnabled = false, Color = ParseHex(entry.HexColor) };
        var dialog = new ContentDialog
        {
            Title = $"Color for {level}",
            Content = picker,
            PrimaryButtonText = "OK",
            SecondaryButtonText = "Reset to default",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var c = picker.Color;
            var hex = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
            entry.HexColor = hex;
            // Persist as a per-level override.
            ResultsViewerSettings.SetLevelColor(level, hex);
        }
        else if (result == ContentDialogResult.Secondary)
        {
            // Reset = drop user override, fall back to active theme.
            var theme = NativeResultsPageViewModel.ThemePresets.TryGetValue(ResultsViewerSettings.ThemeName, out var t)
                ? t : NativeResultsPageViewModel.ThemePresets[NativeResultsPageViewModel.DefaultThemeName];
            entry.HexColor = theme.TryGetValue(level, out var def) ? def : "Transparent";
            ResultsViewerSettings.SetLevelColor(level, entry.HexColor);
        }
        RebindGrid();
    }

    // ----- Row coloring -----
    // Hot path during scroll. Perf rules:
    //   1. Dictionary lookup (not LINQ FirstOrDefault).
    //   2. HexToBrushConverter.Parse caches one SolidColorBrush per hex — no per-row alloc.
    //   3. Skip the DP write when Background is already the right brush. Recycled rows often
    //      get the same color twice in a row during scroll; an unchanged DP set still goes
    //      through some property-system bookkeeping, so an early-out is a win.
    private void ResultsGrid_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        if (e.Row.DataContext is LogLine line) ApplyRowAppearance(e.Row, line);
    }

    /// <summary>
    /// Set a row's background (level tint, or tag tint when "color tagged rows" is on) and its row-
    /// header tag glyph. Used by LoadingRow and to refresh one row in place after a tag change (so we
    /// don't re-publish the page, which would reset the grid selection to the top).
    /// </summary>
    private void ApplyRowAppearance(CommunityToolkit.WinUI.UI.Controls.DataGridRow rowEl, LogLine line)
    {
        var levelHex = (!string.IsNullOrEmpty(line.Level) && _levelLookup.TryGetValue(line.Level, out var entry))
            ? entry.HexColor : null;
        string tagHex = null;
        bool tagged = _rowTags.TryGetValue(line.RowId, out var rowTag) && _tagColors.TryGetValue(rowTag.Name, out tagHex);
        var tag = rowTag.Name;

        // Background: tag tint (when the option is on) else the level tint. Rows recycle, so always set.
        Microsoft.UI.Xaml.Media.Brush bg;
        if (tagged && _colorTaggedRows)
        {
            var c = HexToBrushConverter.ParseColor(tagHex);
            bg = new Microsoft.UI.Xaml.Media.SolidColorBrush(global::Windows.UI.Color.FromArgb(0x44, c.R, c.G, c.B));
        }
        else
        {
            bg = levelHex != null ? HexToBrushConverter.Parse(levelHex) : null;
        }
        rowEl.Background = bg;

        // Tag → a colored tag glyph in the row header (to the left of the row). Cleared on recycle.
        if (tagged)
        {
            var marker = new FontIcon
            {
                Glyph = "", // Tag (Segoe MDL2 Assets)
                FontSize = 12,
                Foreground = HexToBrushConverter.Parse(tagHex),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(marker,
                string.IsNullOrEmpty(rowTag.Text) ? $"Tag: {tag}" : $"{tag}: {rowTag.Text}");
            rowEl.Header = marker;
        }
        else
        {
            rowEl.Header = null;
        }
    }

    // ----- Row details -----
    private void CopyJson_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is LogLine line)
        {
            CopyToClipboard(RowAsJson(line));
        }
    }

    // ----- Row right-click context menu -----
    //
    // RightTapped fires on the DataGrid for any descendant. Walk the visual tree to find both
    // the row (LogLine DataContext) and the clicked cell (DataGridCell — its Column.Header
    // tells us which field). Show a MenuFlyout with cell-copy + row-as-JSON/CSV/XML options.
    private void ResultsGrid_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject src) return;

        // Right-click on a column header → quick rewrite/extract rule for that column.
        var headerColumn = TryGetRightClickedHeader(src);
        if (headerColumn != null)
        {
            e.Handled = true;
            _ = ShowColumnQuickRuleDialogAsync(headerColumn);
            return;
        }

        LogLine row = null;
        string cellColumnHeader = null;
        CommunityToolkit.WinUI.UI.Controls.DataGridRow rowEl = null;

        var node = src;
        while (node != null && (row == null || cellColumnHeader == null || rowEl == null))
        {
            if (node is FrameworkElement fe)
            {
                if (row == null && fe.DataContext is LogLine ll) row = ll;
                if (cellColumnHeader == null
                    && fe is CommunityToolkit.WinUI.UI.Controls.DataGridCell cell)
                {
                    cellColumnHeader = TryGetCellColumnHeader(cell);
                }
                if (rowEl == null && fe is CommunityToolkit.WinUI.UI.Controls.DataGridRow dgr)
                    rowEl = dgr;
            }
            node = VisualTreeHelper.GetParent(node);
        }
        if (row == null) return;

        // Re-realize visible rows so the tag glyph in the row header shows immediately. Setting
        // DataGridRow.Header in place isn't re-rendered until the row is re-created (which is why a
        // tag only appeared after navigating away and back). Preserve selection + keep the tagged
        // row in view so it doesn't jump to the top.
        void RefreshRow()
        {
            RerenderRowsPreservingView();
            try { ResultsGrid.ScrollIntoView(row, null); } catch { /* row may be off-page */ }
        }

        var flyout = new MenuFlyout();

        // ----- Bulk actions when multiple rows are selected (and the clicked row is among them) -----
        var selected = ResultsGrid.SelectedItems.OfType<LogLine>().ToList();
        if (selected.Count > 1 && selected.Any(l => l.RowId == row.RowId))
        {
            flyout.Items.Add(new MenuFlyoutItem { Text = $"{selected.Count:N0} rows selected", IsEnabled = false });

            var copySelJson = new MenuFlyoutItem { Text = $"Copy {selected.Count:N0} selected as JSON", Icon = new SymbolIcon(Symbol.Copy) };
            copySelJson.Click += (_, __) => CopyToClipboard(RowsAsJson(selected));
            flyout.Items.Add(copySelJson);

            var copySelCsv = new MenuFlyoutItem { Text = $"Copy {selected.Count:N0} selected as CSV" };
            copySelCsv.Click += (_, __) => CopyToClipboard(RowsAsCsv(selected));
            flyout.Items.Add(copySelCsv);

            var tagSelSub = new MenuFlyoutSubItem { Text = $"Tag {selected.Count:N0} selected" };
            foreach (var (name, _) in TagOptions)
            {
                var capturedSel = name;
                var it = new MenuFlyoutItem { Text = name };
                it.Click += (_, __) =>
                {
                    foreach (var l in selected)
                    {
                        var note = _rowTags.TryGetValue(l.RowId, out var ex) ? ex.Text : null;
                        _rowTags[l.RowId] = new RowTag(capturedSel, note);
                    }
                    RerenderRowsPreservingView();
                };
                tagSelSub.Items.Add(it);
            }
            tagSelSub.Items.Add(new MenuFlyoutSeparator());
            var clearSel = new MenuFlyoutItem { Text = "Clear tags" };
            clearSel.Click += (_, __) => { foreach (var l in selected) _rowTags.Remove(l.RowId); RerenderRowsPreservingView(); };
            tagSelSub.Items.Add(clearSel);
            flyout.Items.Add(tagSelSub);

            flyout.Items.Add(new MenuFlyoutSeparator());
        }

        // ----- Tag (mark this row) -----
        var key = row.RowId;
        var tagSub = new MenuFlyoutSubItem { Text = "Tag" };
        _rowTags.TryGetValue(key, out var currentTag);
        foreach (var (name, _) in TagOptions)
        {
            var capturedName = name;
            var item = new MenuFlyoutItem { Text = name };
            if (!string.IsNullOrEmpty(currentTag.Name) && string.Equals(currentTag.Name, name, StringComparison.OrdinalIgnoreCase))
                item.Icon = new SymbolIcon(Symbol.Accept); // checkmark on the active tag
            // Changing the category preserves any existing note.
            item.Click += (_, __) =>
            {
                var note = _rowTags.TryGetValue(key, out var ex) ? ex.Text : null;
                _rowTags[key] = new RowTag(capturedName, note);
                RefreshRow();
            };
            tagSub.Items.Add(item);
        }
        tagSub.Items.Add(new MenuFlyoutSeparator());
        var noteItem = new MenuFlyoutItem
        {
            Text = string.IsNullOrEmpty(currentTag.Text) ? "Add note…" : "Edit note…",
            Icon = new SymbolIcon(Symbol.Edit),
        };
        noteItem.Click += async (_, __) => await EditTagNoteAsync(key, RefreshRow);
        tagSub.Items.Add(noteItem);
        tagSub.Items.Add(new MenuFlyoutSeparator());
        var clearTag = new MenuFlyoutItem { Text = "Clear tag" };
        clearTag.Click += (_, __) => { _rowTags.Remove(key); RefreshRow(); };
        tagSub.Items.Add(clearTag);
        flyout.Items.Add(tagSub);
        flyout.Items.Add(new MenuFlyoutSeparator());

        if (!string.IsNullOrEmpty(cellColumnHeader))
        {
            var cellValue = GetRowField(row, cellColumnHeader);
            var copyCell = new MenuFlyoutItem
            {
                Text = $"Copy cell ({cellColumnHeader})",
                Icon = new SymbolIcon(Symbol.Copy),
            };
            copyCell.Click += (_, __) => CopyToClipboard(cellValue ?? "");
            flyout.Items.Add(copyCell);
            flyout.Items.Add(new MenuFlyoutSeparator());
        }

        var copyJson = new MenuFlyoutItem { Text = "Copy row as JSON" };
        copyJson.Click += (_, __) => CopyToClipboard(RowAsJson(row));
        flyout.Items.Add(copyJson);

        var copyCsv = new MenuFlyoutItem { Text = "Copy row as CSV" };
        copyCsv.Click += (_, __) => CopyToClipboard(RowAsCsv(row));
        flyout.Items.Add(copyCsv);

        var copyXml = new MenuFlyoutItem { Text = "Copy row as XML" };
        copyXml.Click += (_, __) => CopyToClipboard(RowAsXml(row));
        flyout.Items.Add(copyXml);

        // Anchor on ResultsGrid and position at the click point so the menu lands under the
        // cursor regardless of which visual-tree descendant raised the event.
        flyout.ShowAt(ResultsGrid, e.GetPosition(ResultsGrid));
        e.Handled = true;
    }

    /// <summary>
    /// CommunityToolkit's <c>DataGridCell.OwningColumn</c> is <c>internal</c>, so reflection
    /// is the only way to read it from outside the assembly. Falls back to walking up to the
    /// parent <c>DataGridColumnHeader</c> if reflection fails for whatever reason. Header
    /// strings are stable (we set them in XAML) and aren't likely to change at runtime.
    /// </summary>
    /// <summary>
    /// Prompt for a free-text note on a row's tag. If the row has no category yet, applies "Note"
    /// so the tag (and its glyph) appears. Empty text is allowed (keeps the tag, clears the note).
    /// </summary>
    private async System.Threading.Tasks.Task EditTagNoteAsync(long rowId, Action refreshRow)
    {
        _rowTags.TryGetValue(rowId, out var existing);
        var tb = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Width = 380,
            Height = 120,
            PlaceholderText = "Note for this row",
            Text = existing.Text ?? "",
        };
        var dialog = new ContentDialog
        {
            Title = "Tag note",
            Content = tb,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var name = string.IsNullOrEmpty(existing.Name) ? TagOptions[3].Name : existing.Name; // default "Note"
            _rowTags[rowId] = new RowTag(name, tb.Text);
            refreshRow();
        }
    }

    /// <summary>If the right-click landed on a DataGrid column header, return that column's name.</summary>
    private static string TryGetRightClickedHeader(DependencyObject src)
    {
        var node = src;
        while (node != null)
        {
            if (node is ContentControl cc && node.GetType().Name == "DataGridColumnHeader")
                return cc.Content as string;
            node = VisualTreeHelper.GetParent(node);
        }
        return null;
    }

    /// <summary>Viewer column → the enrichment field that fills it ("Provider" is fed by Source).
    /// Null = the column isn't directly settable by an extract rule.</summary>
    private static string SettableFieldFor(string column) => column switch
    {
        "TaskName" => "TaskName",
        "Provider" => "Source",
        "ProcessId" => "ProcessId",
        "ProcessName" => "ProcessName",
        "ThreadId" => "ThreadId",
        "EventId" => "EventId",
        "ActivityId" => "ActivityId",
        "Channel" => "Channel",
        "OpCode" => "OpCode",
        _ => null,
    };

    private static string TruncMid(string s, int n) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n) + "…");

    /// <summary>Build a session viewer rule from the dialog inputs. Extract mode also strips the match
    /// (so the value isn't duplicated in the message), matching the bundled *-fields rules. Returns
    /// null if the regex is invalid.</summary>
    private static FindNeedleUX.Services.ViewerQuickRule BuildQuickRule(string column, string field, string pattern, bool extract)
    {
        var gm = System.Text.RegularExpressions.Regex.Match(pattern ?? "", @"\(\?<(\w+)>");
        string effPattern = gm.Success ? pattern : "(?<v>" + (pattern ?? "") + ")";
        string grp = gm.Success ? gm.Groups[1].Value : "v";
        try
        {
            var rx = new System.Text.RegularExpressions.Regex(
                extract ? effPattern : pattern,
                System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(250));
            return new FindNeedleUX.Services.ViewerQuickRule
            {
                Pattern = rx, CaptureGroup = grp, TargetField = extract ? field : null, Strip = true, ColumnLabel = column,
            };
        }
        catch { return null; }
    }

    private async System.Threading.Tasks.Task ShowColumnQuickRuleDialogAsync(string column)
    {
        var settable = SettableFieldFor(column);
        var samples = ViewModel.Results.Select(r => r.Message).Where(s => !string.IsNullOrEmpty(s)).Take(12).ToList();

        var pattern = new TextBox
        {
            PlaceholderText = @"regex — e.g. PID=(?<v>\d+)   (the (?<v>…) group is the captured value)",
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
        };
        var extractRadio = new RadioButton
        {
            Content = settable != null ? $"Extract a value into the {column} column" : $"Extract (the {column} column can't be set)",
            IsChecked = settable != null, IsEnabled = settable != null, GroupName = "qr",
        };
        var stripRadio = new RadioButton { Content = "Strip the matched text from the Message", IsChecked = settable == null, GroupName = "qr" };
        var preview = new TextBlock
        {
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 12,
            TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
        };

        void Refresh()
        {
            if (string.IsNullOrWhiteSpace(pattern.Text)) { preview.Text = "Type a regex to preview…"; return; }
            bool extract = extractRadio.IsChecked == true && settable != null;
            var rule = BuildQuickRule(column, settable ?? column, pattern.Text, extract);
            if (rule == null) { preview.Text = "⚠ Invalid regex"; return; }
            var sb = new System.Text.StringBuilder();
            int shown = 0, changed = 0;
            foreach (var msg in samples)
            {
                var (matched, captured, after) = rule.Evaluate(msg);
                if (matched) changed++;
                if (shown < 8 && (matched || shown < 3))
                {
                    var val = extract && !string.IsNullOrEmpty(captured) ? $"   → {column}={captured}" : "";
                    sb.Append("• ").Append(TruncMid(msg, 92)).Append('\n');
                    sb.Append("   ⇒ ").Append(TruncMid(after, 92)).Append(val).Append("\n\n");
                    shown++;
                }
            }
            sb.Append($"Matched {changed} of {samples.Count} sampled rows.");
            preview.Text = sb.ToString();
        }
        pattern.TextChanged += (_, _) => Refresh();
        extractRadio.Checked += (_, _) => Refresh();
        stripRadio.Checked += (_, _) => Refresh();

        var panel = new StackPanel { Spacing = 8, MinWidth = 480 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Pull a value out of the Message into the {column} column, or strip matching text. Applies to the open results instantly — no re-scan; cleared on restart.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });
        panel.Children.Add(pattern);
        panel.Children.Add(extractRadio);
        panel.Children.Add(stripRadio);
        panel.Children.Add(new TextBlock { Text = "Preview (current page)", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
        panel.Children.Add(new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
            CornerRadius = new CornerRadius(4), Padding = new Thickness(8), Child = preview,
        });

        // A "Clear quick rules" secondary button when any session rule is already active.
        bool anyActive = FindNeedleUX.Services.ViewerQuickRulesStore.Any;
        var dlg = new ContentDialog
        {
            Title = $"Quick rule — {column}",
            Content = new ScrollViewer { Content = panel, MaxHeight = 440 },
            PrimaryButtonText = "Apply",
            SecondaryButtonText = anyActive ? "Clear all quick rules" : null,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };
        Refresh();
        var result = await dlg.ShowAsync();
        if (result == ContentDialogResult.Secondary)
        {
            FindNeedleUX.Services.ViewerQuickRulesStore.Clear();
            ViewModel.RefreshNow();
            return;
        }
        if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(pattern.Text)) return;
        bool ex2 = extractRadio.IsChecked == true && settable != null;
        var rule = BuildQuickRule(column, settable ?? column, pattern.Text, ex2);
        if (rule == null) return;
        FindNeedleUX.Services.ViewerQuickRulesStore.Add(rule);
        ViewModel.RefreshNow(); // re-fetch the current page → rows rebuild with the rule applied (no re-scan)
    }

    private static string TryGetCellColumnHeader(CommunityToolkit.WinUI.UI.Controls.DataGridCell cell)
    {
        try
        {
            var prop = cell.GetType().GetProperty(
                "OwningColumn",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var col = prop?.GetValue(cell) as CommunityToolkit.WinUI.UI.Controls.DataGridColumn;
            return col?.Header as string;
        }
        catch { return null; }
    }

    private static string GetRowField(LogLine line, string column) => column switch
    {
        "Index"    => line.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "Time"     => line.Time,
        "Provider" => line.Provider,
        "TaskName" => line.TaskName,
        "Message"  => line.Message,
        "Source"   => line.Source,
        "Level"    => line.Level,
        _          => null
    };

    /// <summary>
    /// Serialise every populated field on the row. We hand the whole LogLine to JsonSerializer
    /// (rather than building a Dictionary) so any field added to LogLine later is included
    /// automatically — same shape as the existing row-details "Copy as JSON" button.
    /// </summary>
    private static string RowAsJson(LogLine line)
    {
        return System.Text.Json.JsonSerializer.Serialize(line,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>JSON array of multiple rows (for "copy N selected").</summary>
    private static string RowsAsJson(System.Collections.Generic.IReadOnlyList<LogLine> rows)
        => System.Text.Json.JsonSerializer.Serialize(rows,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    /// <summary>CSV with one header + one line per row (same columns as <see cref="RowAsCsv"/>).</summary>
    private static string RowsAsCsv(System.Collections.Generic.IReadOnlyList<LogLine> rows)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("Index,Time,Provider,TaskName,Message,Source,Level,MachineName,Username,OpCode\n");
        foreach (var line in rows)
        {
            sb.Append(EscapeCsv(line.Index.ToString(System.Globalization.CultureInfo.InvariantCulture))).Append(',');
            sb.Append(EscapeCsv(line.Time)).Append(',');
            sb.Append(EscapeCsv(line.Provider)).Append(',');
            sb.Append(EscapeCsv(line.TaskName)).Append(',');
            sb.Append(EscapeCsv(line.Message)).Append(',');
            sb.Append(EscapeCsv(line.Source)).Append(',');
            sb.Append(EscapeCsv(line.Level)).Append(',');
            sb.Append(EscapeCsv(line.MachineName)).Append(',');
            sb.Append(EscapeCsv(line.Username)).Append(',');
            sb.Append(EscapeCsv(line.OpCode)).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Two-line CSV with header + values. Column order matches the DataGrid's default. Includes
    /// the less-common fields too so users pasting into a spreadsheet get the full row.
    /// </summary>
    private static string RowAsCsv(LogLine line)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("Index,Time,Provider,TaskName,Message,Source,Level,MachineName,Username,OpCode");
        sb.Append('\n');
        sb.Append(EscapeCsv(line.Index.ToString(System.Globalization.CultureInfo.InvariantCulture))).Append(',');
        sb.Append(EscapeCsv(line.Time)).Append(',');
        sb.Append(EscapeCsv(line.Provider)).Append(',');
        sb.Append(EscapeCsv(line.TaskName)).Append(',');
        sb.Append(EscapeCsv(line.Message)).Append(',');
        sb.Append(EscapeCsv(line.Source)).Append(',');
        sb.Append(EscapeCsv(line.Level)).Append(',');
        sb.Append(EscapeCsv(line.MachineName)).Append(',');
        sb.Append(EscapeCsv(line.Username)).Append(',');
        sb.Append(EscapeCsv(line.OpCode));
        return sb.ToString();
    }

    private static string EscapeCsv(string v)
    {
        if (string.IsNullOrEmpty(v)) return "";
        if (v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return v;
        return "\"" + v.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>
    /// XML form with one element per field. Element content is escaped via SecurityElement.Escape.
    /// </summary>
    private static string RowAsXml(LogLine line)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<row>");
        AppendXmlField(sb, "Index",       line.Index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AppendXmlField(sb, "Time",        line.Time);
        AppendXmlField(sb, "Provider",    line.Provider);
        AppendXmlField(sb, "TaskName",    line.TaskName);
        AppendXmlField(sb, "Message",     line.Message);
        AppendXmlField(sb, "Source",      line.Source);
        AppendXmlField(sb, "Level",       line.Level);
        AppendXmlField(sb, "MachineName", line.MachineName);
        AppendXmlField(sb, "Username",    line.Username);
        AppendXmlField(sb, "OpCode",      line.OpCode);
        sb.Append("</row>");
        return sb.ToString();
    }

    private static void AppendXmlField(System.Text.StringBuilder sb, string name, string value)
    {
        if (string.IsNullOrEmpty(value)) { sb.Append("  <").Append(name).Append(" />\n"); return; }
        sb.Append("  <").Append(name).Append('>');
        sb.Append(System.Security.SecurityElement.Escape(value));
        sb.Append("</").Append(name).Append(">\n");
    }

    private static void CopyToClipboard(string text)
    {
        var pkg = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
        pkg.SetText(text ?? "");
        global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
    }

    // ===== MCP viewer controller (IMcpViewerController) =====
    // Lets the MCP bridge read and drive THIS live view. Every method hops to the UI thread so the
    // grid updates as the agent acts; reads return plain DTOs (no WinUI types).

    private const int McpMessageCap = 300; // truncate Message in list results (token budget)

    private Task McpOnUiAsync(Action a)
    {
        var dq = DispatcherQueue;
        if (dq == null || dq.HasThreadAccess) { a(); return Task.CompletedTask; }
        var tcs = new TaskCompletionSource();
        if (!dq.TryEnqueue(() => { try { a(); tcs.TrySetResult(); } catch (Exception ex) { tcs.TrySetException(ex); } }))
            a();
        return tcs.Task;
    }

    /// <summary>Marshal an async action to the UI thread and await its completion (so the caller can
    /// await e.g. a reload finishing before reading results).</summary>
    private Task McpOnUiAsync(Func<System.Threading.Tasks.Task> f)
    {
        var dq = DispatcherQueue;
        if (dq == null || dq.HasThreadAccess) return f();
        var tcs = new TaskCompletionSource();
        if (!dq.TryEnqueue(async () => { try { await f(); tcs.TrySetResult(); } catch (Exception ex) { tcs.TrySetException(ex); } }))
            return f();
        return tcs.Task;
    }

    /// <summary>MCP: reload the viewer so it rebinds to the current (freshly-run) streaming search.</summary>
    public Task ReloadAsync() => McpOnUiAsync(() => ReloadResultsAsync());

    private Task<T> McpOnUiAsync<T>(Func<T> f)
    {
        var dq = DispatcherQueue;
        if (dq == null || dq.HasThreadAccess) return Task.FromResult(f());
        var tcs = new TaskCompletionSource<T>();
        if (!dq.TryEnqueue(() => { try { tcs.TrySetResult(f()); } catch (Exception ex) { tcs.TrySetException(ex); } }))
            return Task.FromResult(f());
        return tcs.Task;
    }

    /// <summary>Marshal an async function returning a value to the UI thread and await it.</summary>
    private Task<T> McpOnUiAsync<T>(Func<System.Threading.Tasks.Task<T>> f)
    {
        var dq = DispatcherQueue;
        if (dq == null || dq.HasThreadAccess) return f();
        var tcs = new TaskCompletionSource<T>();
        if (!dq.TryEnqueue(async () => { try { tcs.TrySetResult(await f()); } catch (Exception ex) { tcs.TrySetException(ex); } }))
            return f();
        return tcs.Task;
    }

    /// <summary>MCP: turn the rule-view filter on/off — applies the active filter/redact rules to the
    /// open view (the same thing the toolbar's rule-filter toggle does), so an agent can see a
    /// filter rule's effect without the UI. Returns the kept row count when on, the unfiltered count
    /// when off, or -1 if turning on with no filter rules among the active set.</summary>
    public Task<int> SetRuleViewFilterAsync(bool on) => McpOnUiAsync(async () =>
    {
        var ruleFiles = MiddleLayerService.LastRuleProcessors?
            .Select(p => p.RulesFilePath)
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new System.Collections.Generic.List<string>();
        if (on && ruleFiles.Count == 0)
        {
            if (RuleFilterToggle != null) RuleFilterToggle.IsChecked = false;
            return -1;
        }
        int kept = await ViewModel.SetRuleViewFilterAsync(on, ruleFiles);
        if (RuleFilterToggle != null) RuleFilterToggle.IsChecked = on && kept >= 0;
        RebindGrid();
        UpdateEmptyState();
        return on ? kept : ViewModel.TotalFilteredCount;
    });

    private FindNeedleUX.Services.Mcp.RecordDto McpToRecord(LogLine l, bool full)
    {
        if (l == null) return null;
        bool tagged = _rowTags.TryGetValue(l.RowId, out var rt);
        var dto = new FindNeedleUX.Services.Mcp.RecordDto
        {
            RowId = l.RowId,
            Index = l.Index,
            Time = l.Time,
            Level = l.Level,
            Provider = l.Provider,
            TaskName = l.TaskName,
            Source = l.Source,
            ProcessId = l.ProcessId,
            ThreadId = l.ThreadId,
            EventId = l.EventId, // cheap + a common field-extraction target, so surface it on list rows too
            Message = full ? l.Message : McpTruncate(l.Message, McpMessageCap),
            Tag = tagged ? rt.Name : null,
            TagText = tagged ? rt.Text : null,
        };
        if (full)
        {
            dto.MachineName = l.MachineName;
            dto.Username = l.Username;
            dto.OpCode = l.OpCode;
            dto.SearchableData = l.SearchableData;
            dto.ProcessName = l.ProcessName;
            dto.ActivityId = l.ActivityId;
            dto.Keywords = l.Keywords;
            dto.Channel = l.Channel;
        }
        return dto;
    }

    private static string McpTruncate(string s, int cap)
        => string.IsNullOrEmpty(s) || s.Length <= cap ? s : s.Substring(0, cap) + "…";

    public Task<FindNeedleUX.Services.Mcp.ViewStateDto> GetViewAsync() => McpOnUiAsync(() =>
        new FindNeedleUX.Services.Mcp.ViewStateDto
        {
            Search = ViewModel.SearchText,
            Provider = ViewModel.ProviderFilter,
            TaskName = ViewModel.TaskNameFilter,
            Message = ViewModel.MessageFilter,
            Source = ViewModel.SourceFilter,
            Level = ViewModel.LevelFilter,
            FromTime = ViewModel.FromDate?.ToString("o"),
            ToTime = ViewModel.ToDate?.ToString("o"),
            SortColumn = ViewModel.SortColumn,
            SortDescending = ViewModel.SortDescending,
            CurrentPage = ViewModel.CurrentPage,
            PageSize = ViewModel.PageSize,
            TotalPages = ViewModel.TotalPages,
            TotalFiltered = ViewModel.TotalFilteredCount,
            Total = ViewModel.TotalCount,
            DetailsMode = _detailsMode.ToString(),
        });

    public Task<FindNeedleUX.Services.Mcp.PageDto> GetPageAsync(int? offset, int limit) => McpOnUiAsync(() =>
    {
        if (limit <= 0) limit = ViewModel.PageSize;
        if (limit > 500) limit = 500; // token-budget hard cap
        int off = offset ?? (ViewModel.CurrentPage - 1) * ViewModel.PageSize;
        if (off < 0) off = 0;
        var rows = ViewModel.GetRows(off, limit);
        var dto = new FindNeedleUX.Services.Mcp.PageDto
        {
            Offset = off,
            Limit = limit,
            TotalFiltered = ViewModel.TotalFilteredCount,
            Total = ViewModel.TotalCount,
        };
        foreach (var r in rows) dto.Rows.Add(McpToRecord(r, full: false));
        return dto;
    });

    public Task<FindNeedleUX.Services.Mcp.RecordDto> GetRecordAsync(long rowId) =>
        McpOnUiAsync(() => McpToRecord(ViewModel.GetRecordByRowId(rowId), full: true));

    public Task<FindNeedleUX.Services.Mcp.SummaryDto> GetSummaryAsync() => McpOnUiAsync(() =>
    {
        var (min, max) = ViewModel.GetFilteredTimeRange();
        var dto = new FindNeedleUX.Services.Mcp.SummaryDto
        {
            Total = ViewModel.TotalCount,
            TotalFiltered = ViewModel.TotalFilteredCount,
            FromTime = min?.ToString("o"),
            ToTime = max?.ToString("o"),
            Levels = ViewModel.GetFilteredLevelCounts(),
        };
        foreach (var loc in MiddleLayerService.Locations)
        {
            string name, desc;
            try { name = loc.GetName(); } catch { name = "(unknown)"; }
            try { desc = loc.GetDescription(); } catch { desc = ""; }
            dto.Sources.Add(new FindNeedleUX.Services.Mcp.LocationDto
            {
                Name = name, Description = desc,
                IsEditable = loc is KustoPlugin.Location.KustoLocation,
            });
        }
        var rules = MiddleLayerService.SearchQueryUX?.CurrentQuery?.RulesConfigPaths;
        if (rules != null) dto.Rules.AddRange(rules);
        return dto;
    });

    public Task<List<FindNeedleUX.Services.Mcp.HistogramBucketDto>> GetHistogramAsync(int buckets) => McpOnUiAsync(() =>
    {
        if (buckets <= 0) buckets = 20;
        if (buckets > 200) buckets = 200;
        var result = new List<FindNeedleUX.Services.Mcp.HistogramBucketDto>();
        var (min, max) = ViewModel.GetFilteredTimeRange();
        if (min == null || max == null) return result;
        var start = min.Value;
        var span = max.Value - start;
        if (span <= TimeSpan.Zero)
        {
            result.Add(new FindNeedleUX.Services.Mcp.HistogramBucketDto
            { Start = start.ToString("o"), Count = ViewModel.TotalFilteredCount });
            return result;
        }
        var width = TimeSpan.FromTicks(span.Ticks / buckets);
        for (int i = 0; i < buckets; i++)
        {
            var lo = start + TimeSpan.FromTicks(width.Ticks * i);
            // Last bucket is inclusive of max; widen its upper bound slightly so max rows count.
            var hi = i == buckets - 1 ? max.Value : lo + width;
            result.Add(new FindNeedleUX.Services.Mcp.HistogramBucketDto
            {
                Start = lo.ToString("o"),
                Count = ViewModel.CountInTimeRange(lo, hi),
            });
        }
        return result;
    });

    // Facets / patterns scan up to a sample cap and are pure reads — run them on the threadpool so a
    // big log doesn't freeze the UI. The paged source's GetPage is already used off-thread elsewhere.
    public Task<FindNeedleUX.Services.Mcp.LogAnalysis.FacetResult> GetFacetsAsync(string field, int limit, int sampleCap)
        => System.Threading.Tasks.Task.Run(() => ViewModel.GetFacets(field, limit, sampleCap));

    public Task<FindNeedleUX.Services.Mcp.LogAnalysis.PatternResult> GetTopPatternsAsync(int limit, int sampleCap)
        => System.Threading.Tasks.Task.Run(() => ViewModel.GetTopPatterns(limit, sampleCap));

    public Task<int> SetFilterAsync(string search, string provider, string taskName, string message,
        string source, string level, string fromTime, string toTime) => McpOnUiAsync(() =>
    {
        DateTime? from = null, to = null;
        bool clearFrom = fromTime == "", clearTo = toTime == "";
        if (!string.IsNullOrEmpty(fromTime) && DateTime.TryParse(fromTime, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var pf)) from = pf;
        if (!string.IsNullOrEmpty(toTime) && DateTime.TryParse(toTime, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var pt)) to = pt;

        // Keep the visible filter controls in sync so the agent's change shows in the UI.
        if (search != null) SearchBox.Text = search;
        if (provider != null) ProviderFilterBox.Text = provider;
        if (taskName != null) TaskNameFilterBox.Text = taskName;
        if (message != null) MessageFilterBox.Text = message;
        if (source != null) SourceFilterBox.Text = source;
        if (level != null) LevelFilterCombo.SelectedItem = string.IsNullOrEmpty(level) ? null : level;
        if (clearFrom) { FromDatePicker.Date = null; FromTimePicker.SelectedTime = null; }
        else if (from.HasValue) { FromDatePicker.Date = from.Value; FromTimePicker.SelectedTime = from.Value.TimeOfDay; }
        if (clearTo) { ToDatePicker.Date = null; ToTimePicker.SelectedTime = null; }
        else if (to.HasValue) { ToDatePicker.Date = to.Value; ToTimePicker.SelectedTime = to.Value.TimeOfDay; }

        // Authoritative single apply + count (the controls' own events would each reload separately).
        return ViewModel.SetFiltersBulk(search, provider, taskName, message, source, level,
            from, to, clearFrom, clearTo);
    });

    public Task<int> ClearFiltersAsync() => McpOnUiAsync(() =>
    {
        SearchBox.Text = "";
        ProviderFilterBox.Text = TaskNameFilterBox.Text = MessageFilterBox.Text = SourceFilterBox.Text = "";
        LevelFilterCombo.SelectedItem = null;
        FromDatePicker.Date = null;
        ToDatePicker.Date = null;
        FromTimePicker.SelectedTime = null;
        ToTimePicker.SelectedTime = null;
        ViewModel.ClearFilters();
        return ViewModel.TotalFilteredCount;
    });

    public Task SetSortAsync(string column, bool descending) => McpOnUiAsync(() =>
    {
        ViewModel.ApplySort(column, descending);
        // Reflect the sort arrow on the matching column header.
        foreach (var c in ResultsGrid.Columns)
        {
            if (string.Equals(c.Header?.ToString(), column, StringComparison.OrdinalIgnoreCase))
                c.SortDirection = descending ? DataGridSortDirection.Descending : DataGridSortDirection.Ascending;
            else
                c.SortDirection = null;
        }
    });

    public Task GoToPageAsync(int page) => McpOnUiAsync(() => ViewModel.GoToPage(page));

    public Task SetPageSizeAsync(int pageSize) => McpOnUiAsync(() =>
    {
        if (pageSize <= 0) return;
        ViewModel.PageSize = pageSize;
        ResultsViewerSettings.PageSize = pageSize;
    });

    public Task<bool> SelectRowAsync(long rowId) => McpOnUiAsync(() =>
    {
        foreach (var item in ViewModel.Results)
        {
            if (item.RowId == rowId)
            {
                ResultsGrid.SelectedItem = item;
                try { ResultsGrid.ScrollIntoView(item, null); } catch { /* best effort */ }
                return true;
            }
        }
        return false; // not on the current page — agent can page/filter to it first
    });

    public Task<bool> TagRowAsync(long rowId, string tag, string text) => McpOnUiAsync(() =>
    {
        _rowTags.TryGetValue(rowId, out var existing);
        // Category: use the given one, else keep the row's existing category.
        var name = string.IsNullOrWhiteSpace(tag) ? existing.Name : tag;
        if (string.IsNullOrWhiteSpace(name))
        {
            if (text != null) name = TagOptions[3].Name; // note-only → default category "Note"
            else return false;                            // nothing to tag with
        }
        if (!_tagColors.ContainsKey(name)) return false;         // unknown category
        // Note: null = keep existing; "" or a value = set it.
        var note = text ?? existing.Text;
        _rowTags[rowId] = new RowTag(name, note);
        RerenderRowsPreservingView(); // re-render so the tag glyph/tooltip updates immediately
        return true;
    });

    public Task<bool> ClearTagAsync(long rowId) => McpOnUiAsync(() =>
    {
        bool removed = _rowTags.Remove(rowId);
        if (removed) RerenderRowsPreservingView();
        return removed;
    });

    public Task SetDetailsModeAsync(string mode) => McpOnUiAsync(() =>
    {
        var m = (mode ?? "").Trim().ToLowerInvariant() switch
        {
            "bottom" or "bottompanel" or "panel" => DetailsMode.BottomPanel,
            "popup" => DetailsMode.Popup,
            _ => DetailsMode.Inrow,
        };
        SetDetailsMode(m);
    });

    public async Task<FindNeedleUX.Services.Mcp.ExportResultDto> ExportAsync(string format, string destPath)
    {
        var vmFormat = (format ?? "").Trim().ToLowerInvariant() switch
        {
            "json" => NativeResultViewer.NativeResultsPageViewModel.ExportFormat.Json,
            "xml"  => NativeResultViewer.NativeResultsPageViewModel.ExportFormat.Xml,
            _      => NativeResultViewer.NativeResultsPageViewModel.ExportFormat.Csv,
        };
        var (ext, _) = FindNeedleUX.Services.ResultExporter.FormatInfo(vmFormat switch
        {
            NativeResultViewer.NativeResultsPageViewModel.ExportFormat.Json => FindNeedleUX.Services.ResultExporter.Format.Json,
            NativeResultViewer.NativeResultsPageViewModel.ExportFormat.Xml  => FindNeedleUX.Services.ResultExporter.Format.Xml,
            _ => FindNeedleUX.Services.ResultExporter.Format.Csv,
        });
        var path = string.IsNullOrWhiteSpace(destPath)
            ? System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"findneedle-results-{DateTime.Now:yyyyMMdd-HHmmss}{ext}")
            : destPath;

        // Build + write off the UI thread (a large export streams many rows). Reads VM filter/sort as
        // a snapshot; SQLite reads are lock-safe.
        int count = await ViewModel.ExportToPathAsync(vmFormat, path).ConfigureAwait(false);
        return new FindNeedleUX.Services.Mcp.ExportResultDto
        {
            Path = count >= 0 ? path : null,
            RowCount = Math.Max(0, count),
        };
    }

    private static global::Windows.UI.Color ParseHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex.Equals("Transparent", StringComparison.OrdinalIgnoreCase))
            return global::Windows.UI.Color.FromArgb(0, 0, 0, 0);
        var s = hex.TrimStart('#');
        if (s.Length == 6) s = "FF" + s;
        if (s.Length != 8) return global::Windows.UI.Color.FromArgb(255, 255, 255, 255);
        return global::Windows.UI.Color.FromArgb(
            (byte)Convert.ToInt32(s.Substring(0, 2), 16),
            (byte)Convert.ToInt32(s.Substring(2, 2), 16),
            (byte)Convert.ToInt32(s.Substring(4, 2), 16),
            (byte)Convert.ToInt32(s.Substring(6, 2), 16));
    }
}
} // FindNeedleUX.Pages

namespace FindNeedleUX.Pages.NativeResultViewer
{

/// <summary>
/// Formats <see cref="DateTime"/> for the Time column in the native viewer.
/// The user picks a format from the toolbar; the chosen .NET format string is stored in
/// <see cref="Format"/> and consulted by every cell render.
/// </summary>
public class TimeFormatConverter : IValueConverter
{
    /// <summary>Default format. Changed by the settings page's Time-format dropdown.</summary>
    public static string Format
    {
        get => _format;
        set
        {
            if (_format != value)
            {
                _format = value;
                lock (_cache) _cache.Clear();
            }
        }
    }
    private static string _format = "yyyy-MM-dd HH:mm:ss";

    // Cache formatted timestamp strings by DateTime.Ticks. The Time column's Convert is the most-
    // called converter during virtualized scroll — without caching, every cell render allocates a
    // fresh string via dt.ToString(format). Adjacent log rows often share the same second, so
    // even a small cache catches most calls. Cleared whenever Format changes.
    //
    // Capped at 100k entries to bound memory; once full we just stop adding (existing entries
    // continue to serve cache hits). For a 24-hour log at second resolution that's 86,400 unique
    // timestamps — fits with room to spare.
    private const int CacheCap = 100_000;
    private static readonly System.Collections.Generic.Dictionary<long, string> _cache
        = new(capacity: 1024);

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not DateTime dt || dt == DateTime.MinValue) return "";
        var ticks = dt.Ticks;
        lock (_cache)
        {
            if (_cache.TryGetValue(ticks, out var cached)) return cached;
        }
        string formatted;
        try { formatted = dt.ToString(_format, System.Globalization.CultureInfo.CurrentCulture); }
        catch { formatted = dt.ToString("o"); }
        lock (_cache)
        {
            if (_cache.Count < CacheCap) _cache[ticks] = formatted;
        }
        return formatted;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts a hex color string ("#RRGGBB", "#AARRGGBB", "Transparent") to a SolidColorBrush.
/// Used by the level chip swatch + by code-behind for row backgrounds.
/// </summary>
public class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => Parse(value as string);
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();

    // Brushes are immutable per-color; share one instance per distinct hex string. Avoids
    // allocating a fresh SolidColorBrush per DataGrid row (LoadingRow fires during virtualized
    // scroll — for tens of thousands of visible rows this was the main scroll-jank source).
    private static readonly System.Collections.Generic.Dictionary<string, SolidColorBrush> _brushCache
        = new(StringComparer.OrdinalIgnoreCase);

    public static SolidColorBrush Parse(string hex)
    {
        var key = string.IsNullOrEmpty(hex) ? "Transparent" : hex;
        lock (_brushCache)
        {
            if (_brushCache.TryGetValue(key, out var cached)) return cached;
            var brush = new SolidColorBrush(ParseColor(hex));
            _brushCache[key] = brush;
            return brush;
        }
    }

    public static global::Windows.UI.Color ParseColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex.Equals("Transparent", StringComparison.OrdinalIgnoreCase))
            return Colors.Transparent;
        var s = hex.TrimStart('#');
        if (s.Length == 6) s = "FF" + s;
        if (s.Length != 8) return Colors.Transparent;
        try
        {
            return global::Windows.UI.Color.FromArgb(
                (byte)System.Convert.ToInt32(s.Substring(0, 2), 16),
                (byte)System.Convert.ToInt32(s.Substring(2, 2), 16),
                (byte)System.Convert.ToInt32(s.Substring(4, 2), 16),
                (byte)System.Convert.ToInt32(s.Substring(6, 2), 16));
        }
        catch
        {
            return Colors.Transparent;
        }
    }
}
} // FindNeedleUX.Pages.NativeResultViewer
