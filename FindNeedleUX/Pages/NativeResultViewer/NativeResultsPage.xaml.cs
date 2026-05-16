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
public sealed partial class NativeResultsPage : Page
{
    private NativeResultsPageViewModel ViewModel { get; } = new();

    // Pre-built O(1) lookup from Level name -> LevelEntry, used by LoadingRow on every row that
    // gets virtualized in. Rebuilt whenever ViewModel.Levels changes (after load, after theme
    // change, after color edit).
    private readonly Dictionary<string, LevelEntry> _levelLookup =
        new(StringComparer.OrdinalIgnoreCase);

    public NativeResultsPage()
    {
        this.InitializeComponent();

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
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        LoadingOverlay.Visibility = Visibility.Visible;

        // Subscribe to settings changes per Loaded cycle. With NavigationCacheMode.Required,
        // the page survives navigation; Unloaded unsubscribes so a backgrounded viewer doesn't
        // react to settings changes, and re-subscribe here when it comes back to the foreground.
        // Idempotent: subtract-then-add is safe even if we accidentally double-subscribe.
        ResultsViewerSettings.Changed -= OnSettingsChanged;
        ResultsViewerSettings.Changed += OnSettingsChanged;

        // Apply persisted prefs BEFORE rendering so the first paint already has the user's choices.
        ApplyPersistedSettings();
        ApplyPersistedColumnDefaults();
        ApplyFiltersToggleState(ResultsViewerSettings.FiltersExpanded);
        ApplyDetailsPanelToggleState(ResultsViewerSettings.DetailsPanelVisible);
        ApplyPersistedPageSize();
        await ViewModel.LoadResultsCommand.ExecuteAsync(null);
        // After load, re-apply persisted level color overrides — LoadResultsAsync() repopulates
        // ViewModel.Levels from the theme defaults, which would clobber overrides otherwise.
        ApplyPersistedLevelOverrides();
        LoadingOverlay.Visibility = Visibility.Collapsed;
        ApplyAllColumnVisibility();
        RebindGrid();

        // Tell MainWindow to hide the pre-nav spinner now that we're fully rendered.
        MainWindowActions.HideNavigationSpinner();
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

    private void ApplyFiltersToggleState(bool expanded)
    {
        FiltersToggle.IsChecked = expanded;
        FiltersPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        FiltersToggleGlyph.Text = expanded ? "▾" : "▸";
    }

    /// <summary>
    /// Switches between the two row-detail modes.
    ///   <c>visible=false</c> (default, "Inrow") — DataGrid shows an expandable details panel
    ///     beneath the selected row via <see cref="DataGrid.RowDetailsTemplate"/>, no separate
    ///     panel.
    ///   <c>visible=true</c> ("Details panel") — the inline expand is suppressed and a
    ///     persistent panel beneath the grid surfaces the selected row's full field set.
    /// </summary>
    private void ApplyDetailsPanelToggleState(bool visible)
    {
        DetailsPanelToggle.IsChecked = visible;
        DetailsPanelToggleGlyph.Text = visible ? "▾" : "▸";
        DetailsPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        // The two modes are mutually exclusive: in details-panel mode the inline row expand is
        // off, because having both would mean clicking a row puts the same data in two places.
        ResultsGrid.RowDetailsVisibilityMode = visible
            ? DataGridRowDetailsVisibilityMode.Collapsed
            : DataGridRowDetailsVisibilityMode.VisibleWhenSelected;

        // Re-populate so opening the panel immediately shows the current selection (or the
        // placeholder if nothing's selected).
        if (visible) RefreshDetailsPanel();
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
        ViewModel.DetachFromStreaming();
    }

    private void StopStreamingButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.StopStreaming();
    }

    private void OnSettingsChanged()
    {
        // Settings page edited the prefs while the viewer is open — reapply.
        DispatcherQueue.TryEnqueue(() =>
        {
            ApplyPersistedSettings();
            ApplyPersistedLevelOverrides();
            RebindGrid();
        });
    }

