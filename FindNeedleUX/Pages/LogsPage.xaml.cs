using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.ApplicationModel.DataTransfer;
using FindPluginCore;
using FindPluginCore.GlobalConfiguration;
using FindNeedleUX; // For WindowUtil
using FindNeedlePluginLib; // For Logger

namespace FindNeedleUX.Pages;

/// <summary>
/// Log viewer page with copy functionality.
/// </summary>
public sealed partial class LogsPage : Page
{
    public ObservableCollection<string> LogLines { get; } = new();

    public LogsPage()
    {
        InitializeComponent();
        LogListView.ItemsSource = LogLines;
        // Load cached log lines
        foreach (var line in Logger.Instance.LogCache)
        {
            LogLines.Add(line);
        }
        Logger.Instance.LogCallback = AddLogLine;
        DebugToggleSwitch.IsOn = GlobalSettings.Debug;
        UpdateDebugStatusText();
    }

    public void AddLogLine(string line)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            LogLines.Add(line);
            try
            {
                LogListView.ScrollIntoView(line);
            }
            catch { }
        }
        else
        {
            DispatcherQueue.TryEnqueue(() => AddLogLine(line));
        }
    }

    private void DebugToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        GlobalSettings.ToggleDebug();
        UpdateDebugStatusText();
        Logger.Instance.Log($"Debug logging toggled: {GlobalSettings.Debug}");
    }

    private void UpdateDebugStatusText()
    {
        DebugStatusText.Text = $"Debug is {(GlobalSettings.Debug ? "ON" : "OFF")}";
    }

    private void CopySelected_Click(object sender, RoutedEventArgs e)
    {
        if (LogListView.SelectedItems.Count == 0)
        {
            Logger.Instance.Log("No log lines selected to copy");
            return;
        }

        var selectedLines = LogListView.SelectedItems.Cast<string>().ToList();
        var text = string.Join(Environment.NewLine, selectedLines);
        
        CopyToClipboard(text);
        Logger.Instance.Log($"Copied {selectedLines.Count} log lines to clipboard");
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        if (LogLines.Count == 0)
        {
            Logger.Instance.Log("No log lines to copy");
            return;
        }

        var text = string.Join(Environment.NewLine, LogLines);
        
        CopyToClipboard(text);
        Logger.Instance.Log($"Copied all {LogLines.Count} log lines to clipboard");
    }

    private static void CopyToClipboard(string text)
    {
        try
        {
            var dataPackage = new DataPackage();
            dataPackage.RequestedOperation = DataPackageOperation.Copy;
            dataPackage.SetText(text);
            Clipboard.SetContent(dataPackage);
            Clipboard.Flush(); // Ensures data persists after app closes
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"Failed to copy to clipboard: {ex.Message}");
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        LogLines.Clear();
        Logger.Instance.Log("Log view cleared");
    }
}
