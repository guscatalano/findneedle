using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FindNeedleUX.Services;
using FindPluginCore.GlobalConfiguration;
using FindNeedleCoreUtils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using FindNeedlePluginLib;

namespace FindNeedleUX;

public sealed partial class MainWindow : Window
{
    private static readonly Dictionary<string, Type> ResultViewerPages = new()
    {
        { "resultswebpage", typeof(FindNeedleUX.Pages.ResultsWebPage) },
        { "nativereviewer", typeof(FindNeedleUX.Pages.NativeResultsPage) }
    };

    private static readonly string[] RawResultViewers = new[]
    {
        "resultswebpage", "nativereviewer"
    };

    private CancellationTokenSource _quickActionCts;
    private string _lastRunSummary = "—";

    public MainWindow()
    {
        this.InitializeComponent();
        WindowUtil.TrackWindow(this);
        SetWindowIcon("Assets\\appicon.ico");
        contentFrame.Navigated += (s, e) => RefreshStatusStrip();
        MiddleLayerService.StateChanged += () => DispatcherQueue.TryEnqueue(RefreshStatusStrip);
        // Show WelcomePage on startup
        contentFrame.Navigate(typeof(FindNeedleUX.Pages.WelcomePage));
        RefreshStatusStrip();

        // Pre-warm the heavy viewer pages on a low-priority dispatcher tick. Constructing the
        // page without attaching it to the visual tree primes the XAML parser cache, runs
        // every type's static .cctor, and JITs the hot constructors — about half the cost of
        // the first user-initiated switch. The welcome page is already painted, so this happens
        // while the user is reading it. DataGrid first-layout still happens on the real switch
        // (it requires visual-tree attachment), but the second half of the freeze is gone.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, WarmUpViewers);
    }