    private void ApplyPersistedSettings()
    {
        TimeFormatConverter.Format = ResultsViewerSettings.TimeFormat;
        ViewModel.ApplyTheme(ResultsViewerSettings.ThemeName);
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

    private void FiltersToggle_Click(object sender, RoutedEventArgs e)
    {
        var expanded = FiltersToggle.IsChecked == true;
        ApplyFiltersToggleState(expanded);
        ResultsViewerSettings.FiltersExpanded = expanded;
    }

    // ----- Details panel mode -----
    private void DetailsPanelToggle_Click(object sender, RoutedEventArgs e)
    {
        var visible = DetailsPanelToggle.IsChecked == true;
        ApplyDetailsPanelToggleState(visible);
        ResultsViewerSettings.DetailsPanelVisible = visible;
    }

    private void DetailsPanelClose_Click(object sender, RoutedEventArgs e)
    {
        // Closing the panel is equivalent to switching back to Inrow mode.
        ApplyDetailsPanelToggleState(false);
        ResultsViewerSettings.DetailsPanelVisible = false;
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

        AppendDetailRow("Index",       line.Index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AppendDetailRow("Time",        line.Time);
        AppendDetailRow("Provider",    line.Provider);
        AppendDetailRow("TaskName",    line.TaskName);
        AppendDetailRow("Message",     line.Message);
        AppendDetailRow("Source",      line.Source);
        AppendDetailRow("Level",       line.Level);
        AppendDetailRow("MachineName", line.MachineName);
        AppendDetailRow("Username",    line.Username);
        AppendDetailRow("OpCode",      line.OpCode);
    }

    private void AppendDetailRow(string label, string value)
    {
        int row = DetailsPanelGrid.RowDefinitions.Count;
        DetailsPanelGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var k = new TextBlock { Text = label, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        Grid.SetRow(k, row);
        Grid.SetColumn(k, 0);
        DetailsPanelGrid.Children.Add(k);

        var v = new TextBlock
        {
            Text = value ?? "",
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
        Grid.SetRow(v, row);
        Grid.SetColumn(v, 1);
        DetailsPanelGrid.Children.Add(v);
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
    /// Force the DataGrid to re-render rows. Used after a theme change (LoadingRow re-runs) or
    /// a Time-format change (the Binding Converter is consulted again).
    /// </summary>
    private void RebindGrid()
    {
        ResultsGrid.ItemsSource = null;
        ResultsGrid.ItemsSource = ViewModel.Results;
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
        if (e.Key == global::Windows.System.VirtualKey.Escape)
        {
            if (ColumnPanel.Visibility == Visibility.Visible)
            {
                ColumnPanel.Visibility = Visibility.Collapsed;
                e.Handled = true;
            }
        }
    }

    // ----- Search + filter inputs -----
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ViewModel.SearchText = SearchBox.Text;
    private void ProviderFilter_TextChanged(object sender, TextChangedEventArgs e) => ViewModel.ProviderFilter = ProviderFilterBox.Text;
    private void TaskNameFilter_TextChanged(object sender, TextChangedEventArgs e) => ViewModel.TaskNameFilter = TaskNameFilterBox.Text;
    private void MessageFilter_TextChanged(object sender, TextChangedEventArgs e)  => ViewModel.MessageFilter  = MessageFilterBox.Text;
    private void SourceFilter_TextChanged(object sender, TextChangedEventArgs e)   => ViewModel.SourceFilter   = SourceFilterBox.Text;
    private void LevelFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => ViewModel.LevelFilter = (LevelFilterCombo.SelectedItem as string) ?? "";

    private void FromDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
        => ViewModel.FromDate = sender.Date?.DateTime;
    private void ToDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
        => ViewModel.ToDate = sender.Date?.DateTime;

    private void ClearTimeRange_Click(object sender, RoutedEventArgs e)
    {
        FromDatePicker.Date = null;
        ToDatePicker.Date = null;
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
        Bullet("Esc closes the column visibility popover.");
        var dialog = new ContentDialog
        {
            Title = "Filtering & navigation",
            Content = stack,
            CloseButtonText = "Close",
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
    }

    // ----- Column visibility popover -----
    private void ColumnsButton_Click(object sender, RoutedEventArgs e)
        => ColumnPanel.Visibility = ColumnPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    private void CloseColumnPanel_Click(object sender, RoutedEventArgs e)
        => ColumnPanel.Visibility = Visibility.Collapsed;

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
        if (e.Row.DataContext is not LogLine line || string.IsNullOrEmpty(line.Level)) return;
        if (!_levelLookup.TryGetValue(line.Level, out var entry)) return;
        var brush = HexToBrushConverter.Parse(entry.HexColor);
        if (!ReferenceEquals(e.Row.Background, brush))
            e.Row.Background = brush;
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

        LogLine row = null;
        string cellColumnHeader = null;

        var node = src;
        while (node != null && (row == null || cellColumnHeader == null))
        {
            if (node is FrameworkElement fe)
            {
                if (row == null && fe.DataContext is LogLine ll) row = ll;
                if (cellColumnHeader == null
                    && fe is CommunityToolkit.WinUI.UI.Controls.DataGridCell cell)
                {
                    cellColumnHeader = TryGetCellColumnHeader(cell);
                }
            }
            node = VisualTreeHelper.GetParent(node);
        }
        if (row == null) return;

        var flyout = new MenuFlyout();

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
