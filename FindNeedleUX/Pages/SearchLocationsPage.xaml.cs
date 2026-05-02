using System;
using System.Collections.ObjectModel;
using FindNeedleUX.Services;
using FindNeedleUX.Utils;
using FindNeedleUX.ViewObjects;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FindNeedleUX.Pages;

public sealed partial class SearchLocationsPage : Page
{
    ObservableCollection<LocationListItem> RecipeList = new();

    public SearchLocationsPage()
    {
        this.InitializeComponent();
        CheckOtherDLLs.AreWeInstalledOk();
        RecipeList = MiddleLayerService.GetLocationListItems();
        VariedImageSizeRepeater.ItemsSource = RecipeList;
    }

    private void Button_Remove(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name })
        {
            MiddleLayerService.RemoveLocationByName(name);
            RefreshList();
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
            MiddleLayerService.AddFolderLocation(folder.Path);
            RefreshList();
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
            MiddleLayerService.AddFolderLocation(file.Path);
            RefreshList();
        }
    }

    private void RefreshList()
    {
        RecipeList = MiddleLayerService.GetLocationListItems();
        VariedImageSizeRepeater.ItemsSource = RecipeList;
    }
}
