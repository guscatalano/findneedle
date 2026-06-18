using FindNeedleUX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FindNeedleUX.Pages;

/// <summary>
/// Search statistics &amp; timing for the most recent run. Renders the structured
/// <see cref="FindPluginCore.Diagnostics.SearchRunReport"/> (rows, storage backend, cache hit/miss,
/// consolidation, slowest phases, and "why" hints) — the same data the old "Performance Report"
/// dialog showed, now the single home for both. The legacy per-component SearchStatistics is not
/// used here: it isn't populated for the current NuSearchQuery pipeline.
/// </summary>
public sealed partial class SearchStatisticsPage : Page
{
    private string _text = "";

    public SearchStatisticsPage()
    {
        this.InitializeComponent();
        Loaded += (_, _) => Render();
    }

    private void Render()
    {
        var report = MiddleLayerService.GetLastPerfReport();
        _text = report?.ToText() ?? "No search has run yet — run a search, then check back here.";
        ReportText.Text = _text;
        CopyButton.IsEnabled = report != null;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Render();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var pkg = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
            pkg.SetText(_text);
            global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
        }
        catch { /* clipboard contention — ignore */ }
    }
}
