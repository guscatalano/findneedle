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
using FindPluginCore;
using FindPluginCore.GlobalConfiguration;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FindNeedleUX.Pages;
/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
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
        LogLines.Add(line);
        try
        {
            LogListView.ScrollIntoView(line);
        }
        catch { }
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
}
