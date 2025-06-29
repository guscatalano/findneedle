using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FindNeedleUX.Services;
using FindPluginCore;
using FindPluginCore.GlobalConfiguration; // Add this for settings
using findneedle.ETWPlugin;
using findneedle.WDK;
using FindNeedleCoreUtils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Text;
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
    private static readonly Dictionary<string, Type> ResultViewerPages = new()
    {
        { "resultswebpage", typeof(FindNeedleUX.Pages.ResultsWebPage) },
        { "resultsvcommunitypage", typeof(FindNeedleUX.Pages.ResultsVCommunityPage) },
        { "searchresultpage", typeof(FindNeedleUX.Pages.SearchResultPage) }
    };

    private static readonly string[] RawResultViewers = new[]
    {
        "resultswebpage", "resultsvcommunitypage", "searchresultpage"
    };

    public MainWindow()
    {
        this.InitializeComponent();
        WindowUtil.TrackWindow(this);
        SetWindowIcon("Assets\\appicon.ico");
        // Show WelcomePage on startup
        contentFrame.Navigate(typeof(FindNeedleUX.Pages.WelcomePage));
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

    private async void MenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        var selectedFlyoutItem = sender as MenuFlyoutItem;
        switch (selectedFlyoutItem.Name.ToLower())
        {
            case "newworkspace":
                MiddleLayerService.NewWorkspace();
                Logger.Instance.Log("Navigated: NewWorkspace (workspace reset)");
                break;
            case "saveworkspace":
                SaveCommand();
                Logger.Instance.Log("Navigated: SaveWorkspace");
                break;
            case "openworkspace":
                LoadCommand();
                Logger.Instance.Log("Navigated: OpenWorkspace");
                break;
            case "search_location":
                Logger.Instance.Log("Navigated: SearchLocationsPage");
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.SearchLocationsPage));
                break;
            case "search_filters":
                Logger.Instance.Log("Navigated: SearchFiltersPage");
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.SearchFiltersPage));
                break;
            case "search_processors":
                Logger.Instance.Log("Navigated: SearchProcessorsPage");
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.SearchProcessorsPage));
                break;
            case "search_plugins":
                Logger.Instance.Log("Navigated: PluginsPage");
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.PluginsPage));
                break;
            case "results_get":
                Logger.Instance.Log("Navigated: RunSearchPage");
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.RunSearchPage));
                break;
            case "results_statistics":
                Logger.Instance.Log("Navigated: SearchStatisticsPage");
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.SearchStatisticsPage));
                break;
            case "results_viewraw":
                Logger.Instance.Log("Navigated: ViewRawResults");
                // Load preferred view from GlobalSettings
                var viewerKey = GlobalSettings.DefaultResultViewer?.ToLower() ?? "resultswebpage";
                if (!((IList<string>)RawResultViewers).Contains(viewerKey))
                    viewerKey = "resultswebpage";
                if (!ResultViewerPages.TryGetValue(viewerKey, out var viewerType))
                    viewerType = typeof(FindNeedleUX.Pages.ResultsWebPage);
                contentFrame.Navigate(viewerType);
                break;
            case "results_processoroutput":
                Logger.Instance.Log("Navigated: ProcessorOutputPage");
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.ProcessorOutputPage));
                break;
            case "results_viewnative":
            case "results_viewweb":
            case "results_viewcommunity":
                // Deprecated: handled by results_viewraw
                break;
            case "systeminfo":
                Logger.Instance.Log("Navigated: SystemInfoPage");
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.SystemInfoPage));
                break;
            case "logs":
                Logger.Instance.Log("Navigated: LogsPage");
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.LogsPage));
                break;
            case "openlogfile":
                Logger.Instance.Log("Opened log file picker");
                QuickFileOpen();
                break;
            case "openlogfolder":
                Logger.Instance.Log("Opened log folder picker");
                QuickFolderOpen();
                break;
            case "inspect_etl":
                await InspectEtlFile();
                break;
            default:
                Logger.Instance.Log($"Navigation error: unknown menu item {selectedFlyoutItem.Name}");
                throw new Exception("bad code");
        }
    }

    private async Task RunSearchWithProgress(bool surfaceScan = false)
    {
        ShowSpinner(true);
        await Task.Run(() => MiddleLayerService.RunSearch(surfaceScan).Wait());
        ShowSpinner(false);
    }

    private async void QuickFileOpen()
    {
        // Retrieve the window handle (HWND) of the current WinUI 3 window.
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);


        var picker = new FileOpenPicker()
        {
            ViewMode = PickerViewMode.List,
            FileTypeFilter = { ".txt", ".etl", ".log", ".zip", ".evtx" },
        };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            MiddleLayerService.NewWorkspace();
            MiddleLayerService.AddFolderLocation(file.Path);
            await RunSearchWithProgress();
            // Use the default result viewer setting
            var viewerKey = GlobalSettings.DefaultResultViewer?.ToLower() ?? "resultswebpage";
            if (!ResultViewerPages.TryGetValue(viewerKey, out var viewerType))
            {
                viewerType = typeof(FindNeedleUX.Pages.ResultsWebPage);
            }
            contentFrame.Navigate(viewerType);
        }
    }

    private async void QuickFolderOpen()
    {
        // Retrieve the window handle (HWND) of the current WinUI 3 window.
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        var picker = new global::Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*"); // Required, even for folders
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            var folderPath = folder.Path;
            MiddleLayerService.NewWorkspace();
            MiddleLayerService.AddFolderLocation(folderPath);
            await RunSearchWithProgress();
            // Use the default result viewer setting
            var viewerKey = GlobalSettings.DefaultResultViewer?.ToLower() ?? "resultswebpage";
            if (!ResultViewerPages.TryGetValue(viewerKey, out var viewerType))
            {
                viewerType = typeof(FindNeedleUX.Pages.ResultsWebPage);
            }
            contentFrame.Navigate(viewerType);
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
        if (file != null)
        {
            MiddleLayerService.SaveWorkspace(file.Path);
        }
    }

    private void ShowSpinner(bool show)
    {
        SpinnerPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        EtlSpinner.IsActive = show;
    }

    private async Task InspectEtlFile()
    {
        ShowSpinner(true);
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var picker = new FileOpenPicker()
        {
            ViewMode = PickerViewMode.List,
            FileTypeFilter = { ".etl" },
        };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
        var file = await picker.PickSingleFileAsync();
        if (file == null)
        {
            ShowSpinner(false);
            return;
        }
        string etlPath = file.Path;
        List<string> providers = null;
        Dictionary<string, string> sysInfo = null;
        ETLSummary reportSummary = null;
        string error = null;
        string tempPath = null;
        await Task.Run(() =>
        {
            try
            {
                providers = EtlInfoExtractor.GetProviders(etlPath);
                sysInfo = EtlInfoExtractor.GetSystemInfo(etlPath);
                tempPath = TempStorage.GetNewTempPath("tracerpt");
                reportSummary = TracerptRunner.RunAndParseReport(etlPath, tempPath);
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
        });
        ShowSpinner(false);
        var dialog = new ContentDialog
        {
            Title = error == null ? "ETL Inspection Results" : "ETL Inspection Error",
            CloseButtonText = "OK",
            XamlRoot = this.Content.XamlRoot
        };
        if (error != null)
        {
            dialog.Content = $"Error inspecting ETL: {error}";
        }
        else
        {
            var content = new StackPanel();
            // Providers from TraceEvent
            content.Children.Add(new TextBlock { Text = $"Providers (TraceEvent) ({providers.Count}):", FontWeight = FontWeights.Bold });
            foreach (var p in providers.Take(20))
                content.Children.Add(new TextBlock { Text = p });
            if (providers.Count > 20)
                content.Children.Add(new TextBlock { Text = $"...and {providers.Count - 20} more" });
            // Providers from tracerpt -report
            if (reportSummary != null && reportSummary.Providers != null && reportSummary.Providers.Count > 0)
            {
                content.Children.Add(new TextBlock { Text = $"\nProviders (tracerpt -report) ({reportSummary.Providers.Count}):", FontWeight = FontWeights.Bold, Margin = new Microsoft.UI.Xaml.Thickness(0,8,0,0) });
                foreach (var p in reportSummary.Providers.Take(20))
                    content.Children.Add(new TextBlock { Text = p });
                if (reportSummary.Providers.Count > 20)
                    content.Children.Add(new TextBlock { Text = $"...and {reportSummary.Providers.Count - 20} more" });
            }
            // Windows build info from tracerpt -report
            if (!string.IsNullOrWhiteSpace(reportSummary?.WindowsBuildInfo))
            {
                content.Children.Add(new TextBlock { Text = $"\nWindows Build: {reportSummary.WindowsBuildInfo}", FontWeight = FontWeights.Bold, Margin = new Microsoft.UI.Xaml.Thickness(0,8,0,0) });
            }
            // System info from TraceEvent
            content.Children.Add(new TextBlock { Text = "\nSystem Info:", FontWeight = FontWeights.Bold, Margin = new Microsoft.UI.Xaml.Thickness(0,8,0,0) });
            if (sysInfo == null || sysInfo.Count == 0)
                content.Children.Add(new TextBlock { Text = "(No system info found)" });
            else
                foreach (var kv in sysInfo.Take(10))
                    content.Children.Add(new TextBlock { Text = $"{kv.Key}: {kv.Value}" });
            if (sysInfo != null && sysInfo.Count > 10)
                content.Children.Add(new TextBlock { Text = $"...and {sysInfo.Count - 10} more" });
            dialog.Content = content;
        }
        await dialog.ShowAsync();
        if (tempPath != null)
        {
            TempStorage.DeleteSomeTempPath(tempPath);
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
