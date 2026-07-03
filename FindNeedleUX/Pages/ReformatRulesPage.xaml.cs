using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FindNeedleUX.Services;
using FindNeedleUX.Services.PagedLogSource;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FindNeedleUX.Pages;

/// <summary>Manage message-reformat rules: regexes with named groups that break a dense one-line Message
/// into readable named fields in the results details panel. Built-ins (DISM, CBS) plus the user's own.
/// A live tester shows how the enabled rules break up a pasted message.</summary>
public sealed partial class ReformatRulesPage : Page
{
    private bool _suppressEnrichmentEvent;

    public ReformatRulesPage()
    {
        this.InitializeComponent();
        Loaded += (_, _) =>
        {
            // Reflect the persisted enrichment toggle (moved here from Settings → Integrations).
            _suppressEnrichmentEvent = true;
            EnrichmentEnabledCheck.IsChecked = ResultsViewerSettings.EnrichmentEnabled;
            _suppressEnrichmentEvent = false;
            RenderList();
        };
    }

    private void EnrichmentEnabledCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEnrichmentEvent) return;
        ResultsViewerSettings.EnrichmentEnabled = EnrichmentEnabledCheck.IsChecked == true;
    }

    private void RenderList()
    {
        ListHost.Children.Clear();
        var all = MessageReformatCatalog.GetAll();
        for (int i = 0; i < all.Count; i++)
            ListHost.Children.Add(BuildRow(all[i], i, all.Count));
        RefreshTest();
    }

    private FrameworkElement BuildRow(MessageReformatRule r, int index, int count)
    {
        var card = new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            Padding = new Thickness(12),
        };
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var toggle = new CheckBox { IsChecked = r.Enabled, VerticalAlignment = VerticalAlignment.Center, MinWidth = 0 };
        ToolTipService.SetToolTip(toggle, r.Enabled ? "Enabled" : "Disabled");
        toggle.Checked += (_, _) => MessageReformatCatalog.SetEnabled(r.Id, true);
        toggle.Unchecked += (_, _) => MessageReformatCatalog.SetEnabled(r.Id, false);
        // Re-render after toggle so the tester updates.
        toggle.Checked += (_, _) => RefreshTest();
        toggle.Unchecked += (_, _) => RefreshTest();
        Grid.SetColumn(toggle, 0); grid.Children.Add(toggle);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (!r.Enabled) card.Opacity = 0.6;
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleRow.Children.Add(new TextBlock { Text = r.Name, FontWeight = FontWeights.SemiBold });
        if (r.BuiltIn) titleRow.Children.Add(new TextBlock { Text = "built-in", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Foreground = Dim() });
        text.Children.Add(titleRow);
        if (!string.IsNullOrWhiteSpace(r.Description))
            text.Children.Add(new TextBlock { Text = r.Description, FontSize = 12, Foreground = Dim(), TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock
        {
            Text = r.Pattern, FontSize = 11, Foreground = Dim(), FontFamily = new FontFamily("Consolas"),
            TextTrimming = TextTrimming.CharacterEllipsis, IsTextSelectionEnabled = true,
        });
        Grid.SetColumn(text, 1); grid.Children.Add(text);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var up = new Button { Content = new FontIcon { Glyph = ((char)0xE70E).ToString(), FontSize = 12 }, IsEnabled = index > 0, Padding = new Thickness(6) };
        ToolTipService.SetToolTip(up, "Move up");
        up.Click += (_, _) => { MessageReformatCatalog.Move(r.Id, -1); RenderList(); };
        var down = new Button { Content = new FontIcon { Glyph = ((char)0xE70D).ToString(), FontSize = 12 }, IsEnabled = index < count - 1, Padding = new Thickness(6) };
        ToolTipService.SetToolTip(down, "Move down");
        down.Click += (_, _) => { MessageReformatCatalog.Move(r.Id, +1); RenderList(); };
        actions.Children.Add(up); actions.Children.Add(down);

        if (r.BuiltIn)
        {
            // Built-ins can be duplicated into an editable copy, not edited/removed in place.
            var dup = new Button { Content = "Duplicate" };
            dup.Click += async (_, _) =>
            {
                var copy = new MessageReformatRule { Name = r.Name + " (copy)", Description = r.Description, Match = r.Match, Pattern = r.Pattern };
                var edited = await ShowRuleDialog(copy);
                if (edited != null) { MessageReformatCatalog.Upsert(edited); RenderList(); }
            };
            actions.Children.Add(dup);
        }
        else
        {
            var edit = new Button { Content = "Edit" };
            edit.Click += async (_, _) => { var u = await ShowRuleDialog(r); if (u != null) { MessageReformatCatalog.Upsert(u); RenderList(); } };
            actions.Children.Add(edit);
            var remove = new Button { Content = "Remove" };
            remove.Click += (_, _) => { MessageReformatCatalog.Remove(r.Id); RenderList(); };
            actions.Children.Add(remove);
        }
        Grid.SetColumn(actions, 2); grid.Children.Add(actions);

        card.Child = grid;
        return card;
    }

    private static Brush Dim() => (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

    // Cap the preview sample so a huge log doesn't stall the dialog. First N rows is a representative,
    // cheap slice (the viewer's default sort/insertion order).
    private const int PreviewSampleSize = 3000;

    /// <summary>Snapshot up to <see cref="PreviewSampleSize"/> messages from the currently-loaded results
    /// for the live match preview. Returns null when nothing is loaded. <paramref name="total"/> is the
    /// full loaded row count so the preview can say "of N rows" vs "sampled N of M".</summary>
    private static List<string> SnapshotLoadedMessages(out int total)
    {
        total = 0;
        var src = MiddleLayerService.CurrentStreamingSearch?.Source;
        if (src == null) return null;
        try
        {
            total = src.TotalCount;
            return src.GetPage(FilterSpec.Empty, SortSpec.None, 0, PreviewSampleSize)
                      .Select(l => l.Message ?? "").ToList();
        }
        catch { return null; }
    }

    /// <summary>Evaluate the gate (Match) + extraction pattern against the sampled messages and describe the
    /// hit rate. Invalid regex is reported inline; an empty gate means "applies to every row".</summary>
    private void RenderMatchPreview(TextBlock target, List<string> sample, int total, string matchGate, string pattern)
    {
        if (target == null) return;
        if (sample == null || sample.Count == 0)
        {
            target.Text = "▸ Load a log and run a search to preview how many rows this rule matches.";
            return;
        }

        Regex gate = null;
        if (!string.IsNullOrWhiteSpace(matchGate))
        {
            try { gate = new Regex(matchGate, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)); }
            catch { target.Text = "⚠ Gate regex doesn't compile."; return; }
        }
        Regex pat = null;
        if (!string.IsNullOrWhiteSpace(pattern))
        {
            try { pat = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100)); }
            catch { target.Text = "⚠ Pattern regex doesn't compile."; return; }
        }

        var groupNames = pat?.GetGroupNames().Where(n => !int.TryParse(n, out _)).ToList() ?? new List<string>();
        int gateHits = 0, extractHits = 0;
        string example = null;
        try
        {
            foreach (var msg in sample)
            {
                var m = msg ?? "";
                if (gate != null && !gate.IsMatch(m)) continue;
                gateHits++;
                if (pat == null) continue;
                var mm = pat.Match(m);
                if (!mm.Success) continue;
                extractHits++;
                if (example == null && groupNames.Count > 0)
                {
                    example = string.Join(", ", groupNames.Take(3)
                        .Select(n => $"{n}={Truncate(mm.Groups[n].Value, 24)}"));
                }
            }
        }
        catch (RegexMatchTimeoutException) { target.Text = "⚠ Pattern is too slow to preview (catastrophic backtracking?)."; return; }

        string scope = total > sample.Count
            ? $"first {sample.Count:N0} of {total:N0} rows"
            : $"{sample.Count:N0} loaded rows";
        var sb = new System.Text.StringBuilder();
        sb.Append("▸ ");
        sb.Append(gate == null
            ? $"No gate — applies to all {scope}. "
            : $"Gate matches {gateHits:N0} of {scope}. ");
        if (pat != null)
        {
            sb.Append($"Pattern extracts fields from {extractHits:N0}");
            if (example != null) sb.Append($" (e.g. {example})");
            sb.Append('.');
        }
        target.Text = sb.ToString();
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max) + "…");

    private void TestInput_Changed(object sender, TextChangedEventArgs e) => RefreshTest();

    /// <summary>Run the enabled rules against the tester input and show what fields would be extracted.</summary>
    private void RefreshTest()
    {
        if (TestOutput == null) return;
        TestOutput.Children.Clear();
        var input = TestInput?.Text ?? "";
        if (string.IsNullOrWhiteSpace(input)) return;

        MessageReformatResult r = null;
        try { r = MessageReformatCatalog.Apply(input); } catch { }
        if (r == null)
        {
            TestOutput.Children.Add(new TextBlock { Text = "No enabled rule matched this message.", FontSize = 12, Foreground = Dim(), FontStyle = global::Windows.UI.Text.FontStyle.Italic });
            return;
        }
        TestOutput.Children.Add(new TextBlock { Text = $"Matched: {r.RuleName}", FontSize = 12, Foreground = Dim() });
        foreach (var (field, value) in r.Fields)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock { Text = field, FontWeight = FontWeights.SemiBold, MinWidth = 110 });
            row.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            TestOutput.Children.Add(row);
        }
    }

    private async void Button_AddRule(object sender, RoutedEventArgs e)
    {
        var rule = await ShowRuleDialog(null);
        if (rule != null) { MessageReformatCatalog.Upsert(rule); RenderList(); }
    }

    /// <summary>Add/edit dialog: name, optional gate (Match), the named-group regex, description. Validates
    /// that the pattern compiles and has at least one named group before saving.</summary>
    private async Task<MessageReformatRule> ShowRuleDialog(MessageReformatRule existing)
    {
        var name = new TextBox { Header = "Name", PlaceholderText = "e.g. MyApp log line", Text = existing?.Name ?? "" };
        var match = new TextBox { Header = "Applies when message matches (optional regex gate)", PlaceholderText = @"e.g. \bMyApp\b", Text = existing?.Match ?? "" };
        var pattern = new TextBox
        {
            Header = "Pattern — regex with named groups (?<Field>...)",
            PlaceholderText = @"^(?<Time>\S+) (?<Level>\w+) (?<Payload>.*)$",
            Text = existing?.Pattern ?? "", TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
            FontFamily = new FontFamily("Consolas"),
        };
        var desc = new TextBox { Header = "Description (optional)", Text = existing?.Description ?? "" };
        var error = new TextBlock { Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"], Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap };

        // Live match preview: sample the currently-loaded rows ONCE, then re-evaluate the gate/pattern
        // against that in-memory sample on every keystroke — so you see how many real rows a rule hits
        // before running a full search. No sample loaded ⇒ a hint to run a search first.
        var sample = SnapshotLoadedMessages(out int totalLoaded);
        var preview = new TextBlock
        {
            FontSize = 12, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
            Foreground = Dim(),
        };
        void UpdatePreview() => RenderMatchPreview(preview, sample, totalLoaded, match.Text, pattern.Text);
        match.TextChanged += (_, _) => UpdatePreview();
        pattern.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        var panel = new StackPanel { Spacing = 10, MinWidth = 520 };
        panel.Children.Add(name); panel.Children.Add(match); panel.Children.Add(pattern);
        panel.Children.Add(preview);
        panel.Children.Add(desc); panel.Children.Add(error);

        var dialog = new ContentDialog
        {
            Title = existing == null ? "Add reformat rule" : "Edit reformat rule",
            Content = new ScrollViewer { Content = panel, MaxHeight = 520, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 16, 0) },
            PrimaryButtonText = existing == null ? "Add" : "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text))
            { error.Text = "Name is required."; error.Visibility = Visibility.Visible; args.Cancel = true; return; }
            if (!MessageReformatCatalog.TryValidatePattern(pattern.Text, out var err))
            { error.Text = err; error.Visibility = Visibility.Visible; args.Cancel = true; }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;

        var result = existing ?? new MessageReformatRule();
        result.Name = name.Text.Trim();
        result.Match = match.Text?.Trim() ?? "";
        result.Pattern = pattern.Text.Trim();
        result.Description = desc.Text?.Trim() ?? "";
        result.Enabled = existing?.Enabled ?? true;
        return result;
    }
}
