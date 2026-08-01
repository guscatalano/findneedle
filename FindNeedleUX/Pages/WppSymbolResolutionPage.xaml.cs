using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FindNeedleUX.Services;
using FindPluginCore.Wpp.Symbols;
using FindPluginCore.Wpp.Symbols.WppSymbols;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FindNeedleUX.Pages;

/// <summary>
/// Dedicated workbench for resolving a WPP trace's decoding symbols — kept out of the general
/// Settings page so that page stays lean. Shows what the loaded trace needs, lets the user point at
/// a folder of PDBs/binaries (one symbol or a hundred), and reports the per-binary resolution status
/// (✓ found · ✗ missing/wrong · ⚠ no WPP data) with a Locate… action per unresolved symbol.
/// </summary>
public sealed partial class WppSymbolResolutionPage : Page
{
    private bool _simulate;

    public WppSymbolResolutionPage()
    {
        this.InitializeComponent();
        Loaded += (_, _) => OnLoaded();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _simulate = (e.Parameter as string) == "simulate";
    }

    private void OnLoaded()
    {
        LoadSourceFolders();
        SourceList.ItemsSource = _sourceFolders;
        UpdateSourceEmptyState();
        LoadTmfFolders();
        TmfList.ItemsSource = _tmfFolders;
        UpdateTmfEmptyState();
        SymbolPathBox.Text = MultilineFromSetting(ResultsViewerSettings.SymbolPath);
        if (_simulate)
        {
            // Dev scale check: 1000 WPP providers, 75% missing — see the page under a realistic backlog.
            var sim = WppSymbolResolver.GenerateSimulatedOutcomes(1000, 0.75);
            NeedsText.Text = "SIMULATION — 1000 WPP providers, 75% missing symbols. This is what a large, "
                + "mostly-unresolved trace looks like; scroll the list and use Locate…/Build to fix them.";
            StatusText.Text = new BuildTmfsResult { Outcomes = sim }.Summary;
            RenderStatus(sim);
            return;
        }
        RenderNeeds();
        RenderStatus(SafeDiagnose());
    }

    // ---- "What the current trace needs" ----

    private void RenderNeeds()
    {
        string missing = null;
        try { missing = MiddleLayerService.GetDecodeWarning()?.missingTmfs; } catch { }
        if (!string.IsNullOrEmpty(missing))
        {
            var guids = missing.Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            NeedsText.Text = $"The loaded trace has WPP events that couldn't decode. Needed TMF GUID(s) ({guids.Length}):\n  "
                + string.Join("\n  ", guids.Take(20))
                + (guids.Length > 20 ? $"\n  …and {guids.Length - 20} more" : "");
        }
        else
        {
            NeedsText.Text = "No undecoded WPP symbols detected for the currently loaded trace. "
                + "Open a WPP .etl that needs symbols, or use this page to pre-stage symbols before opening one.";
        }
    }

    // ---- Per-symbol status list ----

    private IReadOnlyList<SymbolOutcome> SafeDiagnose()
    {
        try { return WppSymbolResolver.Diagnose(ResultsViewerSettings.SymbolSourcePath, ResultsViewerSettings.SymbolPath); }
        catch { return Array.Empty<SymbolOutcome>(); }
    }

    private IReadOnlyList<SymbolOutcome> _allOutcomes = System.Array.Empty<SymbolOutcome>();

    private void RenderStatus(IReadOnlyList<SymbolOutcome> outcomes)
    {
        _allOutcomes = outcomes ?? System.Array.Empty<SymbolOutcome>();
        if (_allOutcomes.Count == 0)
        {
            StatusList.ItemsSource = null;
            TableArea.Visibility = Visibility.Collapsed;
            EmptyStatusText.Visibility = Visibility.Visible;
            return;
        }
        EmptyStatusText.Visibility = Visibility.Collapsed;
        TableArea.Visibility = Visibility.Visible;
        ApplyFilter();
    }

