using System;
using System.Collections.Generic;
using FindNeedleUX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FindNeedleUX;

/// <summary>
/// An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class MainWindow : Window
{


    public MainWindow()
    {
        this.InitializeComponent();
        WindowUtil.TrackWindow(this);
    }
    /*
    private void NavigationView_SelectionChanged(Microsoft.UI.Xaml.Controls.NavigationView sender, Microsoft.UI.Xaml.Controls.NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            // contentFrame.Navigate(typeof(SampleSettingsPage));
        }
        else
        {
            var selectedItem = (Microsoft.UI.Xaml.Controls.NavigationViewItem)args.SelectedItem;
            if (selectedItem != null)
            {
                var selectedItemTag = ((string)selectedItem.Tag);
                sender.Header = selectedItemTag;
                var pageName = "FindNeedleUX.Pages." + selectedItemTag;
                Type pageType = Type.GetType(pageName);
                contentFrame.Navigate(pageType);
            }
        }
    }*/

    private void MenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        var selectedFlyoutItem = sender as MenuFlyoutItem;
        switch (selectedFlyoutItem.Name.ToLower())
        {
            case "newworkspace":
                MiddleLayerService.NewWorkspace();
                break;
            case "saveworkspace":
                SaveCommand();
                break;
            case "openworkspace":
                LoadCommand();
                break;
            case "search_location":
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.SearchLocationsPage));
                break;
            case "search_filters":
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.SearchFiltersPage));
                break;
            case "search_processors":
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.SearchProcessorsPage));
                break;
            case "search_plugins":
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.PluginsPage));
                break;
            case "results_get":
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.RunSearchPage));
                break;
            case "results_statistics":
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.SearchStatisticsPage));
                break;
            case "results_viewnative":
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.SearchResultPage)); //not lightresults probably?
                break;
            case "results_viewweb":
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.ResultsWebPage));
                break;
            case "results_viewcommunity":
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.ResultsVCommunityPage));
                break;
            case "systeminfo":
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.SystemInfoPage));
                break;
            default:
                throw new Exception("bad code");
        }


    }

    private async void LoadCommand()
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);



        var picker = new FileOpenPicker()
        {
            ViewMode = PickerViewMode.List,
            FileTypeFilter = { ".json" },
        };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

        var file = await picker.PickSingleFileAsync();
        // var files = await picker.PickMultipleFilesAsync();

        // Initialize the file picker with the window handle (HWND).
        // WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hWnd);

        // Set options for your file picker
        // openPicker.ViewMode = PickerViewMode.Thumbnail;
        //  openPicker.FileTypeFilter.Add("*");

        // Open the picker for the user to pick a file
        //  var file = await openPicker.PickSingleFileAsync();
        if (file != null)
        {
            MiddleLayerService.OpenWorkspace(file.Path);
        }
    }

    private async void SaveCommand()
    {


        // Retrieve the window handle (HWND) of the current WinUI 3 window.
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);



        var picker = new FileSavePicker();
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        picker.SuggestedFileName = "SearchQuery";
        picker.FileTypeChoices.Add("ComplexJson", new List<string>() { ".json" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

        var file = await picker.PickSaveFileAsync();
        // var files = await picker.PickMultipleFilesAsync();

        // Initialize the file picker with the window handle (HWND).
        // WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hWnd);

        // Set options for your file picker
        // openPicker.ViewMode = PickerViewMode.Thumbnail;
        //  openPicker.FileTypeFilter.Add("*");

        // Open the picker for the user to pick a file
        //  var file = await openPicker.PickSingleFileAsync();
        if (file != null)
        {
            MiddleLayerService.SaveWorkspace(file.Path);
        }
    }
}
