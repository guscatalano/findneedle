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
        var path = FindNeedleUX.Services.Win32FileDialog.OpenFile(hWnd, new (string, string)[] { ("PlantUML JAR", "*.jar") });
        if (path != null)
        {
            SystemInfoMiddleware.SetPlantUMLPath(path);
            PlantUmlPathTextBlock.Text = path;
            this.sysout.Text = SystemInfoMiddleware.GetPanelText();
        }
    }
}
