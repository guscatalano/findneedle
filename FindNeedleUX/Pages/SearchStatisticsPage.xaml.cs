using System;
using System.Collections;
using System.IO;
using System.Linq;
using FindNeedleCoreUtils;
using FindNeedlePluginLib;
using FindNeedleUX.Services;
using FindPluginCore.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FindNeedleUX.Pages;

/// <summary>
/// Search statistics &amp; timing dashboard for the most recent run. Pulls accurate volumes/timings
/// from <see cref="SearchRunReport"/> (rows stored/scanned, storage backend, cache, phase timings,
/// hints) and the memory snapshots + per-component/provider/file breakdown from
/// <see cref="SearchStatistics"/>.
/// </summary>
public sealed partial class SearchStatisticsPage : Page
{
    private string _copyText = "";
    private readonly System.Collections.Generic.List<string> _rawOutputPaths = new();
    private readonly System.Collections.Generic.List<string> _resolveLogPaths = new();
    private SearchStatistics _statsCache;
    private string _breakdownFilter = "";

    public SearchStatisticsPage()
    {
        this.InitializeComponent();
        Loaded += (_, _) => Render();
    }

    private static Brush Secondary => (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
    private static Brush CardBg => (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];
    private static Brush CardStroke => (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
    private static Brush Accent => (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];

    private void Render()
    {
        var report = MiddleLayerService.GetLastPerfReport();
        var stats = MiddleLayerService.GetStats();
        bool any = report != null || stats != null;

        EmptyState.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        Cards.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        SubtitleText.Text = report != null
            ? $"Most recent search · {report.StartedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
            : "Most recent search";

        _statsCache = stats;
        BuildCards(report, stats);
        BuildPhaseBars(report);
        BuildNotes(report);
        BuildBreakdown(stats);

        SummaryText.Text = stats != null ? stats.GetSummaryReport().TrimEnd() : "(no statistics)";
        PerfText.Text = report?.ToText() ?? "(no timing report yet)";
        _copyText = SummaryText.Text + Environment.NewLine + Environment.NewLine + PerfText.Text;
        CopyButton.IsEnabled = any;
        OpenRawOutputButton.Visibility = _rawOutputPaths.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        OpenResolveLogButton.Visibility = _resolveLogPaths.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ----- stat cards -----
    private void BuildCards(SearchRunReport report, SearchStatistics stats)
    {
        Cards.Children.Clear();

        // Accurate row counts: prefer the perf report, fall back to the live storage stats.
        long matched = report?.StoredRows ?? 0;
        long scanned = report?.RawRows ?? 0;
        try
        {
            var s = MiddleLayerService.GetSearchStorage()?.GetStatistics();
            if (s.HasValue)
            {
                if (matched == 0) matched = s.Value.filteredRecordCount;
                if (scanned == 0) scanned = s.Value.rawRecordCount;
            }
        }
        catch { /* storage not available — use report values */ }

        Cards.Children.Add(Card("Rows matched", matched.ToString("N0")));
        if (scanned > 0 && scanned != matched)
            Cards.Children.Add(Card("Rows scanned", scanned.ToString("N0")));

        if (report != null)
        {
            Cards.Children.Add(Card("Total time", FmtMs(report.TotalMs),
                $"search {FmtMs(report.SearchMs)} · viewer {FmtMs(report.ViewerMs)}"));
            Cards.Children.Add(Card("Storage", ShortStorage(report.StorageType), report.StorageMode));
            Cards.Children.Add(Card("Cache",
                report.CacheHit ? "HIT" : report.CacheWritten ? "written" : "—",
                report.CacheHit ? "scan skipped" : report.CacheWritten ? "saved for next time" : null));
        }

        long mem = stats?.GetPrivateBytes(SearchStep.AtSearch) ?? 0;
        if (mem <= 0) mem = stats?.GetPrivateBytes(SearchStep.AtLoad) ?? 0;
        if (mem > 0) Cards.Children.Add(Card("Memory", ByteUtils.BytesToFriendlyString(mem), "private, at search"));
    }

    private FrameworkElement Card(string label, string value, string sub = null)
    {
        var sp = new StackPanel { Spacing = 2 };
        sp.Children.Add(new TextBlock { Text = value, FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        sp.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Secondary });
        if (!string.IsNullOrEmpty(sub))
            sp.Children.Add(new TextBlock { Text = sub, FontSize = 11, Foreground = Secondary, TextWrapping = TextWrapping.Wrap });
        return new Border
        {
            Background = CardBg,
            BorderBrush = CardStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 12, 16, 12),
            MinWidth = 150,
            Child = sp,
        };
    }

    // ----- phase bars -----
    private void BuildPhaseBars(SearchRunReport report)
    {
        PhaseBars.Children.Clear();
        if (report == null) { PhasesCard.Visibility = Visibility.Collapsed; return; }
        PhasesCard.Visibility = Visibility.Visible;

        var phases = report.TopPhases(8).Where(p => p.ElapsedMs > 0).ToList();
        if (phases.Count == 0)
        {
            PhaseBars.Children.Add(new TextBlock { Text = "No phase timings recorded.", Foreground = Secondary, FontSize = 12 });
            return;
        }
        long max = Math.Max(1, phases.Max(p => p.ElapsedMs));
        foreach (var p in phases)
            PhaseBars.Children.Add(PhaseRow(p.Name, p.ElapsedMs, max));
    }

    private FrameworkElement PhaseRow(string name, long ms, long max)
    {
        var grid = new Grid { ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });

        var label = new TextBlock { Text = name, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(label, name);
        Grid.SetColumn(label, 0); grid.Children.Add(label);

        double frac = Math.Max(0.03, Math.Min(1.0, (double)ms / max));
        var barArea = new Grid();
        barArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(frac, GridUnitType.Star) });
        barArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - frac, GridUnitType.Star) });
        var bar = new Border { Background = Accent, CornerRadius = new CornerRadius(3), Height = 14, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetColumn(bar, 0); barArea.Children.Add(bar);
        Grid.SetColumn(barArea, 1); grid.Children.Add(barArea);

        var msText = new TextBlock { Text = FmtMs(ms), FontSize = 12, Foreground = Secondary, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(msText, 2); grid.Children.Add(msText);

        return grid;
    }

    // ----- notes -----
    private void BuildNotes(SearchRunReport report)
    {
        Notes.Children.Clear();
        if (report == null) { NotesCard.Visibility = Visibility.Collapsed; return; }
        NotesCard.Visibility = Visibility.Visible;
        foreach (var h in report.BuildHints())
            Notes.Children.Add(new TextBlock { Text = "• " + h, TextWrapping = TextWrapping.Wrap, FontSize = 12 });
    }

    // ----- "how each source decoded" (readable, per-file) -----
    private void BuildDecode(SearchStatistics stats)
    {
        DecodeList.Children.Clear();
        bool any = false;
        if (stats?.componentReports != null)
            foreach (var list in stats.componentReports.Values)
                foreach (var report in list)
                {
                    if (report?.summary != "DecodeByFile" || report.metric == null) continue;
                    foreach (var kv in report.metric)
                    {
                        if (kv.Value is not IDictionary d) continue;
                        DecodeList.Children.Add(DecodeRow(Path.GetFileName(kv.Key), d));
                        any = true;
                    }
                }
        DecodeCard.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
    }

    private FrameworkElement DecodeRow(string file, IDictionary d)
    {
        string Get(string k) => d.Contains(k) ? d[k]?.ToString() : null;
        var sp = new StackPanel { Spacing = 4 };
        sp.Children.Add(new TextBlock { Text = file, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });

        var method = Get("method") ?? "(unknown)";
        bool bad = method.Contains("missing", StringComparison.OrdinalIgnoreCase);
        sp.Children.Add(new TextBlock
        {
            Text = method,
            FontSize = 12,
            Foreground = bad ? (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"] : Secondary,
            TextWrapping = TextWrapping.Wrap,
        });

        var bits = new System.Collections.Generic.List<string>();
        void Bit(string label, string key) { var v = Get(key); if (!string.IsNullOrEmpty(v)) bits.Add($"{label} {v}"); }
        Bit("rows", "rows");
        Bit("decodable", "decodable");
        Bit("events", "eventsProcessed");
        Bit("unknown", "unknowns");
        Bit("lost", "eventsLost");
        Bit("errors", "formatErrors");
        Bit("time", "elapsed");
        if (bits.Count > 0)
            sp.Children.Add(new TextBlock { Text = string.Join("  ·  ", bits), FontSize = 12, Foreground = Secondary, TextWrapping = TextWrapping.Wrap });

        var providers = Get("providers");
        if (!string.IsNullOrEmpty(providers))
            sp.Children.Add(new TextBlock { Text = "providers: " + providers, FontSize = 12, Foreground = Secondary, TextWrapping = TextWrapping.Wrap });

        var missing = Get("missingTmfs");
        if (!string.IsNullOrEmpty(missing))
            sp.Children.Add(new TextBlock { Text = "missing TMF: " + missing, FontSize = 12, FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap });

        return new Border
        {
            Background = CardBg,
            BorderBrush = CardStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 8),
            Child = sp,
        };
    }

    // ----- component breakdown tree -----
    private void BuildBreakdown(SearchStatistics stats)
    {
        BreakdownTree.RootNodes.Clear();
        _rawOutputPaths.Clear();
        _resolveLogPaths.Clear();
        CollectRawOutputs(stats);
        BuildDecode(stats);
        if (stats?.componentReports == null || stats.componentReports.Count == 0)
        {
            BreakdownExpander.Visibility = Visibility.Collapsed;
            return;
        }
        BreakdownExpander.Visibility = Visibility.Visible;

        string f = _breakdownFilter;
        foreach (var step in stats.componentReports.Keys.OrderBy(k => k.ToString()))
        {
            var stepNode = new TreeViewNode { Content = $"Step: {step}", IsExpanded = true };
            foreach (var report in stats.componentReports[step])
            {
                var title = string.IsNullOrEmpty(report.component) ? report.summary : $"{report.summary} — {report.component}";
                var compNode = new TreeViewNode { Content = title, IsExpanded = true };
                if (report.metric != null)
                    foreach (var kv in report.metric)
                    {
                        var child = BuildMetricNode(kv.Key, (object)kv.Value, f);
                        if (child != null) compNode.Children.Add(child);
                    }
                if (string.IsNullOrEmpty(f) || compNode.Children.Count > 0 || Match(title, f))
                    stepNode.Children.Add(compNode);
            }
            if (stepNode.Children.Count > 0)
                BreakdownTree.RootNodes.Add(stepNode);
        }
    }

    private static bool Match(string s, string f) => s != null && s.Contains(f, StringComparison.OrdinalIgnoreCase);

    private void BreakdownFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        _breakdownFilter = BreakdownFilter.Text?.Trim() ?? "";
        BuildBreakdown(_statsCache);
    }

    private void ExpandAll_Click(object sender, RoutedEventArgs e) => SetExpanded(BreakdownTree.RootNodes, true);
    private void CollapseAll_Click(object sender, RoutedEventArgs e) => SetExpanded(BreakdownTree.RootNodes, false);

    private static void SetExpanded(System.Collections.Generic.IList<TreeViewNode> nodes, bool expanded)
    {
        foreach (var n in nodes)
        {
            n.IsExpanded = expanded;
            SetExpanded(n.Children, expanded);
        }
    }

    /// <summary>Pull any "rawOutput" file paths out of the DecodeByFile reports (for the open button).</summary>
    private void CollectRawOutputs(SearchStatistics stats)
    {
        if (stats?.componentReports == null) return;
        foreach (var list in stats.componentReports.Values)
            foreach (var report in list)
            {
                if (report?.summary != "DecodeByFile" || report.metric == null) continue;
                foreach (var perFile in report.metric.Values)
                {
                    if (perFile is not IDictionary dict) continue;
                    if (dict["rawOutput"] is string p && !string.IsNullOrEmpty(p) && File.Exists(p) && !_rawOutputPaths.Contains(p))
                        _rawOutputPaths.Add(p);
                    if (dict["resolveLog"] is string r && !string.IsNullOrEmpty(r) && File.Exists(r) && !_resolveLogPaths.Contains(r))
                        _resolveLogPaths.Add(r);
                }
            }
    }

    private void OpenRawOutput_Click(object sender, RoutedEventArgs e) => OpenFiles(_rawOutputPaths);
    private void OpenResolveLog_Click(object sender, RoutedEventArgs e) => OpenFiles(_resolveLogPaths);

    private static void OpenFiles(System.Collections.Generic.IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = p, UseShellExecute = true });
            }
            catch { /* no handler / file gone — ignore */ }
        }
    }

    /// <summary>
    /// Build a tree node for one metric (recursing into nested dictionaries). When a filter is set,
    /// returns null for branches that neither match nor contain a match, and auto-expands what's left.
    /// </summary>
    private static TreeViewNode BuildMetricNode(string key, object value, string filter)
    {
        if (value is IDictionary dict)
        {
            var node = new TreeViewNode { Content = key, IsExpanded = !string.IsNullOrEmpty(filter) };
            foreach (DictionaryEntry e in dict)
            {
                var child = BuildMetricNode(e.Key?.ToString() ?? "", e.Value, filter);
                if (child != null) node.Children.Add(child);
            }
            if (string.IsNullOrEmpty(filter) || node.Children.Count > 0 || Match(key, filter))
                return node;
            return null;
        }

        var text = $"{key} → {value}";
        if (string.IsNullOrEmpty(filter) || Match(text, filter))
            return new TreeViewNode { Content = text };
        return null;
    }

    // ----- helpers -----
    private static string FmtMs(long ms) => ms < 1000 ? $"{ms} ms" : $"{ms / 1000.0:0.00} s";

    private static string ShortStorage(string s)
    {
        if (string.IsNullOrEmpty(s)) return "—";
        return s.Replace("Storage", "");
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
