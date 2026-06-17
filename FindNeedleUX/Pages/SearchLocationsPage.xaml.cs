using System;
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

    private async void Button_AddFolder(object sender, RoutedEventArgs e)
    {
        var window = WindowUtil.GetWindowForElement(this);
        var hWnd = WindowNative.GetWindowHandle(window);
        var picker = new FolderPicker { ViewMode = PickerViewMode.List };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, hWnd);
        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            _viewModel.AddLocation(folder.Path);
        }
    }

    private async void Button_AddFile(object sender, RoutedEventArgs e)
    {
        var window = WindowUtil.GetWindowForElement(this);
        var hWnd = WindowNative.GetWindowHandle(window);
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            FileTypeFilter = { ".txt", ".etl", ".log", ".zip", ".evtx" },
        };
        InitializeWithWindow.Initialize(picker, hWnd);
        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            _viewModel.AddLocation(file.Path);
        }
    }

    private async void Button_AddKusto(object sender, RoutedEventArgs e)
    {
        var cluster = new TextBox
        {
            Header = "Cluster URI",
            PlaceholderText = "https://<name>.<region>.kusto.windows.net",
            Text = "https://kvc-6u9h87febddjedr8ed.southcentralus.kusto.windows.net",
        };
        // RadioButtons (not a ComboBox) so there's no dropdown to get clipped by the dialog scroll.
        var auth = new RadioButtons { Header = "Sign-in", MaxColumns = 1 };
        auth.Items.Add("Interactive browser sign-in");
        auth.Items.Add("Azure CLI (az login)");
        auth.Items.Add("Device code");
        auth.SelectedIndex = 0;

        KustoAuthMode Mode() => auth.SelectedIndex switch
        {
            1 => KustoAuthMode.AzureCli,
            2 => KustoAuthMode.DeviceCode,
            _ => KustoAuthMode.Interactive,
        };

        Microsoft.UI.Xaml.Media.Brush Dim()
            => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

        // Database: type it, or Load and click from the inline list (no dropdown → nothing to clip).
        var dbBox = new TextBox { Header = "Database", PlaceholderText = "Load, or type a name" };
        var dbList = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 120 };
        var loadDbBtn = new Button { Content = "Load databases" };
        var dbStatus = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Dim() };
        loadDbBtn.Click += async (_, __) =>
        {
            if (string.IsNullOrWhiteSpace(cluster.Text)) { dbStatus.Text = "Enter a cluster URI first."; return; }
            var prev = loadDbBtn.Content; loadDbBtn.IsEnabled = false; loadDbBtn.Content = "Loading…";
            dbStatus.Text = "Connecting… (you may be prompted to sign in the first time)";
            try
            {
                var clusterUri = cluster.Text; var mode = Mode();
                var dbs = await Task.Run(() => KustoLocation.GetDatabases(clusterUri, mode));
                dbList.ItemsSource = dbs;
                dbStatus.Text = dbs.Count > 0 ? $"{dbs.Count} databases (click one)." : "No databases found.";
            }
            catch (Exception ex) { dbStatus.Text = "Failed to load databases: " + ex.Message; }
            finally { loadDbBtn.Content = prev; loadDbBtn.IsEnabled = true; }
        };
        dbList.SelectionChanged += (_, __) => { if (dbList.SelectedItem is string d) dbBox.Text = d; };

        var query = new TextBox
        {
            Header = "KQL query",
            PlaceholderText = "MyTable | where Timestamp > ago(1h) | take 1000",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 110,
        };

        // Tables in the chosen database — click one to seed a query from it.
        var tables = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 120 };
        var loadTablesBtn = new Button { Content = "Load tables" };
        var tablesStatus = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Dim() };
        loadTablesBtn.Click += async (_, __) =>
        {
            var db = (dbBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(cluster.Text) || string.IsNullOrWhiteSpace(db))
            { tablesStatus.Text = "Pick a cluster + database first."; return; }
            var prev = loadTablesBtn.Content; loadTablesBtn.IsEnabled = false; loadTablesBtn.Content = "Loading…"; tablesStatus.Text = "Loading tables…";
            try
            {
                var clusterUri = cluster.Text; var mode = Mode();
                var t = await Task.Run(() => KustoLocation.GetTables(clusterUri, db, mode));
                tables.ItemsSource = t;
                tablesStatus.Text = t.Count > 0 ? $"{t.Count} tables (click one to build a query)." : "No tables found.";
            }
            catch (Exception ex) { tablesStatus.Text = "Failed to load tables: " + ex.Message; }
            finally { loadTablesBtn.Content = prev; loadTablesBtn.IsEnabled = true; }
        };
        tables.SelectionChanged += (_, __) =>
        {
            if (tables.SelectedItem is string t) query.Text = $"['{t}']\n| take 100";
        };

        // Preview the query's first rows before adding it.
        var previewBtn = new Button { Content = "Preview" };
        var previewOut = new TextBox
        {
            IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 12,
            MaxHeight = 180, PlaceholderText = "Preview results appear here",
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
            catch (Exception ex) { previewOut.Text = "Query failed: " + ex.Message; }
            finally { previewBtn.Content = prev; previewBtn.IsEnabled = true; }
        };

        var dbGroup = new StackPanel { Spacing = 4 };
        dbGroup.Children.Add(dbBox);
        dbGroup.Children.Add(loadDbBtn);
        dbGroup.Children.Add(dbList);
        dbGroup.Children.Add(dbStatus);

        var tablesGroup = new StackPanel { Spacing = 4 };
        tablesGroup.Children.Add(new TextBlock { Text = "Tables", FontWeight = global::Microsoft.UI.Text.FontWeights.SemiBold });
        tablesGroup.Children.Add(loadTablesBtn);
        tablesGroup.Children.Add(tables);
        tablesGroup.Children.Add(tablesStatus);

        var previewGroup = new StackPanel { Spacing = 4 };
        previewGroup.Children.Add(previewBtn);
        previewGroup.Children.Add(previewOut);

        var panel = new StackPanel { Spacing = 10, MinWidth = 520 };
        panel.Children.Add(cluster);
        panel.Children.Add(auth);
        panel.Children.Add(dbGroup);
        panel.Children.Add(tablesGroup);
        panel.Children.Add(query);
        panel.Children.Add(previewGroup);

        var dialog = new ContentDialog
        {
            Title = "Add Kusto location",
            Content = new ScrollViewer
            {
                Content = panel, MaxHeight = 620,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var database = (dbBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cluster.Text) || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(query.Text))
            return;

        MiddleLayerService.AddLocation(new KustoLocation(cluster.Text, database, query.Text, Mode()));
        _viewModel.Refresh();
    }
}
