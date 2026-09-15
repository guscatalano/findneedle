using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using findneedle.Implementations;
using FindNeedleCoreUtils;
using FindNeedlePluginLib;
using FindNeedleUX.Services;
using FindPluginCore.Implementations.Storage;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FindNeedleUX.Pages;

/// <summary>
/// Home. One open model: everything on the left (Open log file / folder / with rules, Recent searches,
/// Known logs) lands in the workspace on the right through <see cref="MainWindow.OpenIntoWorkspaceAsync"/>,
/// governed by the Add / Replace / Ask card. The customizable Tools tiles (QuickActionCatalog) live
/// below the cards; navigating here with <see cref="CustomizeParameter"/> opens them in edit mode.
/// </summary>
public sealed partial class WelcomePage : Page
{
    /// <summary>Navigation parameter that opens the Tools tiles in edit mode ("Customize…").</summary>
    public const string CustomizeParameter = "customize";

    private const int RecentCount = 3;
    private const int KnownLogsCount = 3;
    private const int MaxSourceRows = 6;

    // Known-logs order of preference for the Home card (the rest of the catalog is on the Known logs page).
    private static readonly string[] PreferredKnownLogIds = { "builtin:winevt", "builtin:wu", "builtin:panther", "builtin:cbs" };

    private bool _editMode;
    private int _recentLoadGeneration;

    public WelcomePage()
    {
        this.InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string p && string.Equals(p, CustomizeParameter, StringComparison.OrdinalIgnoreCase))
        {
            _editMode = true;
            if (IsLoaded) RenderQuickActions();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        MiddleLayerService.StateChanged += OnWorkspaceStateChanged;
        HomeSectionCatalog.Changed += OnHomeLayoutChanged;
        InitSections();
        ApplyHomeLayout();
        RenderWorkspace();
        RenderKnownLogs();
        RenderQuickActions();
        LoadRecentAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        MiddleLayerService.StateChanged -= OnWorkspaceStateChanged;
        HomeSectionCatalog.Changed -= OnHomeLayoutChanged;
    }

    // ===================== Sections: collapse, hide, reorder (HomeSectionCatalog) =====================
    // Every section except the Open card gets a chevron in its header (collapse to the header, state
    // remembered) and appears in the Customize Home pencil (show / hide / move up / move down). The
    // columns are structural; sections only move within their own column.

    private sealed class SectionUi
    {
        public HomeSection Section;
        public FrameworkElement Root;      // the Border (or the Tools StackPanel)
        public StackPanel Panel;           // the section's inner StackPanel: [header, body...]
        public Button Chevron;
        public TextBlock ChevronGlyph;
    }

    private readonly Dictionary<string, SectionUi> _sections = new(StringComparer.OrdinalIgnoreCase);
    private bool _sectionsInitialized;

