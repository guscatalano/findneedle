using System;
using FindNeedleUX.Utils;
using FindNeedleUX.ViewModels;
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
}