    /// <summary>Rebuild the visible rows from <see cref="_allOutcomes"/> per the search box + "unresolved
    /// only" toggle. Cheap enough to run on every keystroke even at a thousand providers.</summary>
    private void ApplyFilter()
    {
        if (StatusList == null) return;
        var q = (SymbolSearchBox?.Text ?? "").Trim().ToLowerInvariant();
        bool unresolvedOnly = UnresolvedOnlyCheck?.IsChecked == true;
        var rows = new List<SymbolRow>();
        foreach (var o in _allOutcomes)
        {
            if (unresolvedOnly && !o.IsProblem) continue;
            var row = new SymbolRow(o);
            if (q.Length > 0 && !row.SearchText.Contains(q)) continue;
            rows.Add(row);
        }
        StatusList.ItemsSource = rows;
        SymbolCountText.Text = rows.Count == _allOutcomes.Count
            ? $"{_allOutcomes.Count} providers"
            : $"showing {rows.Count} of {_allOutcomes.Count}";
    }

    private void SymbolSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void FilterToggle_Changed(object sender, RoutedEventArgs e) => ApplyFilter();

    internal static Microsoft.UI.Xaml.Media.Brush OutcomeBrush(SymbolStatus s) => s switch
    {
        SymbolStatus.Resolved or SymbolStatus.FoundLocal
            => new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.SeaGreen),
        SymbolStatus.NoTmf
            => new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Goldenrod),
        _ => new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.IndianRed),
    };

    // ---- Symbol source & TMF folders: editable folder lists ----

    private readonly System.Collections.ObjectModel.ObservableCollection<string> _sourceFolders = new();
    private readonly System.Collections.ObjectModel.ObservableCollection<string> _tmfFolders = new();

    private void LoadSourceFolders() => Reload(_sourceFolders, ResultsViewerSettings.SymbolSourcePath);
    private void LoadTmfFolders() => Reload(_tmfFolders, ResultsViewerSettings.TraceFormatSearchPath);

    private static void Reload(System.Collections.ObjectModel.ObservableCollection<string> col, string setting)
    {
        col.Clear();
        foreach (var f in SymbolPathText.Split(setting))
            if (!Contains(col, f)) col.Add(f);
    }

    private static bool Contains(System.Collections.ObjectModel.ObservableCollection<string> col, string folder)
    {
        foreach (var f in col) if (string.Equals(f, folder, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool AddFolderTo(System.Collections.ObjectModel.ObservableCollection<string> col, string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return false;
        folder = folder.Trim();
        if (Contains(col, folder)) return false;
        col.Add(folder);
        return true;
    }

    // -- Symbol source (PDBs/binaries) --

    private void PersistSourceFolders()
    {
        ResultsViewerSettings.SymbolSourcePath = SymbolPathText.Join(_sourceFolders);
        UpdateSourceEmptyState();
    }

    private void UpdateSourceEmptyState()
    {
        if (SourceEmptyText != null)
            SourceEmptyText.Visibility = _sourceFolders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AppendSourceFolder(string folder) { AddFolderTo(_sourceFolders, folder); PersistSourceFolders(); }

    private void AddSymbolFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = PickFolder();
        if (folder != null) { AppendSourceFolder(folder); RefreshStatusFromSource(); }
    }

    private void RemoveSourceFolder_Click(object sender, RoutedEventArgs e)
    {
        if (RemoveByTag(_sourceFolders, sender)) { PersistSourceFolders(); RefreshStatusFromSource(); }
    }

    private void SourceList_DragOver(object sender, Microsoft.UI.Xaml.DragEventArgs e) => AcceptFolderDrag(e);

    private async void SourceList_Drop(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        if (await AddDroppedFoldersAsync(e, _sourceFolders)) { PersistSourceFolders(); RefreshStatusFromSource(); }
    }

    // -- TMF folders (ready-made .tmf files — the fastest path) --

    private void PersistTmfFolders()
    {
        ResultsViewerSettings.TraceFormatSearchPath = SymbolPathText.Join(_tmfFolders);
        UpdateTmfEmptyState();
    }

    private void UpdateTmfEmptyState()
    {
        if (TmfEmptyText != null)
            TmfEmptyText.Visibility = _tmfFolders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddTmfFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = PickFolder();
        if (folder != null && AddFolderTo(_tmfFolders, folder)) PersistTmfFolders();
    }

    private void RemoveTmfFolder_Click(object sender, RoutedEventArgs e)
    {
        if (RemoveByTag(_tmfFolders, sender)) PersistTmfFolders();
    }

    private void TmfList_DragOver(object sender, Microsoft.UI.Xaml.DragEventArgs e) => AcceptFolderDrag(e);

    private async void TmfList_Drop(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        if (await AddDroppedFoldersAsync(e, _tmfFolders)) PersistTmfFolders();
    }

    // -- shared folder-list plumbing --

    private static bool RemoveByTag(System.Collections.ObjectModel.ObservableCollection<string> col, object sender)
    {
        var path = (sender as FrameworkElement)?.Tag as string;
        if (string.IsNullOrEmpty(path)) return false;
        for (int i = 0; i < col.Count; i++)
            if (string.Equals(col[i], path, StringComparison.OrdinalIgnoreCase)) { col.RemoveAt(i); return true; }
        return false;
    }

    private static void AcceptFolderDrag(Microsoft.UI.Xaml.DragEventArgs e)
    {
        if (e.DataView.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            if (e.DragUIOverride != null) e.DragUIOverride.Caption = "Add folder";
        }
    }

    private static async Task<bool> AddDroppedFoldersAsync(
        Microsoft.UI.Xaml.DragEventArgs e, System.Collections.ObjectModel.ObservableCollection<string> col)
    {
        if (!e.DataView.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)) return false;
        var deferral = e.GetDeferral();
        bool any = false;
        try
        {
            foreach (var it in await e.DataView.GetStorageItemsAsync())
            {
                if (it is global::Windows.Storage.StorageFolder folder) any |= AddFolderTo(col, folder.Path);
                else if (it is global::Windows.Storage.StorageFile file)
                {
                    var dir = System.IO.Path.GetDirectoryName(file.Path);
                    if (!string.IsNullOrEmpty(dir)) any |= AddFolderTo(col, dir);
                }
            }
        }
        catch { /* best-effort drop */ }
        finally { deferral.Complete(); }
        return any;
    }

    private static string PickFolder()
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(WindowUtil.GetMainWindow());
        return Win32FileDialog.PickFolder(hWnd);
    }

    // ---- Symbol path: multiline (_NT_SYMBOL_PATH), one element per line ----

    private void SymbolPathBox_LostFocus(object sender, RoutedEventArgs e)
        => ResultsViewerSettings.SymbolPath = SettingFromMultiline(SymbolPathBox.Text);

    private static string MultilineFromSetting(string s) => SymbolPathText.ToLines(s);
    private static string SettingFromMultiline(string t) => SymbolPathText.FromLines(t);

    /// <summary>Append one _NT_SYMBOL_PATH element as a new line (de-duped) and persist.</summary>
    private void AppendSymPathLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        line = line.Trim();
        var cur = SymbolPathBox.Text ?? "";
        foreach (var l in cur.Split('\n'))
            if (string.Equals(l.Trim(), line, StringComparison.OrdinalIgnoreCase)) return; // already present
        SymbolPathBox.Text = string.IsNullOrWhiteSpace(cur) ? line : cur.TrimEnd('\r', '\n') + "\n" + line;
        ResultsViewerSettings.SymbolPath = SettingFromMultiline(SymbolPathBox.Text);
    }

    private void AddMsServer_Click(object sender, RoutedEventArgs e)
        => AppendSymPathLine($"srv*{WppSymbolResolver.PdbCacheDir}*https://msdl.microsoft.com/download/symbols");

    private void AddSymPathFolder_Click(object sender, RoutedEventArgs e)
    {
        var f = PickFolder();
        if (f != null) AppendSymPathLine(f);
    }

    private async void AddSymServer_Click(object sender, RoutedEventArgs e)
    {
        var url = new TextBox { PlaceholderText = "https://symbols.example.com/download/symbols", Width = 380 };
        var cache = new TextBox { Text = WppSymbolResolver.PdbCacheDir, Width = 380 };
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = "Symbol server URL" });
        panel.Children.Add(url);
        panel.Children.Add(new TextBlock { Text = "Local cache folder", Margin = new Thickness(0, 6, 0, 0) });
        panel.Children.Add(cache);
        var dlg = new ContentDialog
        {
            Title = "Add symbol server",
            Content = panel,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(url.Text))
        {
            var c = (cache.Text ?? "").Trim();
            AppendSymPathLine(c.Length > 0 ? $"srv*{c}*{url.Text.Trim()}" : $"srv*{url.Text.Trim()}");
        }
    }

    // ---- Build / resolve / reopen ----

    private void RefreshStatusFromSource()
    {
        if (_simulate) return; // keep the synthetic set on screen
        RenderStatus(SafeDiagnose());
    }

    private async void Locate_Click(object sender, RoutedEventArgs e)
    {
        var folder = PickFolder();
        if (folder == null) return;
        AppendSourceFolder(folder);
        await BuildAndReopenAsync();
    }

    private async void RowLocate_Click(object sender, RoutedEventArgs e)
    {
        // Per-symbol affordance: resolution is folder-based, so picking a folder resolves this symbol
        // (and any others in it) in one build — matching the "one at a time" flow without N separate picks.
        var folder = PickFolder();
        if (folder == null) return;
        AppendSourceFolder(folder);
        await BuildAndReopenAsync();
    }

    private async void BuildReopen_Click(object sender, RoutedEventArgs e) => await BuildAndReopenAsync();

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        ResultsViewerSettings.SymbolPath = SettingFromMultiline(SymbolPathBox.Text);
        RenderNeeds();
        RenderStatus(SafeDiagnose());
    }

    private async Task BuildAndReopenAsync()
    {
        // The folder list persists as it's edited; make sure the (multiline) symbol path is saved too.
        ResultsViewerSettings.SymbolPath = SettingFromMultiline(SymbolPathBox.Text);
        var source = ResultsViewerSettings.SymbolSourcePath;
        var symPath = ResultsViewerSettings.SymbolPath;

        LocateButton.IsEnabled = BuildButton.IsEnabled = false;
        StatusText.Text = "Building TMFs from symbols…";
        BuildTmfsResult result;
        try { result = await Task.Run(() => WppSymbolResolver.BuildTmfs(source, symPath)); }
        catch (Exception ex) { result = new BuildTmfsResult { Log = ex.Message }; }

        // Put the freshly built TMF cache on the search path so the reopen decodes with it.
        TraceFormatConfig.Apply();
        LocateButton.IsEnabled = BuildButton.IsEnabled = true;
        StatusText.Text = result.Summary;
        RenderStatus(result.Outcomes);

        // Reopen the loaded trace so decoding picks up the new TMFs (navigates to the results viewer;
        // if symbols are still missing its banner reappears with the updated diagnosis).
        MainWindowActions.RerunSearch();
    }
}

