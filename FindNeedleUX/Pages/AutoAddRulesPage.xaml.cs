using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FindNeedleUX.Services;
using FindPluginCore.Searching.AutoRules;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FindNeedleUX.Pages;

/// <summary>Row shown in the auto-add rules list (a flattened view of an <see cref="AutoRuleEntry"/>).</summary>
public sealed class AutoRuleRow
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string RulePath { get; init; } = "";
    public string Summary { get; init; } = "";
    public bool BuiltIn { get; init; }
    public bool Enabled { get; init; }
    public bool CanRemove => !BuiltIn;
    public Visibility BuiltInVisibility => BuiltIn ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>
/// Manage the auto-add rules registry: the master on/off switch, per-rule toggles, condition editing,
/// adding your own rule files, and the bundled common-rules library (shown with a "Built-in" badge).
/// </summary>
public sealed partial class AutoAddRulesPage : Page
{
    private readonly ObservableCollection<AutoRuleRow> _rows = new();
    private bool _loading;

    public AutoAddRulesPage()
    {
        this.InitializeComponent();
        EntriesList.ItemsSource = _rows;
        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        _loading = true;
        try
        {
            MasterSwitch.IsOn = AutoRulesStore.Enabled;
            _rows.Clear();
            foreach (var e in AutoRulesStore.Entries)
            {
                _rows.Add(new AutoRuleRow
                {
                    Id = e.Id,
                    Name = string.IsNullOrWhiteSpace(e.Name) ? System.IO.Path.GetFileName(e.RulePath) : e.Name,
                    RulePath = e.RulePath,
                    Summary = SummarizeCondition(e.Condition),
                    BuiltIn = e.BuiltIn,
                    Enabled = e.Enabled,
                });
            }
        }
        finally { _loading = false; }
    }

    private static string SummarizeCondition(AutoRuleCondition c)
    {
        if (c == null) return "No condition (never matches)";
        if (c.Always) return "Always — applies to every search";
        var parts = new List<string>();
        if (c.Extensions is { Count: > 0 }) parts.Add("ext: " + string.Join(", ", c.Extensions));
        if (c.SourceTypes is { Count: > 0 }) parts.Add("source: " + string.Join(", ", c.SourceTypes));
        if (c.PathGlobs is { Count: > 0 }) parts.Add("path: " + string.Join(", ", c.PathGlobs));
        if (c.Providers is { Count: > 0 }) parts.Add("provider: " + string.Join(", ", c.Providers));
        if (c.MinBuild.HasValue || c.MaxBuild.HasValue)
            parts.Add($"build: {c.MinBuild?.ToString() ?? "*"}–{c.MaxBuild?.ToString() ?? "*"}");
        return parts.Count > 0
            ? "When " + string.Join("; ", parts)
            : "No condition set — click \"Edit condition\" to choose when it applies";
    }

    private void MasterSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        AutoRulesStore.Enabled = MasterSwitch.IsOn;
    }

    private void EntryToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (sender is ToggleSwitch sw && sw.Tag is string id)
            AutoRulesStore.SetEntryEnabled(id, sw.IsOn);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string id)
        {
            AutoRulesStore.Remove(id);
            Refresh();
        }
    }

    private void AddFile_Click(object sender, RoutedEventArgs e)
    {
        var window = WindowUtil.GetWindowForElement(this);
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var path = Win32FileDialog.OpenFile(hWnd, new (string, string)[] { ("Rules JSON", "*.json") });
        if (string.IsNullOrWhiteSpace(path)) return;

        var entry = new AutoRuleEntry
        {
            Name = System.IO.Path.GetFileNameWithoutExtension(path),
            RulePath = path,
            BuiltIn = false,
            Enabled = true,
            Condition = new AutoRuleCondition(), // user sets it next via the editor
        };
        AutoRulesStore.Upsert(entry);
        Refresh();
        ShowStatus($"Added “{entry.Name}”. Set when it applies with “Edit condition”.", InfoBarSeverity.Success);
    }

    private void RevealLibrary_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = CommonRuleLibrary.LibraryDir;
            System.IO.Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { ShowStatus("Couldn't open the library folder: " + ex.Message, InfoBarSeverity.Error); }
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string id) return;
        var entry = AutoRulesStore.Entries.FirstOrDefault(x => x.Id == id);
        if (entry == null) return;

        var c = entry.Condition ?? new AutoRuleCondition();

        var always = new CheckBox { Content = "Always (apply to every search)", IsChecked = c.Always };
        var ext = new TextBox { Header = "File extensions (comma-separated, e.g. .etl, .evtx)", Text = string.Join(", ", c.Extensions ?? new()) };
        var src = new TextBox { Header = "Source kinds (ETW, EventLog, Folder, File, Zip)", Text = string.Join(", ", c.SourceTypes ?? new()) };
        var glob = new TextBox { Header = "Path globs (e.g. *panther*, *cbs*.log)", Text = string.Join(", ", c.PathGlobs ?? new()) };
        var prov = new TextBox { Header = "Provider names", Text = string.Join(", ", c.Providers ?? new()) };
        var minB = new TextBox { Header = "Min build (optional)", Text = c.MinBuild?.ToString() ?? "" };
        var maxB = new TextBox { Header = "Max build (optional)", Text = c.MaxBuild?.ToString() ?? "" };

        var panel = new StackPanel { Spacing = 10, Width = 420 };
        panel.Children.Add(new TextBlock
        {
            Text = "The rule auto-adds when every filled-in criterion matches. Leave a field blank to ignore it.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
        });
        foreach (var ctrl in new FrameworkElement[] { always, ext, src, glob, prov, minB, maxB })
            panel.Children.Add(ctrl);

        var dialog = new ContentDialog
        {
            Title = $"Condition — {entry.Name}",
            Content = new ScrollViewer { Content = panel, MaxHeight = 460 },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        entry.Condition = new AutoRuleCondition
        {
            Always = always.IsChecked == true,
            Extensions = SplitList(ext.Text),
            SourceTypes = SplitList(src.Text),
            PathGlobs = SplitList(glob.Text),
            Providers = SplitList(prov.Text),
            MinBuild = int.TryParse(minB.Text.Trim(), out var mn) ? mn : null,
            MaxBuild = int.TryParse(maxB.Text.Trim(), out var mx) ? mx : null,
        };
        AutoRulesStore.Upsert(entry);
        Refresh();
        ShowStatus($"Updated condition for “{entry.Name}”.", InfoBarSeverity.Success);
    }

    private static List<string> SplitList(string s) =>
        string.IsNullOrWhiteSpace(s)
            ? new List<string>()
            : s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }
}