    private void WarmUpViewers()
    {
        try
        {
            _ = new FindNeedleUX.Pages.NativeResultsPage();
            Logger.Instance.Log("Viewer pre-warm complete");
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"Viewer pre-warm failed: {ex.Message}");
        }
    }

    public void NavigateToQuickLogWithRules()
    {
        contentFrame.Navigate(typeof(FindNeedleUX.Pages.QuickLogWithRulesPage));
    }

    /// <summary>
    /// Programmatically navigate to the native result viewer. Used by the streaming search flow
    /// to open the viewer mid-load when a search runs past the grace window.
    /// </summary>
    public void NavigateToNativeResultsPage()
    {
        DispatcherQueue.TryEnqueue(() =>
            contentFrame.Navigate(typeof(FindNeedleUX.Pages.NativeResultsPage)));
    }

    /// <summary>
    /// Show / hide a non-cancellable spinner used to give immediate feedback when switching to a
    /// page whose first construction is slow (e.g. the native viewer's DataGrid init). The
    /// destination page is expected to call <see cref="HideNavigationSpinner"/> in its
    /// <c>Loaded</c> handler once it's fully painted.
    /// </summary>
    public void ShowNavigationSpinner(string text)
    {
        ShowSpinner(true, text);
    }

    public void HideNavigationSpinner()
    {
        ShowSpinner(false);
    }

    /// <summary>
    /// Show the loading spinner, paint one frame so the user sees it, then perform the
    /// (synchronous, UI-thread-blocking) Frame navigation. The destination page is expected to
    /// call <see cref="HideNavigationSpinner"/> from its Loaded handler — for pages that don't,
    /// we drop a safety-net hide call after the navigation completes.
    ///
    /// While the spinner is visible we also seed its text with the current result-set size so
    /// the user understands the order of magnitude they're waiting for (e.g. "Loading viewer ·
    /// 423,231 rows"). For a running streaming search the count is live; for a completed search
    /// it's the final row count.
    /// </summary>
    private void NavigateWithSpinner(Type pageType)
    {
        ShowSpinner(true, BuildLoadingViewerText());
        // Defer the navigation to the next dispatcher tick so the spinner gets one paint pass
        // before the UI thread is blocked by page construction (DataGrid init in particular).
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            try { contentFrame.Navigate(pageType); }
            finally
            {
                // Safety net: if the destination didn't hide the spinner itself, kill it on the
                // next tick so we never leave it visible permanently.
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                    () => ShowSpinner(false));
            }
        });
    }

    private static string BuildLoadingViewerText()
    {
        // Prefer the running streaming search's storage if there is one (in-flight load), since
        // that count grows as rows arrive. Otherwise fall back to the last completed search's
        // result count. Either way, surface it so the user knows what's coming.
        try
        {
            var streaming = MiddleLayerService.CurrentStreamingSearch;
            if (streaming?.Storage != null)
            {
                var n = streaming.Storage.GetStatistics().filteredRecordCount;
                if (streaming.Source?.IsLoading == true)
                    return $"Loading viewer · streaming {n:N0} rows…";
                return $"Loading viewer · {n:N0} rows";
            }
            var completed = MiddleLayerService.GetFilteredRowCount();
            if (completed > 0) return $"Loading viewer · {completed:N0} rows";
        }
        catch { /* never let UX feedback break the navigation */ }
        return "Loading viewer…";
    }

    private void RefreshStatusStrip()
    {
        var query = MiddleLayerService.GetCurrentQuery();
        var locations = MiddleLayerService.Locations?.Count ?? 0;
        var rules = query?.RulesConfigPaths?.Count ?? 0;
        StatusStrip.Text = $"Locations: {locations} · Rules: {rules} · Last run: {_lastRunSummary}";
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
            case "search_rules":
                Logger.Instance.Log("Navigated: SearchRulesPage");
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.SearchRulesPage));
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
                NavigateWithSpinner(viewerType);
                break;
            case "results_processoroutput":
                Logger.Instance.Log("Navigated: ProcessorOutputPage");
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.ProcessorOutputPage));
                break;
            case "results_viewweb":
                Logger.Instance.Log("Navigated: ResultsWebPage");
                NavigateWithSpinner(typeof(FindNeedleUX.Pages.ResultsWebPage));
                break;
            case "results_viewnative":
                Logger.Instance.Log("Navigated: NativeResultsPage");
                NavigateWithSpinner(typeof(FindNeedleUX.Pages.NativeResultsPage));
                break;
            case "systeminfo":
                Logger.Instance.Log("Navigated: SystemInfoPage");
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.SystemInfoPage));
                break;
            case "settings_resultviewer":
                Logger.Instance.Log("Navigated: ResultsViewerSettingsPage");
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.ResultsViewerSettingsPage));
                break;
            case "diagramtools":
                Logger.Instance.Log("Navigated: DiagramToolsPage");
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.DiagramToolsPage));
                break;
            case "rules_uml":
                Logger.Instance.Log("Navigated: DiagramToolsPage");
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.DiagramToolsPage));
                break;
            case "logs":
                Logger.Instance.Log("Navigated: LogsPage");
                var logsWindow = new FindNeedleUX.Windows.LogsWindow();
                logsWindow.Activate();
                break;
            case "openlogfile":
                Logger.Instance.Log("Opened log file picker");
                QuickFileOpen();
                break;
            case "openlogfolder":
                Logger.Instance.Log("Opened log folder picker");
                QuickFolderOpen();
                break;
            case "openlogwithrules":
                Logger.Instance.Log("Navigated: QuickLogWithRulesPage");
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.QuickLogWithRulesPage));
                break;
            case "inspect_etl":
                await InspectionService.InspectEtlAsync(this, (show, text) => ShowSpinner(show, text));
                break;
            case "inspect_binary":
                await InspectionService.InspectBinaryAsync(this, (show, text) => ShowSpinner(show, text));
                break;
            default:
                Logger.Instance.Log($"Navigation error: unknown menu item {selectedFlyoutItem.Name}");
                throw new Exception("bad code");
        }
    }

    private async Task RunSearchWithProgress(bool surfaceScan = false)
    {
        _quickActionCts = new CancellationTokenSource();
        ShowSpinner(true, "Running search...", showCancel:true);
        // Register for progress updates
        var sink = MiddleLayerService.GetProgressEventSink();
        void OnTextProgress(string text)
        {
            DispatcherQueue.TryEnqueue(() => SpinnerText.Text = text);
        }
        void OnNumericProgress(int percent)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!string.IsNullOrWhiteSpace(SpinnerText.Text))
                    SpinnerText.Text = $"{SpinnerText.Text.Split('(')[0].Trim()} ({percent}%)";
                else
                    SpinnerText.Text = $"Progress: {percent}%";
            });
        }
        sink.RegisterForTextProgress(OnTextProgress);
        sink.RegisterForNumericProgress(OnNumericProgress);
        try
        {
            await Task.Run(() => MiddleLayerService.RunSearch(surfaceScan, _quickActionCts.Token).Wait(), _quickActionCts.Token);
            var stats = MiddleLayerService.GetStats();
            var count = MiddleLayerService.GetFilteredRowCount();
            _lastRunSummary = $"{count} results";
        }
        catch (OperationCanceledException)
        {
            DispatcherQueue.TryEnqueue(() => SpinnerText.Text = "Search cancelled.");
            _lastRunSummary = "cancelled";
        }
        finally
        {
            ShowSpinner(false);
            _quickActionCts?.Dispose();
            _quickActionCts = null;
            DispatcherQueue.TryEnqueue(RefreshStatusStrip);
        }
    }

    public async void QuickFileOpen()
    {
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
            ShowSpinner(true, "Opening file...", showCancel:true);
            await RunSearchWithProgress();
            ShowSpinner(false);
            var viewerKey = GlobalSettings.DefaultResultViewer?.ToLower() ?? "resultswebpage";
            if (!ResultViewerPages.TryGetValue(viewerKey, out var viewerType))
            {
                viewerType = typeof(FindNeedleUX.Pages.ResultsWebPage);
            }
            contentFrame.Navigate(viewerType);
        }
    }

    public async void QuickFolderOpen()
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var picker = new global::Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            var folderPath = folder.Path;
            MiddleLayerService.NewWorkspace();
            MiddleLayerService.AddFolderLocation(folderPath);
            ShowSpinner(true, "Opening folder...", showCancel:true);
            await RunSearchWithProgress();
            ShowSpinner(false);
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
            ShowSpinner(true, "Loading workspace...");
            MiddleLayerService.OpenWorkspace(file.Path);
            ShowSpinner(false);
        }
    }

    private async void SaveCommand()
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var picker = new FileSavePicker();
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        picker.SuggestedFileName = "SearchQuery";
        picker.FileTypeChoices.Add("ComplexJson", new List<string>() { ".json" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
            ShowSpinner(true, "Saving workspace...");
            MiddleLayerService.SaveWorkspace(file.Path);
            ShowSpinner(false);
        }
    }

    private void ShowSpinner(bool show, string text = null, bool showCancel = false)
    {
        SpinnerPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        EtlSpinner.IsActive = show;
        if (text != null)
            SpinnerText.Text = text;
        CancelQuickActionButton.Visibility = show && showCancel ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CancelQuickActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_quickActionCts != null && !_quickActionCts.IsCancellationRequested)
        {
            _quickActionCts.Cancel();
            SpinnerText.Text = "Cancelling...";
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