/// <summary>Row view-model for the status table — pre-typed, per-column properties so the DataTemplate
/// binds with x:Bind and no value converters. Cells are individually selectable (copy a GUID etc.).</summary>
public sealed class SymbolRow
{
    public SymbolOutcome Outcome { get; init; }

    public string Glyph => Outcome.Glyph;
    public Microsoft.UI.Xaml.Media.Brush GlyphBrush => WppSymbolResolutionPage.OutcomeBrush(Outcome.Status);

    public string Provider => Outcome.Binary ?? Outcome.PdbName ?? "(unknown)";
    public string Guid => Outcome.Guid ?? "";
    public string AgeText => string.IsNullOrEmpty(Outcome.Guid) ? "" : Outcome.Age.ToString();
    public string Note => Outcome.Detail ?? "";

    public bool IsProblem => Outcome.IsProblem;
    public Microsoft.UI.Xaml.Visibility LocateVisibility
        => Outcome.IsProblem ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>Lower-cased haystack for the filter box (provider, PDB, GUID, note, status).</summary>
    public string SearchText { get; }

    public SymbolRow() { }
    public SymbolRow(SymbolOutcome outcome)
    {
        Outcome = outcome;
        SearchText = ($"{outcome.Binary} {outcome.PdbName} {outcome.Guid} {outcome.Detail} {outcome.Status}")
            .ToLowerInvariant();
    }
}