    private void InitSections()
    {
        if (_sectionsInitialized) return;
        _sectionsInitialized = true;
        void Add(string id, FrameworkElement root)
        {
            var section = HomeSectionCatalog.Find(id);
            var panel = root is Border b ? b.Child as StackPanel : root as StackPanel;
            if (section == null || panel == null || panel.Children.Count == 0) return;
            var ui = new SectionUi { Section = section, Root = root, Panel = panel };
            // Wrap the header (first child) in a grid with the chevron at its right edge.
            var header = panel.Children[0];
            panel.Children.RemoveAt(0);
            var wrap = new Grid { ColumnSpacing = 4 };
            wrap.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            wrap.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            wrap.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn((FrameworkElement)header, 0);
            wrap.Children.Add(header);
            Button Quiet(UIElement content) => new Button
            {
                Content = content, Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 0, 6, 0), MinHeight = 0, Height = 24, VerticalAlignment = VerticalAlignment.Top,
            };
            ui.ChevronGlyph = new TextBlock { Text = "▾", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            ui.Chevron = Quiet(ui.ChevronGlyph);
            ui.Chevron.Click += (_, _) => HomeSectionCatalog.SetCollapsed(id, !HomeSectionCatalog.IsCollapsed(id));
            Grid.SetColumn(ui.Chevron, 1);
            wrap.Children.Add(ui.Chevron);
            // Hide outright, from the section itself; "Customize Home…" is the way back (its Show toggle).
            var hide = Quiet(new TextBlock { Text = "✕", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(hide, "Hide " + section.Title);
            ToolTipService.SetToolTip(hide, "Hide this section (Customize Home… brings it back)");
            hide.Click += (_, _) => HomeSectionCatalog.SetHidden(id, true);
            Grid.SetColumn(hide, 2);
            wrap.Children.Add(hide);
            panel.Children.Insert(0, wrap);
            _sections[id] = ui;
        }
        Add("recent", RecentSection);
        Add("known", KnownSection);
        Add("workspace", WorkspaceSection);
        Add("workspaces", WorkspacesSection);
        Add("tools", ToolsSection);
        CustomizeHomeButton.Click += (_, _) => ShowCustomizeHomeMenu();
    }

    private void OnHomeLayoutChanged() => DispatcherQueue.TryEnqueue(ApplyHomeLayout);

    /// <summary>Put the sections in their stored order, hide the hidden ones, fold the collapsed ones.</summary>
    private void ApplyHomeLayout()
    {
        if (!_sectionsInitialized) return;
        foreach (var ui in _sections.Values)
        {
            bool hidden = HomeSectionCatalog.IsHidden(ui.Section.Id);
            bool collapsed = HomeSectionCatalog.IsCollapsed(ui.Section.Id);
            ui.Root.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
            for (int i = 1; i < ui.Panel.Children.Count; i++)
                ui.Panel.Children[i].Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            ui.ChevronGlyph.Text = collapsed ? "▸" : "▾";
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ui.Chevron, (collapsed ? "Expand " : "Collapse ") + ui.Section.Title);
            ToolTipService.SetToolTip(ui.Chevron, collapsed ? "Expand" : "Collapse to the header");
        }
        Reorder(LeftColumn, HomeColumn.Left, new FrameworkElement[] { OpenHeading, OpenCard });
        Reorder(RightColumn, HomeColumn.Right, Array.Empty<FrameworkElement>());
    }

    private void Reorder(StackPanel column, HomeColumn which, FrameworkElement[] fixedFirst)
    {
        var wanted = new List<UIElement>(fixedFirst);
        foreach (var s in HomeSectionCatalog.Order(which))
            if (_sections.TryGetValue(s.Id, out var ui)) wanted.Add(ui.Root);
        // Only touch the panel when the order actually differs (re-parenting resets focus and layout).
        bool same = column.Children.Count == wanted.Count;
        for (int i = 0; same && i < wanted.Count; i++) same = ReferenceEquals(column.Children[i], wanted[i]);
        if (same) return;
        column.Children.Clear();
        foreach (var el in wanted) column.Children.Add(el);
    }

    private void ShowCustomizeHomeMenu()
    {
        var menu = new MenuFlyout();
        foreach (var column in new[] { HomeColumn.Left, HomeColumn.Right, HomeColumn.Bottom })
        {
            var inColumn = HomeSectionCatalog.Order(column).Where(s => !s.Fixed).ToList();
            for (int i = 0; i < inColumn.Count; i++)
            {
                var s = inColumn[i];
                var sub = new MenuFlyoutSubItem { Text = s.Title };
                var show = new ToggleMenuFlyoutItem { Text = "Show", IsChecked = !HomeSectionCatalog.IsHidden(s.Id) };
                show.Click += (_, _) => HomeSectionCatalog.SetHidden(s.Id, !show.IsChecked);
                sub.Items.Add(show);
                var fold = new ToggleMenuFlyoutItem { Text = "Collapsed", IsChecked = HomeSectionCatalog.IsCollapsed(s.Id) };
                fold.Click += (_, _) => HomeSectionCatalog.SetCollapsed(s.Id, fold.IsChecked);
                sub.Items.Add(fold);
                if (inColumn.Count > 1)
                {
                    sub.Items.Add(new MenuFlyoutSeparator());
                    var up = new MenuFlyoutItem { Text = "Move up", IsEnabled = i > 0 };
                    up.Click += (_, _) => HomeSectionCatalog.Move(s.Id, -1);
                    sub.Items.Add(up);
                    var down = new MenuFlyoutItem { Text = "Move down", IsEnabled = i < inColumn.Count - 1 };
                    down.Click += (_, _) => HomeSectionCatalog.Move(s.Id, +1);
                    sub.Items.Add(down);
                }
                menu.Items.Add(sub);
            }
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        var reset = new MenuFlyoutItem { Text = "Reset layout" };
        reset.Click += (_, _) => HomeSectionCatalog.Reset();
        menu.Items.Add(reset);
        menu.ShowAt(CustomizeHomeButton);
    }

    private void OnWorkspaceStateChanged() => DispatcherQueue?.TryEnqueue(() => { if (IsLoaded) RenderWorkspace(); });

    private MainWindow Main => WindowUtil.GetMainWindow() as MainWindow;

    // ===================== Open a log =====================

    private void OpenFile_Click(object sender, RoutedEventArgs e) => Main?.QuickFileOpen();
    private void OpenFolder_Click(object sender, RoutedEventArgs e) => Main?.QuickFolderOpen();
    private void OpenWithRules_Click(object sender, RoutedEventArgs e) => Main?.OpenWithRules();

    // The window's Content_Drop handles drops anywhere; the drop zone just shows the Copy cursor and forwards
    // the same paths through the same open path.
    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        try
        {
            e.AcceptedOperation = e.DataView.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)
                ? global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy
                : global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
            e.DragUIOverride.Caption = "Open";
        }
        catch { }
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)) return;
        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items.Where(i => !string.IsNullOrWhiteSpace(i.Path)).Select(i => i.Path).ToList();
            e.Handled = true; // don't let the window handler open them a second time
            if (paths.Count > 0 && Main is { } main) await main.OpenPathsAsync(paths);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Home drop failed: {ex.Message}"); }
        finally { deferral.Complete(); }
    }

    // ===================== Recent searches =====================

    private void SeeAllRecent_Click(object sender, RoutedEventArgs e) => Frame?.Navigate(typeof(CachedSearchesPage));

    private async void LoadRecentAsync()
    {
        int gen = ++_recentLoadGeneration;
        List<CachedSearchEntry> entries;
        try
        {
            // Opening SQLite files can take a moment on a large cache dir — off the UI thread.
            entries = await System.Threading.Tasks.Task.Run(() => CachedSearchCatalog.List(25));
        }
        catch { entries = new List<CachedSearchEntry>(); }
        if (gen != _recentLoadGeneration || !IsLoaded) return;

        RecentHost.Children.Clear();
        // Only sources that still exist: a cache whose log was deleted (temp files, cleaned-up folders) has
        // nothing to reopen or add, so it would only show as a dead row here. The full Recent searches page
        // still lists it, marked "source missing", so it can be deleted.
        var named = entries.Where(x => !string.IsNullOrEmpty(x.SourcePath) && x.SourceExists) // SourceExists covers files AND folders
                           .Take(RecentCount).ToList();
        RecentEmpty.Visibility = named.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var now = DateTime.Now;
        for (int i = 0; i < named.Count; i++)
            RecentHost.Children.Add(BuildRecentRow(named[i], now, i < named.Count - 1));
    }

    private FrameworkElement BuildRecentRow(CachedSearchEntry entry, DateTime now, bool divider)
    {
        var grid = new Grid { ColumnSpacing = 12, MinHeight = 44, Padding = new Thickness(8, 2, 8, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 96 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 110 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (divider) grid.BorderThickness = new Thickness(0, 0, 0, 1);
        grid.BorderBrush = Brush("DividerStrokeColorDefaultBrush");

        string name = SafeFileName(entry.SourcePath);
        string dir = SafeDirectory(entry.SourcePath);
        bool isFolder = false;
        try { isFolder = Directory.Exists(entry.SourcePath); } catch { }
        if (isFolder) name += " (folder)";
        var sub = new List<string> { dir };
        if (!entry.SourceExists) sub.Add("source missing");
        if (entry.FtsBuilt) sub.Add("indexed");

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1, MinWidth = 0 };
        text.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(Secondary(string.Join(" · ", sub.Where(s => !string.IsNullOrEmpty(s))), trim: true));
        ToolTipService.SetToolTip(text, entry.SourcePath);
        Grid.SetColumn(text, 0); grid.Children.Add(text);

        var when = Secondary(entry.CompletedAt.HasValue ? HomeFormatting.RelativeTime(entry.CompletedAt.Value.ToLocalTime(), now) : "—");
        Grid.SetColumn(when, 1); grid.Children.Add(when);

        var size = Secondary($"{HomeFormatting.CompactCount(entry.Rows)} rows · {ByteUtils.BytesToFriendlyString(entry.SizeOnDiskBytes)}");
        Grid.SetColumn(size, 2); grid.Children.Add(size);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var open = SmallButton("Open", "Open these results without rescanning");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(open, $"Open {name}");
        open.Click += async (_, _) =>
        {
            if (Main is not { } main) return;
            var sources = entry.SourceExists ? new[] { entry.SourcePath } : Array.Empty<string>();
            try { await main.OpenIntoWorkspaceAsync(sources, cachedDbPath: entry.DbPath, displayName: name); }
            catch (Exception ex) { Logger.Instance.Log($"Home: open recent failed: {ex.Message}"); }
        };
        buttons.Children.Add(open);
        var add = SmallButton("Add to workspace", "Add this search's source to the workspace without running it");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(add, $"Add {name} to workspace");
        add.IsEnabled = entry.SourceExists;
        add.Click += (_, _) =>
        {
            try { MiddleLayerService.OpenIntoWorkspace(OpenIntoWorkspaceMode.Add, new[] { entry.SourcePath }); }
            catch (Exception ex) { Logger.Instance.Log($"Home: add recent failed: {ex.Message}"); }
        };
        buttons.Children.Add(add);
        Grid.SetColumn(buttons, 3); grid.Children.Add(buttons);
        return grid;
    }

    // ===================== Known logs =====================

    private void SeeAllKnown_Click(object sender, RoutedEventArgs e) => Frame?.Navigate(typeof(LogFinderPage));

    private void RenderKnownLogs()
    {
        KnownLogsHost.Children.Clear();
        List<LogCatalogEntry> all;
        try { all = LogCatalog.GetAll(includeHidden: false); }
        catch { all = new List<LogCatalogEntry>(); }
        var picked = new List<LogCatalogEntry>();
        foreach (var id in PreferredKnownLogIds)
        {
            var e = all.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (e != null && picked.Count < KnownLogsCount) picked.Add(e);
        }
        foreach (var e in all)
            if (picked.Count < KnownLogsCount && !picked.Contains(e)) picked.Add(e);

        for (int i = 0; i < picked.Count; i++)
            KnownLogsHost.Children.Add(BuildKnownLogRow(picked[i], i < picked.Count - 1));
    }

    private FrameworkElement BuildKnownLogRow(LogCatalogEntry entry, bool divider)
    {
        var grid = new Grid { ColumnSpacing = 12, MinHeight = 44, Padding = new Thickness(8, 2, 8, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (divider) grid.BorderThickness = new Thickness(0, 0, 0, 1);
        grid.BorderBrush = Brush("DividerStrokeColorDefaultBrush");

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1, MinWidth = 0 };
        text.Children.Add(new TextBlock { Text = entry.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var sub = string.IsNullOrWhiteSpace(entry.Description) ? entry.ExpandedPath : $"{entry.Description} · {entry.ExpandedPath}";
        if (!entry.Exists) sub += " · not found on this machine";
        text.Children.Add(Secondary(sub, trim: true));
        Grid.SetColumn(text, 0); grid.Children.Add(text);

        // "Open…" opens these logs straight into the workspace (Add / Replace / Ask), like the Known logs page
        // does. Bouncing through that page first made it Known logs -> Known logs -> Open.
        var open = SmallButton("Open…", "Open these logs");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(open, $"Open {entry.Name}");
        open.Click += (_, _) => OpenKnownLog(entry);
        Grid.SetColumn(open, 1); grid.Children.Add(open);
        return grid;
    }

    private async void OpenKnownLog(LogCatalogEntry entry)
    {
        var path = entry.ExpandedPath;
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (WindowUtil.GetMainWindow() is MainWindow main)
                await main.OpenIntoWorkspaceAsync(new[] { path }, label: $"Opening {entry.Name}…", displayName: entry.Name); // name it, not its folder
        }
        catch (Exception ex) { FindNeedlePluginLib.Logger.Instance.Log($"Open known log failed: {ex.Message}"); }
    }

    // ===================== Current workspace =====================

    private void RenderWorkspace()
    {
        try
        {
            WorkspaceNameText.Text = MiddleLayerService.WorkspaceDisplayName;
            bool empty = MiddleLayerService.IsWorkspaceEmpty;
            WorkspaceEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            WorkspaceBody.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            if (empty) return;

            // Sources
            var locations = MiddleLayerService.Locations?.ToList() ?? new List<ISearchLocation>();
            SourcesCount.Text = locations.Count.ToString();
            SourcesHost.Children.Clear();
            foreach (var loc in locations.Take(MaxSourceRows))
            {
                string path = loc is FolderLocation f ? f.path : SafeName(loc);
                string tag = HomeFormatting.SourceTagFor(loc?.GetType().Name, path);
                string extra = null;
                try
                {
                    if (loc is FolderLocation ff && Directory.Exists(ff.path))
                    {
                        int n = Directory.EnumerateFiles(ff.path).Take(1000).Count();
                        extra = n >= 1000 ? "1000+ files" : $"{n} file{(n == 1 ? "" : "s")}";
                    }
                }
                catch { }
                SourcesHost.Children.Add(BuildTagRow(tag, path, extra));
            }
            if (locations.Count > MaxSourceRows)
                SourcesHost.Children.Add(Secondary($"+ {locations.Count - MaxSourceRows} more"));

            // Rule files
            var rules = MiddleLayerService.UserRulePaths;
            int auto = MiddleLayerService.LastAutoAddedRules?.Count ?? 0;
            RulesCount.Text = rules.Count.ToString();
            RulesHost.Children.Clear();
            foreach (var r in rules.Take(MaxSourceRows))
                RulesHost.Children.Add(BuildTagRow("Rules", SafeFileName(r), null, tooltip: r));
            if (rules.Count > MaxSourceRows)
                RulesHost.Children.Add(Secondary($"+ {rules.Count - MaxSourceRows} more"));
            if (auto > 0)
                RulesHost.Children.Add(Secondary($"+ {auto} auto rule{(auto == 1 ? "" : "s")}"));
            RulesEmpty.Visibility = rules.Count == 0 && auto == 0 ? Visibility.Visible : Visibility.Collapsed;

            // Actions + last run
            RunSearchButton.IsEnabled = locations.Count > 0;
            bool hasResults = MiddleLayerService.LastRunSummary != null || MiddleLayerService.OpenCacheDbPath != null;
            OpenResultsButton.IsEnabled = hasResults;
            if (MiddleLayerService.LastRunSummary is { } summary)
            {
                var when = MiddleLayerService.LastRunCompletedAt;
                LastRunText.Text = when.HasValue
                    ? $"Last run {HomeFormatting.RelativeTime(when.Value, DateTime.Now)} · {summary}"
                    : $"Last run · {summary}";
            }
            else LastRunText.Text = MiddleLayerService.OpenCacheDbPath != null ? "Showing a recent search" : "Not run yet";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RenderWorkspace failed: {ex}");
        }
    }

    private FrameworkElement BuildTagRow(string tag, string text, string extra, string tooltip = null)
    {
        var row = new Grid
        {
            ColumnSpacing = 10,
            MinHeight = 32,
            Padding = new Thickness(10, 0, 10, 0),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush("CardStrokeColorDefaultBrush"),
            Background = Brush("CardBackgroundFillColorSecondaryBrush"),
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var pill = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 1, 8, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brush("SubtleFillColorSecondaryBrush"),
            Child = new TextBlock { Text = tag, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brush("TextFillColorSecondaryBrush") },
        };
        Grid.SetColumn(pill, 0); row.Children.Add(pill);

        var tb = new TextBlock
        {
            Text = text, FontFamily = new FontFamily("Consolas"), FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        ToolTipService.SetToolTip(tb, tooltip ?? text);
        Grid.SetColumn(tb, 1); row.Children.Add(tb);

        if (!string.IsNullOrEmpty(extra))
        {
            var ex = Secondary(extra); ex.FontSize = 11;
            Grid.SetColumn(ex, 2); row.Children.Add(ex);
        }
        return row;
    }

    private void AddSource_Click(object sender, RoutedEventArgs e) => Frame?.Navigate(typeof(SearchLocationsPage));
    private void EditRules_Click(object sender, RoutedEventArgs e) => Frame?.Navigate(typeof(RulesPage), "files");
    private async void RunSearch_Click(object sender, RoutedEventArgs e) { if (Main is { } m) await m.ExecuteMenuActionAsync("results_get"); }
    private void OpenResults_Click(object sender, RoutedEventArgs e) => Main?.RunQuickAction("results");

    // ===================== Workspaces =====================

    private async void NewWorkspace_Click(object sender, RoutedEventArgs e) { if (Main is { } m) await m.ExecuteMenuActionAsync("newworkspace"); }
    private async void OpenWorkspace_Click(object sender, RoutedEventArgs e) { if (Main is { } m) await m.ExecuteMenuActionAsync("openworkspace"); }
    private async void SaveWorkspace_Click(object sender, RoutedEventArgs e) { if (Main is { } m) await m.ExecuteMenuActionAsync("saveworkspace"); }

    // ===================== Tools (customizable tiles) =====================

    private void Customize_Click(object sender, RoutedEventArgs e)
    {
        _editMode = !_editMode;
        RenderQuickActions();
    }

    private void RenderQuickActions()
    {
        if (QuickActionsHost == null) return;
        try
        {
            CustomizeLabel.Text = _editMode ? "Done" : "Customize…";
            CustomizeIcon.Symbol = _editMode ? Symbol.Accept : Symbol.Edit;
            QuickActionsHost.Children.Clear();

            var ids = QuickActionCatalog.GetSelectedIds();
            for (int i = 0; i < ids.Count; i++)
            {
                var action = QuickActionCatalog.Find(ids[i]);
                if (action == null) continue;
                QuickActionsHost.Children.Add(_editMode
                    ? BuildEditTile(action, i, ids.Count)
                    : BuildActionTile(action));
            }

            if (_editMode) QuickActionsHost.Children.Add(BuildAddTile(ids));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RenderQuickActions failed: {ex}");
        }
    }

    /// <summary>Normal tile: a compact emoji + label button; click runs the action.</summary>
    private Button BuildActionTile(QuickAction action)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new TextBlock { Text = action.Emoji, FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = action.Label, VerticalAlignment = VerticalAlignment.Center });

        var btn = new Button { Content = content, Height = 36, MinWidth = 120, Padding = new Thickness(12, 0, 14, 0) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(btn, action.Label);
        btn.Click += (_, _) => Main?.RunQuickAction(action.Id);
        return btn;
    }

    /// <summary>Edit tile: label plus reorder (← →) and remove (×) controls.</summary>
    private FrameworkElement BuildEditTile(QuickAction action, int index, int count)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock { Text = action.Emoji, FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(new TextBlock { Text = action.Label, VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(MiniButton(Symbol.Back, "Move left", () => Move(action.Id, -1), enabled: index > 0));
        stack.Children.Add(MiniButton(Symbol.Forward, "Move right", () => Move(action.Id, +1), enabled: index < count - 1));
        stack.Children.Add(MiniButton(Symbol.Cancel, "Remove", () => Remove(action.Id)));

        return new Border
        {
            Height = 36,
            MinWidth = 120,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(AccentTint(0.5)),
            Background = new SolidColorBrush(AccentTint(0.06)),
            Padding = new Thickness(12, 0, 6, 0),
            Child = stack,
        };
    }

    /// <summary>The "+ Add" tile in edit mode: a flyout of catalog actions not already shown.</summary>
    private FrameworkElement BuildAddTile(List<string> selected)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new SymbolIcon { Symbol = Symbol.Add });
        content.Children.Add(new TextBlock { Text = "Add", VerticalAlignment = VerticalAlignment.Center });

        var btn = new Button { Content = content, Height = 36, MinWidth = 90 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(btn, "Add tool");

        var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom };
        var available = QuickActionCatalog.Available();
        if (available.Count == 0)
            menu.Items.Add(new MenuFlyoutItem { Text = "All actions added", IsEnabled = false });
        else
            foreach (var a in available)
            {
                var item = new MenuFlyoutItem { Text = $"{a.Emoji}  {a.Label}" };
                item.Click += (_, _) => Add(a.Id);
                menu.Items.Add(item);
            }
        btn.Flyout = menu;
        return btn;
    }

    private static Button MiniButton(Symbol symbol, string tip, Action onClick, bool enabled = true)
    {
        var b = new Button
        {
            Content = new SymbolIcon { Symbol = symbol, RenderTransform = new ScaleTransform { ScaleX = 0.65, ScaleY = 0.65 }, RenderTransformOrigin = new global::Windows.Foundation.Point(0.5, 0.5) },
            Padding = new Thickness(2),
            MinWidth = 0, MinHeight = 0,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            IsEnabled = enabled,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(b, tip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, tip);
        b.Click += (_, _) => onClick();
        return b;
    }

    private static Color AccentTint(double opacity)
    {
        Color a;
        try { a = new global::Windows.UI.ViewManagement.UISettings().GetColorValue(global::Windows.UI.ViewManagement.UIColorType.Accent); }
        catch { a = Color.FromArgb(255, 0, 120, 215); } // Windows default accent
        return Color.FromArgb((byte)(opacity * 255), a.R, a.G, a.B);
    }

    // --- mutations (persist via the catalog, then re-render) ---
    private void Move(string id, int delta) { QuickActionCatalog.Move(id, delta); RenderQuickActions(); }
    private void Remove(string id) { QuickActionCatalog.Remove(id); RenderQuickActions(); }
    private void Add(string id) { QuickActionCatalog.Add(id); RenderQuickActions(); }

    // ===================== helpers =====================

    private static Brush Brush(string key)
    {
        try { return (Brush)Application.Current.Resources[key]; }
        catch { return new SolidColorBrush(Colors.Transparent); }
    }

    private static TextBlock Secondary(string text, bool trim = false) => new()
    {
        Text = text ?? "",
        FontSize = 12,
        Foreground = Brush("TextFillColorSecondaryBrush"),
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = trim ? TextTrimming.CharacterEllipsis : TextTrimming.None,
        TextWrapping = trim ? TextWrapping.NoWrap : TextWrapping.Wrap,
    };

    private static Button SmallButton(string text, string tooltip)
    {
        var b = new Button { Content = text, Height = 28, MinHeight = 28, Padding = new Thickness(10, 0, 10, 0), FontSize = 12 };
        ToolTipService.SetToolTip(b, tooltip);
        return b;
    }

    private static string SafeFileName(string path)
    {
        try { return Path.GetFileName(path.TrimEnd('\\', '/')); } catch { return path; }
    }

    private static string SafeDirectory(string path)
    {
        try { return Path.GetDirectoryName(path.TrimEnd('\\', '/')) ?? ""; } catch { return ""; }
    }

    private static string SafeName(ISearchLocation loc)
    {
        try { return loc?.GetName() ?? ""; } catch { return ""; }
    }
}
