using System;
using System.Collections.Generic;
using FindNeedleUX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

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
        SetWindowIcon("Assets\\appicon.ico");
    }

    private void SetWindowIcon(string iconPath)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var hIcon = PInvoke.LoadImage(IntPtr.Zero, iconPath, GDI_IMAGE_TYPE.IMAGE_ICON, 0, 0, IMAGE_FLAGS.LR_LOADFROMFILE);
        if (hIcon != IntPtr.Zero)
        {
            PInvoke.SendMessage(new HWND(hwnd), PInvoke.WM_SETICON, (IntPtr)1, hIcon);
        }
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
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.SearchResultPage)); 
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
            case "openlogfile":
                QuickFileOpen();
                
                break;
            case "openlogfolder":
                break;
            default:
                throw new Exception("bad code");
        }


    }

    private async void QuickFileOpen()
    {


        // Retrieve the window handle (HWND) of the current WinUI 3 window.
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        var currentSelection = "None";

        var picker = new FileOpenPicker()
        {
            ViewMode = PickerViewMode.List,
            FileTypeFilter = { ".txt", ".etl", ".log", ".zip", ".evtx" },
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

            currentSelection = file.Path;
            //MiddleLayerService.AddFolderLocation(MiddleLayerService.Locations, currentSelection);
        }
        else
        {
           

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
// Add the following public static class to expose the required PInvoke methods and constants.

public static class PInvoke
{
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    public static extern IntPtr LoadImage(IntPtr hInst, string lpszName, GDI_IMAGE_TYPE uType, int cxDesired, int cyDesired, IMAGE_FLAGS fuLoad);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessage(HWND hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    public const uint WM_SETICON = 0x0080;
}
// Update the HWND struct to include a constructor that takes a single argument.
public struct HWND : IEquatable<HWND>
{
    public nint Value;

    // Add this constructor to fix the CS1729 error.
    public HWND(nint value)
    {
        Value = value;
    }

    public bool Equals(HWND other) => Value == other.Value;

    public override bool Equals(object obj) => obj is HWND other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value.ToString();
}
// To fix the CS0122 errors, the issue is that the `GDI_IMAGE_TYPE` and `IMAGE_FLAGS` enums are inaccessible due to their protection level.  
// These enums are likely defined as `internal` in the `PInvoke` class.  
// To resolve this, you can either make these enums `public` in their definition or create new public enums in your code.  
// Below is the fix by defining public enums in the same file:

public enum GDI_IMAGE_TYPE
{
    IMAGE_BITMAP = 0,
    IMAGE_CURSOR = 2,
    IMAGE_ICON = 1
}

public enum IMAGE_FLAGS
{
    LR_CREATEDIBSECTION = 8192,
    LR_DEFAULTCOLOR = 0,
    LR_DEFAULTSIZE = 64,
    LR_LOADFROMFILE = 16,
    LR_LOADMAP3DCOLORS = 4096,
    LR_LOADTRANSPARENT = 32,
    LR_MONOCHROME = 1,
    LR_SHARED = 32768,
    LR_VGACOLOR = 128,
    LR_COPYDELETEORG = 8,
    LR_COPYFROMRESOURCE = 16384,
    LR_COPYRETURNORG = 4
}
