using System;
using System.Collections;
using System.Linq;
using FindNeedlePluginLib;
using FindNeedleUX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FindNeedleUX.Pages;

/// <summary>
/// Search statistics &amp; timing for the most recent run. Two sources, both populated by the current
/// <c>NuSearchQuery</c> pipeline:
///   • <see cref="SearchStatistics"/> (records loaded vs. matched, memory snapshots, per-component /
///     provider / file breakdown) — surfaced via <c>MiddleLayerService.GetStats()</c>.
///   • <see cref="FindPluginCore.Diagnostics.SearchRunReport"/> (phase wall-clock, storage backend,
///     cache hit/miss) — the "why so slow" timing report.
/// </summary>
public sealed partial class SearchStatisticsPage : Page
{
    private string _copyText = "";

    public SearchStatisticsPage()
    {
        this.InitializeComponent();
        Loaded += (_, _) => Render();
    }

    private void Render()
    {
        var stats = MiddleLayerService.GetStats();
        SummaryText.Text = stats != null
            ? stats.GetSummaryReport().TrimEnd()
            : "No search has run yet — run a search, then check back here.";

        var report = MiddleLayerService.GetLastPerfReport();
        PerfText.Text = report?.ToText() ?? "(no timing report yet)";

        BuildBreakdown(stats);

        _copyText = SummaryText.Text + Environment.NewLine + Environment.NewLine + PerfText.Text;
    }

    /// <summary>
    /// Render the component reports as a tree: Step → component → metric (recursing into nested
    /// dictionaries, e.g. ProviderByFile → file → provider → count). Handles the dynamic metric
    /// values generically via <see cref="IDictionary"/>, so any component's shape renders.
    /// </summary>
    private void BuildBreakdown(SearchStatistics stats)
    {
        BreakdownTree.RootNodes.Clear();

        if (stats?.componentReports == null || stats.componentReports.Count == 0)
        {
            BreakdownExpander.Visibility = Visibility.Collapsed;
            return;
        }
        BreakdownExpander.Visibility = Visibility.Visible;

        foreach (var step in stats.componentReports.Keys.OrderBy(k => k.ToString()))
        {
            var stepNode = new TreeViewNode { Content = $"Step: {step}", IsExpanded = true };
            foreach (var report in stats.componentReports[step])
            {
                var compNode = new TreeViewNode
                {
                    Content = string.IsNullOrEmpty(report.component)
                        ? report.summary
                        : $"{report.summary} — {report.component}",
                    IsExpanded = true,
                };
                if (report.metric != null)
                    foreach (var kv in report.metric)
                        AddMetric(compNode, kv.Key, (object)kv.Value);
                stepNode.Children.Add(compNode);
            }
            BreakdownTree.RootNodes.Add(stepNode);
        }
    }

    private static void AddMetric(TreeViewNode parent, string key, object value)
    {
        if (value is IDictionary dict)
        {
            var node = new TreeViewNode { Content = key, IsExpanded = false };
            foreach (DictionaryEntry e in dict)
                AddMetric(node, e.Key?.ToString() ?? "", e.Value);
            parent.Children.Add(node);
        }
        else
        {
            parent.Children.Add(new TreeViewNode { Content = $"{key} → {value}" });
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Render();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var pkg = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
            pkg.SetText(_copyText);
            global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
        }
        catch { /* clipboard contention — ignore */ }
    }
}
