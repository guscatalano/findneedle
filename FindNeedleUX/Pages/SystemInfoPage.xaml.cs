using FindNeedleUX.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using FindPluginCore.GlobalConfiguration;
using System.Diagnostics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FindNeedleUX.Pages;
/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class SystemInfoPage : Page
{
    public SystemInfoPage()
    {
        this.InitializeComponent();
        this.sysout.Text = SystemInfoMiddleware.GetPanelText();
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
                this.sysout.Text = SystemInfoMiddleware.GetPanelText(); // Refresh info
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
}
