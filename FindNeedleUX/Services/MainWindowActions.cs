using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.CompilerServices;

namespace FindNeedleUX.Services;
public class MainWindowActions
{
   // private static NavigationView navigationView;
    public static void DisableNavBar()
    {
       // navigationView.IsEnabled = false;
    }

    public static void EnableNavBar()
    {
       // navigationView.IsEnabled = true;
    }

    public static void TrackNavBar(NavigationView bar)
    {
            //navigationView = bar;
    }

    /// <summary>
    /// Switch the main window's content to the native result viewer. Marshals to the UI thread
    /// internally — safe to call from a background task or `await Task.Delay` continuation.
    /// </summary>
    public static void NavigateToNativeResultsPage()
    {
        if (WindowUtil.GetMainWindow() is FindNeedleUX.MainWindow mw)
            mw.NavigateToNativeResultsPage();
    }

    /// <summary>
    /// Show a brief modal spinner labelled "Loading viewer…" so the user gets immediate feedback
    /// when switching to a heavy page (e.g. the native viewer's first construction). The
    /// destination page's <c>Loaded</c> handler calls <see cref="HideNavigationSpinner"/> to
    /// dismiss it.
    /// </summary>
    public static void ShowNavigationSpinner(string text = "Loading viewer…")
    {
        if (WindowUtil.GetMainWindow() is FindNeedleUX.MainWindow mw)
            mw.ShowNavigationSpinner(text);
    }

    public static void HideNavigationSpinner()
    {
        if (WindowUtil.GetMainWindow() is FindNeedleUX.MainWindow mw)
            mw.HideNavigationSpinner();
    }

    /// <summary>Switch to the Sources (Search Locations) page — used by the "Add source" affordance
    /// when a run is attempted with no sources loaded.</summary>
    public static void NavigateToSearchLocations()
    {
        if (WindowUtil.GetMainWindow() is FindNeedleUX.MainWindow mw)
            mw.NavigateToSearchLocations();
    }

    /// <summary>Open a single log file via the file picker (the "Open log file" empty-state action).</summary>
    public static void OpenLogFile()
    {
        if (WindowUtil.GetMainWindow() is FindNeedleUX.MainWindow mw)
            mw.QuickFileOpen();
    }

    /// <summary>Switch to the result-viewer settings page (used by the "fix WPP symbols" banner action).</summary>
    public static void NavigateToResultsViewerSettings()
    {
        if (WindowUtil.GetMainWindow() is FindNeedleUX.MainWindow mw)
            mw.NavigateToResultsViewerSettings();
    }

    /// <summary>Force a full decode + reopen (the "Decode anyway" banner action).</summary>
    public static void RerunWithFullDecode()
    {
        if (WindowUtil.GetMainWindow() is FindNeedleUX.MainWindow mw)
            mw.RerunWithFullDecode();
    }

    /// <summary>Re-run the current search (fresh scan) and reopen the viewer — used after applying a
    /// quick/session rule so its effect shows.</summary>
    public static void RerunSearch()
    {
        if (WindowUtil.GetMainWindow() is FindNeedleUX.MainWindow mw)
            mw.RerunSearch();
    }
}
