using System;
using System.Linq;
using System.Threading.Tasks;
using FindNeedleUX.Services;
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

        var panel = new StackPanel { Spacing = 10, MinWidth = 520 };
        panel.Children.Add(name); panel.Children.Add(match); panel.Children.Add(pattern); panel.Children.Add(desc); panel.Children.Add(error);

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
