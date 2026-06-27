using System.Diagnostics;
using FindNeedleUX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Text;

namespace FindNeedleUX.Pages;

/// <summary>
/// About page — app version / build metadata and the Store / GitHub links. Split out of the old
/// System Check page (which is now a pure environment health view).
/// </summary>
public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        this.InitializeComponent();
        Loaded += (_, _) => Render();
    }

    private void Render()
    {
        VersionPanel.Children.Clear();
        var (version, source, buildTime, storeVersion) = SystemInfoMiddleware.GetAboutInfo();
        Add("Version", version);
        Add("Version source", source);
        Add("Build time", buildTime);
        Add("MS-Store version", storeVersion);
    }

    private void Add(string label, string value)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold });
        var v = new TextBlock { Text = value, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(v, 1);
        grid.Children.Add(v);
        VersionPanel.Children.Add(grid);
    }

    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch { /* no handler — ignore */ }
    }

    private void ExportPerfReport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = FindPluginCore.Diagnostics.PerformanceReport.Save();
            PerfReportStatus.Text = $"Saved to {path} — opening…";
            PerfReportStatus.Visibility = Visibility.Visible;
            Open(path); // hand the .md to the default handler
        }
        catch (System.Exception ex)
        {
            PerfReportStatus.Text = $"Could not write the report: {ex.Message}";
            PerfReportStatus.Visibility = Visibility.Visible;
        }
    }

    private void StoreLink_Click(object sender, RoutedEventArgs e) => Open(SystemInfoMiddleware.StoreUrl);
    private void MsStoreLink_Click(object sender, RoutedEventArgs e) => Open(SystemInfoMiddleware.MsStoreUrl);
    private void GithubReleasesLink_Click(object sender, RoutedEventArgs e) => Open(SystemInfoMiddleware.GithubReleasesUrl);
    private void GithubLink_Click(object sender, RoutedEventArgs e) => Open(SystemInfoMiddleware.GithubUrl);
}
