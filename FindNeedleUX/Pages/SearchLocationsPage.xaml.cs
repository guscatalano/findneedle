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
        var auth = new ComboBox { Header = "Sign-in", SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        auth.Items.Add("Interactive browser sign-in");
        auth.Items.Add("Azure CLI (az login)");
        auth.Items.Add("Device code");

        KustoAuthMode Mode() => auth.SelectedIndex switch
        {
            1 => KustoAuthMode.AzureCli,
            2 => KustoAuthMode.DeviceCode,
            _ => KustoAuthMode.Interactive,
        };

        // Database is a picker populated from the cluster (most people won't know the name). Editable
        // so it still works if listing fails (or you'd rather type it).
        var dbCombo = new ComboBox
        {
            Header = "Database",
            IsEditable = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "Load databases, or type a name",
        };
        var loadBtn = new Button { Content = "Load databases" };
        var status = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        };
        loadBtn.Click += async (_, __) =>
        {
            if (string.IsNullOrWhiteSpace(cluster.Text)) { status.Text = "Enter a cluster URI first."; return; }
            var prev = loadBtn.Content;
            loadBtn.IsEnabled = false; loadBtn.Content = "Loading…"; status.Text = "Connecting… (you may be prompted to sign in)";
            try
            {
                var clusterUri = cluster.Text;
                var mode = Mode();
                var dbs = await Task.Run(() => KustoLocation.GetDatabases(clusterUri, mode));
                var keep = dbCombo.Text;
                dbCombo.Items.Clear();
                foreach (var d in dbs) dbCombo.Items.Add(d);
                status.Text = dbs.Count > 0 ? $"{dbs.Count} databases loaded." : "No databases found.";
                if (dbs.Count > 0) dbCombo.SelectedIndex = 0;
                if (!string.IsNullOrWhiteSpace(keep)) dbCombo.Text = keep;
            }
            catch (Exception ex) { status.Text = "Failed to load databases: " + ex.Message; }
            finally { loadBtn.Content = prev; loadBtn.IsEnabled = true; }
        };

        var query = new TextBox
        {
            Header = "KQL query",
            PlaceholderText = "MyTable | where Timestamp > ago(1h) | take 1000",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 120,
        };

        var dbGroup = new StackPanel { Spacing = 4 };
        dbGroup.Children.Add(dbCombo);
        dbGroup.Children.Add(loadBtn);
        dbGroup.Children.Add(status);

        var panel = new StackPanel { Spacing = 10, MinWidth = 460 };
        panel.Children.Add(cluster);
        panel.Children.Add(auth);
        panel.Children.Add(dbGroup);
        panel.Children.Add(query);

        var dialog = new ContentDialog
        {
            Title = "Add Kusto location",
            Content = new ScrollViewer { Content = panel, MaxHeight = 560 },
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var database = (dbCombo.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cluster.Text) || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(query.Text))
            return;

        MiddleLayerService.AddLocation(new KustoLocation(cluster.Text, database, query.Text, Mode()));
        _viewModel.Refresh();
    }
}
