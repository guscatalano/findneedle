using System;
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
        var database = new TextBox { Header = "Database", PlaceholderText = "MyDatabase" };
        var query = new TextBox
        {
            Header = "KQL query",
            PlaceholderText = "MyTable | where Timestamp > ago(1h) | take 1000",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 120,
        };
        var auth = new ComboBox { Header = "Sign-in", SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        auth.Items.Add("Interactive browser sign-in");
        auth.Items.Add("Azure CLI (az login)");
        auth.Items.Add("Device code");

        var panel = new StackPanel { Spacing = 10, MinWidth = 460 };
        panel.Children.Add(cluster);
        panel.Children.Add(database);
        panel.Children.Add(query);
        panel.Children.Add(auth);

        var dialog = new ContentDialog
        {
            Title = "Add Kusto location",
            Content = new ScrollViewer { Content = panel, MaxHeight = 520 },
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (string.IsNullOrWhiteSpace(cluster.Text) || string.IsNullOrWhiteSpace(database.Text)
            || string.IsNullOrWhiteSpace(query.Text))
            return;

        var mode = auth.SelectedIndex switch
        {
            1 => KustoAuthMode.AzureCli,
            2 => KustoAuthMode.DeviceCode,
            _ => KustoAuthMode.Interactive,
        };
        MiddleLayerService.AddLocation(new KustoLocation(cluster.Text, database.Text, query.Text, mode));
        _viewModel.Refresh();
    }
}
