using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FindNeedleUX.Services;
using FindNeedleUX.Utils;
using FindNeedleUX.ViewModels;
using KustoPlugin.Location;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FindNeedleUX.Pages;

public sealed partial class SearchLocationsPage : Page
{
    private readonly SearchLocationsViewModel _viewModel = new();

    public SearchLocationsPage()
    {
        this.InitializeComponent();
        CheckOtherDLLs.AreWeInstalledOk();
        // Bind the repeater once; the VM refreshes its collection in place.
        VariedImageSizeRepeater.ItemsSource = _viewModel.Locations;
    }

    private void Button_Remove(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name })
        {
            _viewModel.RemoveLocation(name);
        }
    }

    private void Button_AddFolder(object sender, RoutedEventArgs e)
    {
        var window = WindowUtil.GetWindowForElement(this);
        var hWnd = WindowNative.GetWindowHandle(window);
        var path = FindNeedleUX.Services.Win32FileDialog.PickFolder(hWnd);
        if (path != null)
        {
            _viewModel.AddLocation(path);
        }
    }

    private void Button_AddFile(object sender, RoutedEventArgs e)
    {
        var window = WindowUtil.GetWindowForElement(this);
        var hWnd = WindowNative.GetWindowHandle(window);
        var path = FindNeedleUX.Services.Win32FileDialog.OpenFile(hWnd, new (string, string)[]
        {
            ("Log files", "*.txt;*.etl;*.log;*.zip;*.evtx"),
            ("All files", "*.*"),
        });
        if (path != null)
        {
            _viewModel.AddLocation(path);
        }
    }

    private async void Button_AddKusto(object sender, RoutedEventArgs e)
    {
        var loc = await ShowKustoDialogAsync(null);
        if (loc != null) { MiddleLayerService.AddLocation(loc); _viewModel.Refresh(); }
    }

    private async void Button_Edit(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name }) return;
        var existing = MiddleLayerService.Locations.OfType<KustoLocation>().FirstOrDefault(k => k.GetName() == name);
        if (existing == null) return; // only Kusto locations are editable today
        var updated = await ShowKustoDialogAsync(existing);
        if (updated == null) return;
        MiddleLayerService.RemoveLocationByName(name); // replace the old one (its name may have changed)
        MiddleLayerService.AddLocation(updated);
        _viewModel.Refresh();
    }

    /// <summary>
    /// Shows the Kusto cluster/database/query/auth dialog. Pre-filled from <paramref name="existing"/>
    /// when editing. Returns the configured location, or null if cancelled/incomplete.
    /// </summary>
    private async Task<KustoLocation> ShowKustoDialogAsync(KustoLocation existing)
    {
        var cluster = new TextBox
        {
            Header = "Cluster URI",
            PlaceholderText = "https://<name>.<region>.kusto.windows.net",
            Text = existing?.ClusterUri ?? "https://kvc-6u9h87febddjedr8ed.southcentralus.kusto.windows.net",
        };
        // RadioButtons (not a ComboBox) so there's no dropdown to get clipped by the dialog scroll.
        var auth = new RadioButtons { Header = "Sign-in", MaxColumns = 1 };
        auth.Items.Add("Interactive browser sign-in");
        auth.Items.Add("Azure CLI (az login)");
        auth.Items.Add("Device code");
        auth.SelectedIndex = existing != null ? (int)existing.AuthMode : 0;

        KustoAuthMode Mode() => auth.SelectedIndex switch
        {
            1 => KustoAuthMode.AzureCli,
            2 => KustoAuthMode.DeviceCode,
            _ => KustoAuthMode.Interactive,
        };

        // Row limit — Kusto caps results at 500k; raise or remove it (results stream to disk, so even
        // millions stay low-memory, but a big pull takes longer and loads the cluster).
        var rowLimit = new RadioButtons { Header = "Row limit", MaxColumns = 2 };
        rowLimit.Items.Add("Default (500,000)");
        rowLimit.Items.Add("Up to 1,000,000");
        rowLimit.Items.Add("Up to 5,000,000");
        rowLimit.Items.Add("All rows (no limit)");
        rowLimit.SelectedIndex = existing == null ? 0 : existing.RowLimit switch
        {
            1_000_000 => 1,
            5_000_000 => 2,
            < 0 => 3,
            _ => 0,
        };
        long SelectedRowLimit() => rowLimit.SelectedIndex switch
        {
            1 => 1_000_000,
            2 => 5_000_000,
            3 => -1,
            _ => 0,
        };

        Microsoft.UI.Xaml.Media.Brush Dim()
            => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

        // ---- small layout helpers for a cleaner, clearly-labelled dialog ----
        TextBlock Section(string t) => new()
        {
            Text = t,
            FontWeight = global::Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 6, 0, 0),
        };
        TextBlock Hint(string t) => new() { Text = t, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Dim() };
        Border Boxed(FrameworkElement child)
        {
            child.MinHeight = 64;
            return new Border
            {
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = child,
            };
        }

        // Database — type it, or Load and click from the inline list (no dropdown → nothing to clip).
        var dbBox = new TextBox { PlaceholderText = "Selected database (type, or pick below)", Text = existing?.Database ?? string.Empty };
        var dbFilter = new TextBox { PlaceholderText = "Filter databases…" };
        var dbList = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 160 };
        var loadDbBtn = new Button { Content = "Load", VerticalAlignment = VerticalAlignment.Bottom, MinWidth = 72 };
        var dbStatus = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Dim() };
        var allDbs = new List<string>();
        // Filter the (possibly very large) list as you type; ListView virtualizes the rest.
        void ApplyDbFilter()
        {
            var f = (dbFilter.Text ?? string.Empty).Trim();
            var items = string.IsNullOrEmpty(f)
                ? allDbs
                : allDbs.Where(d => d.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            dbList.ItemsSource = items;
            if (!string.IsNullOrWhiteSpace(dbBox.Text) && items.Contains(dbBox.Text)) dbList.SelectedItem = dbBox.Text;
        }
        dbFilter.TextChanged += (_, __) => ApplyDbFilter();
        loadDbBtn.Click += async (_, __) =>
        {
            if (string.IsNullOrWhiteSpace(cluster.Text)) { dbStatus.Text = "Enter a cluster URI first."; return; }
            var prev = loadDbBtn.Content; loadDbBtn.IsEnabled = false; loadDbBtn.Content = "…";
            dbStatus.Text = "Connecting… (you may be prompted to sign in the first time)";
            try
            {
                var clusterUri = cluster.Text; var mode = Mode();
                allDbs = await Task.Run(() => KustoLocation.GetDatabases(clusterUri, mode));
                ApplyDbFilter();
                dbStatus.Text = allDbs.Count > 0 ? $"{allDbs.Count} databases — filter/click to choose." : "No databases found.";
            }
            catch (Exception ex) { dbStatus.Text = "Failed to load databases: " + ex.Message; }
            finally { loadDbBtn.Content = prev; loadDbBtn.IsEnabled = true; }
        };
        dbList.SelectionChanged += (_, __) => { if (dbList.SelectedItem is string d) dbBox.Text = d; };

        // dbBox stretches; Load sits to its right, baseline-aligned.
        var dbRow = new Grid { ColumnSpacing = 8 };
        dbRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dbRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(dbBox, 0); Grid.SetColumn(loadDbBtn, 1);
        dbRow.Children.Add(dbBox); dbRow.Children.Add(loadDbBtn);

        var query = new TextBox
        {
            PlaceholderText = "MyTable | where Timestamp > ago(1h) | take 1000",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 110,
            Text = existing?.Query ?? string.Empty,
        };

        // Tables in the chosen database — click one to seed a query from it.
        var tables = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 160 };
        var tableFilter = new TextBox { PlaceholderText = "Filter tables…" };
        var loadTablesBtn = new Button { Content = "Load tables" };
        var tablesStatus = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Dim() };
        var allTables = new List<string>();
        void ApplyTableFilter()
        {
            var f = (tableFilter.Text ?? string.Empty).Trim();
            tables.ItemsSource = string.IsNullOrEmpty(f)
                ? allTables
                : allTables.Where(t => t.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }
        tableFilter.TextChanged += (_, __) => ApplyTableFilter();
        loadTablesBtn.Click += async (_, __) =>
        {
            var db = (dbBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(cluster.Text) || string.IsNullOrWhiteSpace(db))
            { tablesStatus.Text = "Pick a cluster + database first."; return; }
            var prev = loadTablesBtn.Content; loadTablesBtn.IsEnabled = false; loadTablesBtn.Content = "Loading…"; tablesStatus.Text = "Loading tables…";
            try
            {
                var clusterUri = cluster.Text; var mode = Mode();
                allTables = await Task.Run(() => KustoLocation.GetTables(clusterUri, db, mode));
                ApplyTableFilter();
                tablesStatus.Text = allTables.Count > 0 ? $"{allTables.Count} tables — filter/click to build a query." : "No tables found.";
            }
            catch (Exception ex) { tablesStatus.Text = "Failed to load tables: " + ex.Message; }
            finally { loadTablesBtn.Content = prev; loadTablesBtn.IsEnabled = true; }
        };
        tables.SelectionChanged += (_, __) =>
        {
            if (tables.SelectedItem is string t) query.Text = $"['{t}']\n| take 100";
        };

        // Preview the query's first rows before adding it.
        var previewBtn = new Button { Content = "Run preview" };
        var previewOut = new TextBox
        {
            IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 12,
            MinHeight = 80, MaxHeight = 180, PlaceholderText = "Preview results appear here",
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(previewOut, ScrollBarVisibility.Auto);
        previewBtn.Click += async (_, __) =>
        {
            var db = (dbBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(cluster.Text) || string.IsNullOrWhiteSpace(db) || string.IsNullOrWhiteSpace(query.Text))
            { previewOut.Text = "Enter cluster + database + query first."; return; }
            var prev = previewBtn.Content; previewBtn.IsEnabled = false; previewBtn.Content = "Running…"; previewOut.Text = "Running…";
            try
            {
                var clusterUri = cluster.Text; var q = query.Text; var mode = Mode();
                var (cols, rows, truncated) = await Task.Run(() => KustoLocation.PreviewQuery(clusterUri, db, q, mode, 50));
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(string.Join("\t", cols));
                foreach (var r in rows) sb.AppendLine(string.Join("\t", r));
                sb.AppendLine();
                sb.Append(truncated ? $"(showing first {rows.Count} rows)" : $"({rows.Count} rows)");
                previewOut.Text = sb.ToString();
            }
            catch (Exception ex) { previewOut.Text = KustoLocation.FriendlyError(ex, query.Text); }
            finally { previewBtn.Content = prev; previewBtn.IsEnabled = true; }
        };

        var dbGroup = new StackPanel { Spacing = 4 };
        dbGroup.Children.Add(Section("Database"));
        dbGroup.Children.Add(dbRow);
        dbGroup.Children.Add(Hint("Databases on the cluster (filter, then click to choose):"));
        dbGroup.Children.Add(dbFilter);
        dbGroup.Children.Add(Boxed(dbList));
        dbGroup.Children.Add(dbStatus);

        var tablesGroup = new StackPanel { Spacing = 4 };
        tablesGroup.Children.Add(Section("Tables"));
        tablesGroup.Children.Add(loadTablesBtn);
        tablesGroup.Children.Add(Hint("Tables in the database (filter, then click to start a query):"));
        tablesGroup.Children.Add(tableFilter);
        tablesGroup.Children.Add(Boxed(tables));
        tablesGroup.Children.Add(tablesStatus);

        var queryGroup = new StackPanel { Spacing = 4 };
        queryGroup.Children.Add(Section("KQL query"));
        queryGroup.Children.Add(query);

        var previewGroup = new StackPanel { Spacing = 4 };
        previewGroup.Children.Add(Section("Preview"));
        previewGroup.Children.Add(previewBtn);
        previewGroup.Children.Add(previewOut);

        var panel = new StackPanel { Spacing = 8, MinWidth = 540 };
        panel.Children.Add(cluster);
        panel.Children.Add(auth);
        panel.Children.Add(rowLimit);
        panel.Children.Add(dbGroup);
        panel.Children.Add(tablesGroup);
        panel.Children.Add(queryGroup);
        panel.Children.Add(previewGroup);

        var dialog = new ContentDialog
        {
            Title = existing == null ? "Add Kusto location" : "Edit Kusto location",
            Content = new ScrollViewer
            {
                Content = panel, MaxHeight = 620,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                // Inset the content so the vertical scrollbar doesn't sit on top of the textboxes.
                Padding = new Thickness(0, 0, 16, 0),
            },
            PrimaryButtonText = existing == null ? "Add" : "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };
        // Let the dialog grow wider than the default (~548px) so the inset content has room.
        dialog.Resources["ContentDialogMaxWidth"] = 760.0;
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;

        var database = (dbBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cluster.Text) || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(query.Text))
            return null;

        return new KustoLocation(cluster.Text, database, query.Text, Mode(), SelectedRowLimit());
    }
}
