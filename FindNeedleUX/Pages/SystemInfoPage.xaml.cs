using FindNeedleUX.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using FindPluginCore.GlobalConfiguration;
using System.Diagnostics;
using Windows.Storage.Pickers;

namespace FindNeedleUX.Pages;

/// <summary>
/// System information and configuration page.
/// </summary>
public sealed partial class SystemInfoPage : Page
{
    public SystemInfoPage()
    {
        this.InitializeComponent();
        this.sysout.Text = SystemInfoMiddleware.GetPanelText();
        PlantUmlPathTextBlock.Text = SystemInfoMiddleware.GetPlantUMLPath();
        SetComboBoxToCurrent();
    }

    private void SetComboBoxToCurrent()
    {
        var current = GlobalSettings.DefaultResultViewer?.ToLower() ?? "resultswebpage";
        foreach (ComboBoxItem item in ResultViewerComboBox.Items)
        {
            if ((item.Tag as string)?.ToLower() == current)
            {
                ResultViewerComboBox.SelectedItem = item;
                break;
            }
        }
    }

    private void ResultViewerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultViewerComboBox.SelectedItem is ComboBoxItem selected)
        {
            var tag = selected.Tag as string;
            if (!string.IsNullOrEmpty(tag))
            {
                GlobalSettings.DefaultResultViewer = tag;
                this.sysout.Text = SystemInfoMiddleware.GetPanelText();
            }
        }
    }

    private void StoreLink_Click(object sender, RoutedEventArgs e)
    {
        var url = SystemInfoMiddleware.StoreUrl;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch { }
    }

    private void MsStoreLink_Click(object sender, RoutedEventArgs e)
    {
        var url = SystemInfoMiddleware.MsStoreUrl;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch { }
    }

    private void GithubReleasesLink_Click(object sender, RoutedEventArgs e)
    {
        var url = SystemInfoMiddleware.GithubReleasesUrl;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch { }
    }

    private void GithubLink_Click(object sender, RoutedEventArgs e)
    {
        var url = SystemInfoMiddleware.GithubUrl;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch { }
    }

    private void ChangePlantUmlPath_Click(object sender, RoutedEventArgs e)
    {
        var window = WindowUtil.GetWindowForElement(this);
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".jar");
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
        var fileOp = picker.PickSingleFileAsync();
        fileOp.Completed = (op, status) =>
        {
            var file = op.GetResults();
            DispatcherQueue.TryEnqueue(() =>
            {
                if (file != null)
                {
                    SystemInfoMiddleware.SetPlantUMLPath(file.Path);
                    PlantUmlPathTextBlock.Text = file.Path;
                    this.sysout.Text = SystemInfoMiddleware.GetPanelText();
                }
            });
        };
    }
}
