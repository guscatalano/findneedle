using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Text;
using FindNeedleUX.Services;
using FindPluginCore.GlobalConfiguration;
using FindNeedleCoreUtils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Windows.UI;
using Windows.Storage.Pickers;
using FindNeedlePluginLib;

namespace FindNeedleUX;

public sealed partial class MainWindow : Window
{
    private CancellationTokenSource _quickActionCts;
    private string _lastRunSummary = "—";

    public MainWindow()
    {
        this.InitializeComponent();
        WindowUtil.TrackWindow(this);
        SetWindowIcon("Assets\\appicon.ico");
        contentFrame.Navigated += (s, e) => { RefreshStatusStrip(); BuildQuickMenu(); };
        MiddleLayerService.StateChanged += () => DispatcherQueue.TryEnqueue(RefreshStatusStrip);
        // Unified "Step X of N · phase · detail" status: whenever the spinner is up (search or viewer
        // open), show the current flow phase. Detail (row counts etc.) flows in via FlowProgress.Detail.
        FindNeedlePluginLib.FlowProgress.Updated += OnFlowProgress;
        StepList.ItemsSource = _stepRows;
        // Show WelcomePage on startup
        contentFrame.Navigate(typeof(FindNeedleUX.Pages.WelcomePage));
        RefreshStatusStrip();
        BuildQuickMenu();
        // Keep the top "Quick" menu in sync when quick actions are edited on the welcome page.
        FindNeedleUX.Services.QuickActionCatalog.Changed += () => DispatcherQueue.TryEnqueue(BuildQuickMenu);
        ApplyPersistedStatusStripVisibility();
        ApplyTitleBarColor();
        // Re-apply status-bar visibility + title-bar color when changed in Preferences.
        ResultsViewerSettings.Changed += () => DispatcherQueue.TryEnqueue(() =>
        {
            ApplyPersistedStatusStripVisibility();
            ApplyTitleBarColor();
        });
        InitMcpIndicator();

        // Pre-warm the heavy viewer pages on a low-priority dispatcher tick. Constructing the
        // page without attaching it to the visual tree primes the XAML parser cache, runs
        // every type's static .cctor, and JITs the hot constructors — about half the cost of
        // the first user-initiated switch. The welcome page is already painted, so this happens
        // while the user is reading it. DataGrid first-layout still happens on the real switch
        // (it requires visual-tree attachment), but the second half of the freeze is gone.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, WarmUpViewers);
    }

    private void WarmUpViewers()
    {
        try
        {
            _ = new FindNeedleUX.Pages.NativeResultsPage();
            Logger.Instance.Log("Viewer pre-warm complete");
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"Viewer pre-warm failed: {ex.Message}");
        }
    }

    public void NavigateToQuickLogWithRules()
    {
        contentFrame.Navigate(typeof(FindNeedleUX.Pages.QuickLogWithRulesPage));
    }

    /// <summary>Populate the top "Quick" menu so it mirrors the welcome page: a "Welcome" entry to go
    /// home, then the user's customized quick actions, then a "Customize…" shortcut. Rebuilt on each
    /// navigation so it reflects edits made on the welcome page.</summary>
    private string _quickMenuSig;

    private void BuildQuickMenu()
    {
        if (QuickMenu == null) return;

        // Only rebuild when the selection actually changed — Navigated fires constantly and rebuilding
        // every time piled up duplicate items. Clear reliably via RemoveAt (MenuBarItem.Items.Clear is
        // flaky in WinUI).
        var ids = FindNeedleUX.Services.QuickActionCatalog.GetSelectedIds();
        var sig = string.Join(",", ids);
        if (sig == _quickMenuSig && QuickMenu.Items.Count > 0) return;
        _quickMenuSig = sig;
        while (QuickMenu.Items.Count > 0) QuickMenu.Items.RemoveAt(QuickMenu.Items.Count - 1);

        var home = new MenuFlyoutItem { Text = "🏠 Welcome" };
        home.Click += (_, _) => contentFrame.Navigate(typeof(FindNeedleUX.Pages.WelcomePage));
        QuickMenu.Items.Add(home);
        QuickMenu.Items.Add(new MenuFlyoutSeparator());

        foreach (var qaId in ids)
        {
            var a = FindNeedleUX.Services.QuickActionCatalog.Find(qaId);
            if (a == null) continue;
            var mi = new MenuFlyoutItem { Text = $"{a.Emoji}  {a.Label}" };
            mi.Click += (_, _) => RunQuickAction(a.Id);
            QuickMenu.Items.Add(mi);
        }

        QuickMenu.Items.Add(new MenuFlyoutSeparator());
        var customize = new MenuFlyoutItem { Text = "Customize quick actions…" };
        customize.Click += (_, _) => contentFrame.Navigate(typeof(FindNeedleUX.Pages.WelcomePage));
        QuickMenu.Items.Add(customize);
    }

    /// <summary>Run a welcome-page quick action by its catalog id (see QuickActionCatalog). Maps to the
    /// same navigation/commands the menus use, so the welcome page can be customized freely.</summary>
    public async void RunQuickAction(string id)
    {
        switch (id)
        {
            case "open_file":         QuickFileOpen(); break;
            case "open_folder":       QuickFolderOpen(); break;
            case "open_rules":        contentFrame.Navigate(typeof(FindNeedleUX.Pages.QuickLogWithRulesPage)); break;
            case "log_finder":        contentFrame.Navigate(typeof(FindNeedleUX.Pages.LogFinderPage)); break;
            case "open_ado":          contentFrame.Navigate(typeof(FindNeedleUX.Pages.SearchLocationsPage), "ado"); break;
            case "open_github":       contentFrame.Navigate(typeof(FindNeedleUX.Pages.SearchLocationsPage), "github"); break;
            case "open_kusto":        contentFrame.Navigate(typeof(FindNeedleUX.Pages.SearchLocationsPage), "kusto"); break;
            case "cached":            contentFrame.Navigate(typeof(FindNeedleUX.Pages.CachedSearchesPage)); break;
            case "locations":         contentFrame.Navigate(typeof(FindNeedleUX.Pages.SearchLocationsPage)); break;
            case "rules_config":      contentFrame.Navigate(typeof(FindNeedleUX.Pages.RulesPage), "files"); break;
            case "auto_rules":        contentFrame.Navigate(typeof(FindNeedleUX.Pages.RulesPage), "autoadd"); break;
            case "run_search":        contentFrame.Navigate(typeof(FindNeedleUX.Pages.RunSearchPage)); break;
            case "results":           NavigateWithSpinner(typeof(FindNeedleUX.Pages.NativeResultsPage)); break;
            case "processor_output":  contentFrame.Navigate(typeof(FindNeedleUX.Pages.ProcessorOutputPage)); break;
            case "diagram":           contentFrame.Navigate(typeof(FindNeedleUX.Pages.DiagramToolsPage)); break;
            case "inspect_etl":       await InspectionService.InspectEtlAsync(this, (show, text) => ShowSpinner(show, text)); break;
            default: Logger.Instance.Log($"RunQuickAction: unknown id {id}"); break;
        }
    }


    // ----- Top-right MCP server indicator -----

    private void InitMcpIndicator()
    {
        McpToggle.IsChecked = ResultsViewerSettings.McpServerEnabled;
        RefreshMcpDot();
        FindNeedleUX.Services.Mcp.McpServerHost.StatusChanged += () => DispatcherQueue.TryEnqueue(RefreshMcpDot);
        // Refresh the status strip's MCP chip when a client connects/sends a request, and on server on/off.
        FindNeedleUX.Services.Mcp.McpServerHost.StatusChanged += () => DispatcherQueue.TryEnqueue(RefreshStatusStrip);
        FindNeedleUX.Services.Mcp.McpServer.Activity += () => DispatcherQueue.TryEnqueue(RefreshStatusStrip);
    }

    private void RefreshMcpDot()
    {
        bool running = FindNeedleUX.Services.Mcp.McpViewerBridge.Instance.ServerPort > 0;
        var color = running
            ? global::Windows.UI.Color.FromArgb(0xFF, 0x2E, 0xA0, 0x43)   // green — listening
            : global::Windows.UI.Color.FromArgb(0xFF, 0x9E, 0x9E, 0x9E);  // gray — off
        McpStatusDot.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(color);
        ToolTipService.SetToolTip(McpToggle, "In-app MCP server — " + FindNeedleUX.Services.Mcp.McpServerHost.Status);
        if (McpToggle.IsChecked != ResultsViewerSettings.McpServerEnabled)
            McpToggle.IsChecked = ResultsViewerSettings.McpServerEnabled; // keep in sync if changed elsewhere
    }

    private void McpToggle_Click(object sender, RoutedEventArgs e)
    {
        // Setter broadcasts Changed → McpServerHost starts/stops the server; RefreshMcpDot follows.
        ResultsViewerSettings.McpServerEnabled = McpToggle.IsChecked == true;
        RefreshMcpDot();
    }

    private void McpHelpButton_Click(object sender, RoutedEventArgs e)
    {
        bool running = FindNeedleUX.Services.Mcp.McpViewerBridge.Instance.ServerPort > 0;
        int port = running ? FindNeedleUX.Services.Mcp.McpViewerBridge.Instance.ServerPort
                           : ResultsViewerSettings.McpServerPort;
        var url = $"http://127.0.0.1:{port}/";
        var config =
            "{\n" +
            "  \"mcpServers\": {\n" +
            "    \"findneedle\": {\n" +
            $"      \"url\": \"{url}\"\n" +
            "    }\n" +
            "  }\n" +
            "}";

        var panel = new StackPanel { Spacing = 8, MinWidth = 380, MaxWidth = 460 };
        panel.Children.Add(new TextBlock { Text = "In-app MCP server", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 15 });
        panel.Children.Add(new TextBlock
        {
            Text = running ? $"Running — listening on {url}" : "Stopped — enable the MCP toggle to start it.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            FontSize = 12,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Lets an AI agent read and drive the live result viewer (search, filter, page, tag, export). " +
                   "Transport: Streamable HTTP (JSON-RPC), localhost only, no auth.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });

        panel.Children.Add(new TextBlock { Text = "MCP client config", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) });
        panel.Children.Add(new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"],
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(8),
            Child = new TextBlock { Text = config, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 12, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap },
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var copyEndpoint = new Button { Content = "Copy endpoint" };
        copyEndpoint.Click += (_, __) => SetClipboard(url);
        var copyConfig = new Button { Content = "Copy config" };
        copyConfig.Click += (_, __) => SetClipboard(config);
        buttons.Children.Add(copyEndpoint);
        buttons.Children.Add(copyConfig);
        panel.Children.Add(buttons);

        AppendMcpActivity(panel);

        var flyout = new Flyout { Content = panel };
        var settingsLink = new HyperlinkButton { Content = "More settings…", Padding = new Thickness(0, 4, 0, 0) };
        settingsLink.Click += (_, __) => { flyout.Hide(); contentFrame.Navigate(typeof(FindNeedleUX.Pages.ResultsViewerSettingsPage)); };
        panel.Children.Add(settingsLink);

        flyout.ShowAt(McpHelpButton);
    }

    /// <summary>Append "who's connected + last commands" to the MCP flyout, from the in-memory activity
    /// log. Answers the at-a-glance "is an agent talking to me and what is it doing?"</summary>
    private void AppendMcpActivity(StackPanel panel)
    {
        var dim = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        var clients = FindNeedleUX.Services.Mcp.McpActivityLog.KnownClients();
        var active = FindNeedleUX.Services.Mcp.McpActivityLog.ActiveClients();
        var recent = FindNeedleUX.Services.Mcp.McpActivityLog.RecentCommands(10);

        panel.Children.Add(new TextBlock
        {
            Text = "Connected clients", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12,
            Margin = new Thickness(0, 8, 0, 0),
        });
        if (clients.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = "No client has connected yet.", FontSize = 12, Foreground = dim, TextWrapping = TextWrapping.Wrap });
        }
        else
        {
            foreach (var c in clients)
            {
                bool isActive = active.Any(a => a.Name == c.Name);
                var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                line.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
                {
                    Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center,
                    Fill = new SolidColorBrush(isActive ? Color.FromArgb(255, 46, 160, 67) : Color.FromArgb(255, 158, 158, 158)),
                });
                line.Children.Add(new TextBlock
                {
                    Text = $"{c.Name}  ·  {c.Commands} cmd{(c.Commands == 1 ? "" : "s")}  ·  last {c.LastSeenUtc.ToLocalTime():HH:mm:ss}",
                    FontSize = 12, TextWrapping = TextWrapping.Wrap,
                });
                panel.Children.Add(line);
            }
        }

        panel.Children.Add(new TextBlock
        {
            Text = "Last 10 commands", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12,
            Margin = new Thickness(0, 8, 0, 0),
        });
        if (recent.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = "No commands yet.", FontSize = 12, Foreground = dim });
        }
        else
        {
            var sb = new System.Text.StringBuilder();
            foreach (var cmd in recent)
            {
                var what = string.IsNullOrEmpty(cmd.Tool) ? cmd.Method : $"{cmd.Method} → {cmd.Tool}";
                sb.AppendLine($"{cmd.TimeUtc.ToLocalTime():HH:mm:ss}  {what}");
            }
            panel.Children.Add(new Border
            {
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"],
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(8),
                Child = new TextBlock { Text = sb.ToString().TrimEnd(), FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 11, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap },
            });
        }
    }

    private static void SetClipboard(string text)
    {
        var pkg = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
        pkg.SetText(text ?? "");
        global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
    }

    /// <summary>
    /// GUI equivalent of the findneedle.exe command line: load a log file/folder passed as a
    /// program argument, run the search, and open the result viewer — without going through the
    /// file picker. Recognized arguments (order-independent):
    ///   &lt;path&gt;            first positional arg that exists on disk → the location to search
    ///   --rules=&lt;file&gt;    optional rules DSL JSON to apply
    ///   --viewer=native|web  which result viewer to open (default: user's configured viewer)
    /// Unknown flags are ignored. If no existing path is supplied this is a no-op (normal launch).
    /// The native-viewer path is what the FlaUI DataGrid scroll test drives.
    /// </summary>
    public async void LoadFromCommandLine(string[] args)
    {
        string path = null;
        string rules = null;
        string viewer = null;
        string storage = null;
        string cache = null;
        string indexing = null;
        int? estimate = null;
        foreach (var raw in args ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var a = raw.Trim().Trim('"');
            if (a.StartsWith("--rules=", StringComparison.OrdinalIgnoreCase))
            {
                rules = a.Substring("--rules=".Length).Trim().Trim('"');
            }
            else if (a.StartsWith("--viewer=", StringComparison.OrdinalIgnoreCase))
            {
                viewer = a.Substring("--viewer=".Length).Trim().Trim('"').ToLowerInvariant();
            }
            else if (a.StartsWith("--storage=", StringComparison.OrdinalIgnoreCase))
            {
                storage = a.Substring("--storage=".Length).Trim().Trim('"').ToLowerInvariant();
            }
            else if (a.StartsWith("--estimate=", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(a.Substring("--estimate=".Length).Trim().Trim('"'), out var est)) estimate = est;
            }
            else if (a.StartsWith("--cache=", StringComparison.OrdinalIgnoreCase))
            {
                cache = a.Substring("--cache=".Length).Trim().Trim('"').ToLowerInvariant();
            }
            else if (a.StartsWith("--indexing=", StringComparison.OrdinalIgnoreCase))
            {
                indexing = a.Substring("--indexing=".Length).Trim().Trim('"').ToLowerInvariant();
            }
            else if (a.StartsWith("--"))
            {
                // unknown flag — ignore
            }
            else if (path == null && (System.IO.File.Exists(a) || System.IO.Directory.Exists(a)))
            {
                path = a;
            }
        }

        if (path == null)
        {
            // Nothing loadable on the command line — leave the app on the welcome page.
            return;
        }

        try
        {
            Logger.Instance.Log($"CLI load: {path}" + (rules != null ? $" (rules: {rules})" : "")
                + (storage != null ? $" (storage: {storage})" : "") + (estimate != null ? $" (estimate: {estimate})" : ""));

            // Per-run storage / Auto-estimate overrides (consumed in MiddleLayerService.UpdateSearchQuery).
            MiddleLayerService.StorageOverride = storage switch
            {
                "sqlite" or "sql" or "sqllite" => FindPluginCore.PluginSubsystem.StorageType.SqlLite,
                "inmemory" or "memory" => FindPluginCore.PluginSubsystem.StorageType.InMemory,
                "hybrid" => FindPluginCore.PluginSubsystem.StorageType.Hybrid,
                "auto" => FindPluginCore.PluginSubsystem.StorageType.Auto,
                _ => null,
            };
            MiddleLayerService.RowEstimateOverride = estimate;
            MiddleLayerService.CacheModeOverride = cache switch
            {
                "on" or "reuse" or "always" => FindPluginCore.Searching.CacheReuseMode.Always,
                "off" or "never" or "disabled" => FindPluginCore.Searching.CacheReuseMode.Never,
                _ => null,
            };
            MiddleLayerService.IndexingModeOverride = indexing switch
            {
                "eager" => FindPluginCore.Searching.IndexingMode.Eager,
                "lazy" => FindPluginCore.Searching.IndexingMode.Lazy,
                "background" or "bg" => FindPluginCore.Searching.IndexingMode.Background,
                _ => null,
            };

            MiddleLayerService.NewWorkspace();
            MiddleLayerService.AddFolderLocation(path);
            if (!string.IsNullOrWhiteSpace(rules) && System.IO.File.Exists(rules))
            {
                var query = MiddleLayerService.GetCurrentQuery();
                if (query != null)
                {
                    query.RulesConfigPaths ??= new List<string>();
                    query.RulesConfigPaths.Add(rules);
                }
            }

            ShowSpinner(true, "Opening file...", showCancel: true);
            await RunSearchWithProgress();
            ShowSpinner(false);

            // Only the native viewer remains; --viewer is accepted for back-compat but always native.
            contentFrame.Navigate(typeof(FindNeedleUX.Pages.NativeResultsPage));
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"CLI load failed: {ex.Message}");
            ShowSpinner(false);
        }
    }

    /// <summary>The result viewer page. Only the native viewer remains.</summary>
    private static Type ResolveDefaultViewerType() => typeof(FindNeedleUX.Pages.NativeResultsPage);

    /// <summary>
    /// Programmatically navigate to the native result viewer. Used by the streaming search flow
    /// to open the viewer mid-load when a search runs past the grace window.
    /// </summary>
    public void NavigateToNativeResultsPage()
    {
        DispatcherQueue.TryEnqueue(() =>
            contentFrame.Navigate(typeof(FindNeedleUX.Pages.NativeResultsPage)));
    }

    public void NavigateToResultsViewerSettings()
    {
        DispatcherQueue.TryEnqueue(() =>
            contentFrame.Navigate(typeof(FindNeedleUX.Pages.ResultsViewerSettingsPage)));
    }


    /// <summary>
    /// Show / hide a non-cancellable spinner used to give immediate feedback when switching to a
    /// page whose first construction is slow (e.g. the native viewer's DataGrid init). The
    /// destination page is expected to call <see cref="HideNavigationSpinner"/> in its
    /// <c>Loaded</c> handler once it's fully painted.
    /// </summary>
    public void ShowNavigationSpinner(string text)
    {
        ShowSpinner(true, text);
    }

    public void HideNavigationSpinner()
    {
        ShowSpinner(false);
    }

    /// <summary>
    /// Show the loading spinner, paint one frame so the user sees it, then perform the
    /// (synchronous, UI-thread-blocking) Frame navigation. The destination page is expected to
    /// call <see cref="HideNavigationSpinner"/> from its Loaded handler — for pages that don't,
    /// we drop a safety-net hide call after the navigation completes.
    ///
    /// While the spinner is visible we also seed its text with the current result-set size so
    /// the user understands the order of magnitude they're waiting for (e.g. "Loading viewer ·
    /// 423,231 rows"). For a running streaming search the count is live; for a completed search
    /// it's the final row count.
    /// </summary>
    private void NavigateWithSpinner(Type pageType)
    {
        ShowSpinner(true, BuildLoadingViewerText());
        // Defer the navigation to the next dispatcher tick so the spinner gets one paint pass
        // before the UI thread is blocked by page construction (DataGrid init in particular).
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            try { contentFrame.Navigate(pageType); }
            finally
            {
                // Safety net: if the destination didn't hide the spinner itself, kill it on the
                // next tick so we never leave it visible permanently.
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                    () => ShowSpinner(false));
            }
        });
    }

    private static string BuildLoadingViewerText()
    {
        // Prefer the running streaming search's storage if there is one (in-flight load), since
        // that count grows as rows arrive. Otherwise fall back to the last completed search's
        // result count. Either way, surface it so the user knows what's coming.
        try
        {
            var streaming = MiddleLayerService.CurrentStreamingSearch;
            if (streaming?.Storage != null)
            {
                var n = streaming.Storage.GetStatistics().filteredRecordCount;
                if (streaming.Source?.IsLoading == true)
                    return $"Loading viewer · streaming {n:N0} rows…";
                return $"Loading viewer · {n:N0} rows";
            }
            var completed = MiddleLayerService.GetFilteredRowCount();
            if (completed > 0) return $"Loading viewer · {completed:N0} rows";
        }
        catch { /* never let UX feedback break the navigation */ }
        return "Loading viewer…";
    }

    private void RefreshStatusStrip()
    {
        if (StatusSegments == null) return; // not yet realized
        StatusSegments.Children.Clear();
        foreach (var id in StatusBarCatalog.GetSelectedIds())
        {
            var item = BuildStatusItem(id);
            if (item != null) StatusSegments.Children.Add(item);
        }

        // Always surface "a UML diagram is available/ready" when relevant, regardless of the user's
        // chosen status-bar items — it's a transient notification, not a configurable segment.
        var uml = BuildUmlChip();
        if (uml != null) StatusSegments.Children.Add(uml);
    }

    /// <summary>Build one status-bar item by id. Returns null when an info item is empty and shouldn't
    /// clutter the bar (filters/output files with a zero count).</summary>
    private FrameworkElement BuildStatusItem(string id)
    {
        var query = MiddleLayerService.GetCurrentQuery();
        switch (id)
        {
            case "locations":
            {
                var locs = MiddleLayerService.Locations ?? new List<ISearchLocation>();
                var tip = locs.Count > 0
                    ? string.Join("\n", locs.Select(l => { try { return l.GetName(); } catch { return "(location)"; } }))
                    : "No locations added";
                return MakeStatusSegment(Symbol.Folder, "Locations", locs.Count.ToString(), tip, null,
                    () => contentFrame.Navigate(typeof(FindNeedleUX.Pages.SearchLocationsPage)));
            }
            case "filters":
            {
                var filters = MiddleLayerService.Filters?.Count ?? 0;
                if (filters == 0) return null;
                return MakeStatusSegment(Symbol.Find, "Filters", filters.ToString(), "Active filters", null,
                    () => contentFrame.Navigate(typeof(FindNeedleUX.Pages.RulesPage), "files"));
            }
            case "rules":
            {
                var rulePaths = query?.RulesConfigPaths ?? new List<string>();
                var tip = rulePaths.Count > 0
                    ? string.Join("\n", rulePaths.Select(p => { try { return System.IO.Path.GetFileName(p); } catch { return p; } }))
                    : "No rules configured";
                return MakeStatusSegment(Symbol.List, "Rules", rulePaths.Count.ToString(), tip, null,
                    () => contentFrame.Navigate(typeof(FindNeedleUX.Pages.RulesPage), "files"));
            }
            case "lastrun":
            {
                string lastRun; bool hasResults = false; int liveCount = -1;
                try { if (MiddleLayerService.GetSearchStorage() != null) liveCount = MiddleLayerService.GetFilteredRowCount(); } catch { }
                if (liveCount >= 0)
                {
                    var suffix = MiddleLayerService.LastSearchReusedCache ? " (cached)" : " (scanned)";
                    lastRun = $"{liveCount:N0} result{(liveCount == 1 ? "" : "s")}{suffix}";
                    hasResults = liveCount > 0;
                }
                else
                {
                    lastRun = MiddleLayerService.LastRunSummary ?? _lastRunSummary;
                    hasResults = MiddleLayerService.LastRunSummary != null
                        && !MiddleLayerService.LastRunSummary.StartsWith("0 ", StringComparison.Ordinal);
                }
                var color = hasResults ? Color.FromArgb(255, 46, 160, 67) : (Color?)null;
                return MakeStatusSegment(Symbol.Clock, "Last run", lastRun, "Open the results viewer", color,
                    () => NavigateWithSpinner(typeof(FindNeedleUX.Pages.NativeResultsPage)));
            }
            case "outputfiles":
            {
                var outFiles = new List<string>();
                if (MiddleLayerService.LastRuleOutputFiles != null) outFiles.AddRange(MiddleLayerService.LastRuleOutputFiles);
                if (query is FindPluginCore.Searching.NuSearchQuery nq && nq.GeneratedRuleOutputFiles != null) outFiles.AddRange(nq.GeneratedRuleOutputFiles);
                var files = outFiles.Where(System.IO.File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (files.Count == 0) return null;
                return MakeStatusSegment(Symbol.Document, "Output files", files.Count.ToString(),
                    string.Join("\n", files.Select(System.IO.Path.GetFileName)), Color.FromArgb(255, 46, 160, 67),
                    () => contentFrame.Navigate(typeof(FindNeedleUX.Pages.ProcessorOutputPage)));
            }
            case "run_view":
                return MakeStatusActionButton(Symbol.Play, "Run → View Results",
                    "Run the search and open the results viewer", () => RunAndViewResults());
            case "run":
                return MakeStatusActionButton(Symbol.Refresh, "Run search",
                    "Run the search (without opening the viewer)", () => RunSearchOnly());
            case "stop":
            {
                var btn = MakeStatusActionButton(Symbol.Stop, "Stop", "Cancel the running search", () => StopSearch());
                btn.IsEnabled = IsSearchRunning;
                return btn;
            }
            case "perf":
            {
                var storage = MiddleLayerService.GetSearchStorage();
                if (storage == null) return null;
                var tier = storage.GetType().Name.Replace("Storage", "");
                return MakeStatusSegment(Symbol.Repair, "Storage", tier,
                    "Search storage tier — click for the timing / why-so-slow report", null,
                    () => contentFrame.Navigate(typeof(FindNeedleUX.Pages.SearchStatisticsPage)));
            }
            case "connections":
            {
                var n = FindNeedleUX.Services.ConnectionStore.GetAll().Count;
                return MakeStatusSegment(Symbol.Contact, "Connections", n.ToString(),
                    "Saved online-source connections", null,
                    () => contentFrame.Navigate(typeof(FindNeedleUX.Pages.ConnectionsPage)));
            }
            case "autorules":
            {
                var n = MiddleLayerService.LastAutoAddedRules?.Count ?? 0;
                return MakeStatusSegment(Symbol.Bookmarks, "Auto-rules", n.ToString(),
                    "Rules auto-added to the last search", null,
                    () => contentFrame.Navigate(typeof(FindNeedleUX.Pages.AutoAddRulesPage)));
            }
            case "diagram":
                return MakeStatusNavChip(Symbol.View, "Diagrams", "Open the UML / diagram tools",
                    () => contentFrame.Navigate(typeof(FindNeedleUX.Pages.DiagramToolsPage)));
            case "mcp":
                return BuildMcpChip();
            default:
                return null;
        }
    }

    private bool _searchRunning;
    private bool IsSearchRunning => _searchRunning || MiddleLayerService.CurrentStreamingSearch != null;

    /// <summary>Run the current search without opening the viewer (status-bar "Run search").</summary>
    public async void RunSearchOnly()
    {
        if ((MiddleLayerService.Locations?.Count ?? 0) == 0)
        { contentFrame.Navigate(typeof(FindNeedleUX.Pages.SearchLocationsPage)); return; }
        _searchRunning = true; RefreshStatusStrip();
        try { await RunSearchWithProgress(); }
        finally { _searchRunning = false; RefreshStatusStrip(); }
    }

    /// <summary>Cancel the running search (streaming or progress) — status-bar "Stop".</summary>
    public void StopSearch()
    {
        try { MiddleLayerService.CurrentStreamingSearch?.Stop(); } catch { }
        try { _quickActionCts?.Cancel(); } catch { }
    }

    /// <summary>A flat nav chip (icon + label, no count) for the status bar.</summary>
    private Button MakeStatusNavChip(Symbol icon, string label, string tooltip, Action onClick)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new SymbolIcon { Symbol = icon, RenderTransform = new ScaleTransform { ScaleX = 0.7, ScaleY = 0.7 }, RenderTransformOrigin = new global::Windows.Foundation.Point(0.5, 0.5) });
        row.Children.Add(new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        var btn = new Button { Content = row, Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(8, 2, 8, 2), MinHeight = 0 };
        ToolTipService.SetToolTip(btn, tooltip);
        btn.Click += (_, _) => { try { onClick(); } catch { } };
        return btn;
    }

    /// <summary>MCP chip: a colored dot + state (off / listening / a client is connected). Click opens
    /// the MCP help/config. "Connected" = a client request arrived in the last 2 minutes.</summary>
    /// <summary>A status-bar chip that appears when a UML diagram is available for the current results —
    /// green "ready" once one's been generated, blue "available" when an output rule can produce one.
    /// Injected directly by RefreshStatusStrip (not catalog-gated) so it always surfaces when relevant.
    /// Returns null when no diagram is ready or available.</summary>
    private FrameworkElement BuildUmlChip()
    {
        bool ready, available;
        try
        {
            ready = MiddleLayerService.GeneratedDiagramFiles.Count > 0;
            available = !ready && MiddleLayerService.HasUmlOutputRule;
        }
        catch { return null; }
        if (!ready && !available) return null;

        var (text, dot, tip) = ready
            ? ("UML diagram ready", Color.FromArgb(255, 46, 160, 67), "A UML diagram has been generated — click to view it.")
            : ("UML diagram available", Color.FromArgb(255, 0, 120, 215), "An output rule can produce a UML diagram — click to generate it.");

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(dot), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new SymbolIcon { Symbol = Symbol.View, RenderTransform = new ScaleTransform { ScaleX = 0.7, ScaleY = 0.7 }, RenderTransformOrigin = new global::Windows.Foundation.Point(0.5, 0.5) });
        row.Children.Add(new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        var btn = new Button { Content = row, Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(8, 2, 8, 2), MinHeight = 0 };
        ToolTipService.SetToolTip(btn, tip);
        btn.Click += (_, _) => contentFrame.Navigate(typeof(FindNeedleUX.Pages.ProcessorOutputPage));
        return btn;
    }

    private FrameworkElement BuildMcpChip()
    {
        bool running = FindNeedleUX.Services.Mcp.McpViewerBridge.Instance.ServerPort > 0;
        var last = FindNeedleUX.Services.Mcp.McpServer.LastActivityUtc;
        bool connected = running && last.HasValue && (DateTime.UtcNow - last.Value).TotalSeconds < 120;

        var (text, dot) = !running
            ? ("MCP off", Color.FromArgb(255, 158, 158, 158))
            : connected
                ? ("MCP · client connected", Color.FromArgb(255, 46, 160, 67))
                : ("MCP · listening", Color.FromArgb(255, 0, 120, 215));

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(dot), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        var btn = new Button { Content = row, Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(8, 2, 8, 2), MinHeight = 0 };
        ToolTipService.SetToolTip(btn, running
            ? (connected ? $"An MCP client is connected (last request {last:HH:mm:ss}). Click for details." : "MCP server is listening for a client. Click for details.")
            : "MCP server is off. Click for details.");
        btn.Click += (_, _) => McpHelpButton_Click(btn, null);
        return btn;
    }

    /// <summary>A prominent action button for the status bar (icon + label), distinct from the info
    /// segments — used for "Run → View Results".</summary>
    private Button MakeStatusActionButton(Symbol icon, string label, string tooltip, Action onClick)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new SymbolIcon { Symbol = icon, RenderTransform = new ScaleTransform { ScaleX = 0.7, ScaleY = 0.7 }, RenderTransformOrigin = new global::Windows.Foundation.Point(0.5, 0.5) });
        row.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var btn = new Button
        {
            Content = row,
            Padding = new Thickness(10, 2, 10, 2),
            MinHeight = 0,
            Background = new SolidColorBrush(Color.FromArgb(30, 46, 160, 67)),
        };
        ToolTipService.SetToolTip(btn, tooltip);
        btn.Click += (_, _) => { try { onClick(); } catch { } };
        return btn;
    }

    /// <summary>Run the current search (locations/rules) and open the results viewer — the status-bar
    /// "Run → View Results" action.</summary>
    public async void RunAndViewResults()
    {
        if ((MiddleLayerService.Locations?.Count ?? 0) == 0)
        {
            contentFrame.Navigate(typeof(FindNeedleUX.Pages.SearchLocationsPage));
            return;
        }
        await OpenWithOptionalStreamingAsync("Running search…");
    }

    /// <summary>A flat, clickable status-bar segment: icon + "Label: value", with a tooltip and an
    /// optional value color. Clicking runs <paramref name="onClick"/> (usually navigation).</summary>
    private Button MakeStatusSegment(Symbol icon, string label, string value, string tooltip, Color? valueColor, Action onClick)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new SymbolIcon { Symbol = icon, RenderTransform = new ScaleTransform { ScaleX = 0.7, ScaleY = 0.7 }, RenderTransformOrigin = new global::Windows.Foundation.Point(0.5, 0.5) });
        row.Children.Add(new TextBlock { Text = $"{label}:", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Colors.Gray) });
        var valBlock = new TextBlock { Text = value, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        if (valueColor.HasValue) valBlock.Foreground = new SolidColorBrush(valueColor.Value);
        row.Children.Add(valBlock);

        var btn = new Button
        {
            Content = row,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 2, 8, 2),
            MinHeight = 0,
        };
        ToolTipService.SetToolTip(btn, tooltip);
        btn.Click += (_, _) => { try { onClick(); } catch { } };
        return btn;
    }

    /// <summary>Open a flyout to choose which status-bar items show (and reorder them).</summary>
    private void EditStatusBar_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom };
        var selected = StatusBarCatalog.GetSelectedIds();

        // Selected items first (in order) with toggle + move up/down, then the unselected ones to add.
        foreach (var id in selected)
        {
            var item = StatusBarCatalog.Find(id);
            if (item == null) continue;
            var sub = new MenuFlyoutSubItem { Text = $"✓ {item.Label}" };
            var up = new MenuFlyoutItem { Text = "Move left" };
            up.Click += (_, _) => { StatusBarCatalog.Move(id, -1); RefreshStatusStrip(); };
            var down = new MenuFlyoutItem { Text = "Move right" };
            down.Click += (_, _) => { StatusBarCatalog.Move(id, +1); RefreshStatusStrip(); };
            var hide = new MenuFlyoutItem { Text = "Hide" };
            hide.Click += (_, _) => { StatusBarCatalog.Toggle(id); RefreshStatusStrip(); };
            sub.Items.Add(up); sub.Items.Add(down); sub.Items.Add(hide);
            menu.Items.Add(sub);
        }

        var addable = StatusBarCatalog.All.Where(a => !selected.Contains(a.Id)).ToList();
        if (addable.Count > 0)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            foreach (var a in addable)
            {
                var add = new MenuFlyoutItem { Text = $"➕ {a.Label}" };
                add.Click += (_, _) => { StatusBarCatalog.Toggle(a.Id); RefreshStatusStrip(); };
                menu.Items.Add(add);
            }
        }

        menu.ShowAt(StatusEditButton);
    }

    // The × on the status bar hides it; it's restored from Preferences (Settings ▸ Preferences ▸
    // "Show status bar"), not an on-screen handle. Persisted via ResultsViewerSettings (file-based).
    private void HideStatusStrip_Click(object sender, RoutedEventArgs e) => ResultsViewerSettings.ShowStatusBar = false;

    private void ApplyPersistedStatusStripVisibility()
    {
        StatusStripBorder.Visibility = ResultsViewerSettings.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetWindowIcon(string iconPath)
    {
        var full = System.IO.Path.IsPathRooted(iconPath)
            ? iconPath
            : System.IO.Path.Combine(AppContext.BaseDirectory, iconPath);
        try
        {
            // AppWindow.SetIcon sets BOTH the taskbar and the title-bar (top-left) icon from the same
            // .ico, so they match. The old WM_SETICON path set only ICON_BIG (taskbar), leaving the
            // title bar on a default icon that didn't match.
            AppWindow?.SetIcon(full);
        }
        catch
        {
            // Fallback: set the small (title bar) AND big (taskbar) icons explicitly.
            var hwnd = new HWND(WinRT.Interop.WindowNative.GetWindowHandle(this));
            var big = PInvoke.LoadImage(IntPtr.Zero, full, GDI_IMAGE_TYPE.IMAGE_ICON, 0, 0, IMAGE_FLAGS.LR_LOADFROMFILE);
            var small = PInvoke.LoadImage(IntPtr.Zero, full, GDI_IMAGE_TYPE.IMAGE_ICON, 16, 16, IMAGE_FLAGS.LR_LOADFROMFILE);
            if (small == IntPtr.Zero) small = big;
            if (big != IntPtr.Zero) PInvoke.SendMessage(hwnd, PInvoke.WM_SETICON, (IntPtr)1 /*ICON_BIG*/, big);
            if (small != IntPtr.Zero) PInvoke.SendMessage(hwnd, PInvoke.WM_SETICON, (IntPtr)0 /*ICON_SMALL*/, small);
        }
    }

    private async void MenuFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        var selectedFlyoutItem = sender as MenuFlyoutItem;
        switch (selectedFlyoutItem.Name.ToLower())
        {
            case "newworkspace":
                MiddleLayerService.NewWorkspace();
                Logger.Instance.Log("Navigated: NewWorkspace (workspace reset)");
                break;
            case "clearworkspace":
                MiddleLayerService.ClearWorkspace();
                Logger.Instance.Log("Cleared workspace (locations + filters removed, search cancelled)");
                break;
            case "saveworkspace":
                SaveCommand();
                Logger.Instance.Log("Navigated: SaveWorkspace");
                break;
            case "openworkspace":
                LoadCommand();
                Logger.Instance.Log("Navigated: OpenWorkspace");
                break;
            case "search_location":
                Logger.Instance.Log("Navigated: SearchLocationsPage");
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.SearchLocationsPage));
                break;
            case "connections":
                Logger.Instance.Log("Navigated: ConnectionsPage");
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.ConnectionsPage));
                break;
            case "log_finder":
                Logger.Instance.Log("Navigated: LogFinderPage");
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.LogFinderPage));
                break;
            // All rule configuration now lives behind one tabbed Rules hub.
            case "rules":
                Logger.Instance.Log("Navigated: RulesPage");
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.RulesPage));
                break;
            // Back-compat aliases (status bar / quick actions) → open the matching tab.
            case "search_rules":
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.RulesPage), "files");
                break;
            case "reformat_rules":
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.RulesPage), "fields");
                break;
            case "auto_rules":
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.RulesPage), "autoadd");
                break;
            case "search_processors":
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.RulesPage), "active");
                break;
            case "search_plugins":
                Logger.Instance.Log("Navigated: PluginsPage");
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.PluginsPage));
                break;
            case "results_get":
                Logger.Instance.Log("Navigated: RunSearchPage");
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.RunSearchPage));
                break;
            case "results_statistics":
                Logger.Instance.Log("Navigated: SearchStatisticsPage");
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.SearchStatisticsPage));
                break;
            case "results_processoroutput":
                Logger.Instance.Log("Navigated: ProcessorOutputPage");
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.ProcessorOutputPage));
                break;
            case "results_viewnative":
                Logger.Instance.Log("Navigated: NativeResultsPage");
                NavigateWithSpinner(typeof(FindNeedleUX.Pages.NativeResultsPage));
                break;
            case "systeminfo":
                Logger.Instance.Log("Navigated: SystemInfoPage");
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.SystemInfoPage));
                break;
            case "about":
                Logger.Instance.Log("Navigated: AboutPage");
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.AboutPage));
                break;
            case "settings_resultviewer":
                Logger.Instance.Log("Navigated: ResultsViewerSettingsPage");
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.ResultsViewerSettingsPage));
                break;
            case "diagramtools":
                Logger.Instance.Log("Navigated: DiagramToolsPage");
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.DiagramToolsPage));
                break;
            case "rules_uml":
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.RulesPage), "uml");
                break;
            case "logs":
                Logger.Instance.Log("Navigated: LogsPage");
                var logsWindow = new FindNeedleUX.Windows.LogsWindow();
                logsWindow.Activate();
                break;
            case "openlogfile":
                Logger.Instance.Log("Opened log file picker");
                QuickFileOpen();
                break;
            case "openlogfolder":
                Logger.Instance.Log("Opened log folder picker");
                QuickFolderOpen();
                break;
            case "cached_searches":
                Logger.Instance.Log("Navigated: CachedSearchesPage");
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.CachedSearchesPage));
                break;
            case "openlogwithrules":
                Logger.Instance.Log("Navigated: QuickLogWithRulesPage");
                contentFrame.Navigate(typeof(FindNeedleUX.Pages.QuickLogWithRulesPage));
                break;
            case "inspect_etl":
                await InspectionService.InspectEtlAsync(this, (show, text) => ShowSpinner(show, text));
                break;
            case "inspect_binary":
                await InspectionService.InspectBinaryAsync(this, (show, text) => ShowSpinner(show, text));
                break;
            default:
                Logger.Instance.Log($"Navigation error: unknown menu item {selectedFlyoutItem.Name}");
                throw new Exception("bad code");
        }
    }

    /// <summary>
    /// Show the structured performance report for the most recent search + viewer load — the
    /// "why did this take so long" breakdown (phase timings, storage tier + reason, and plain-
    /// language hints). Selectable text plus a Copy button.
    /// </summary>
    private readonly ObservableCollection<StepRow> _stepRows = new();

    private void OnFlowProgress(string label, int step, int total)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (SpinnerPanel.Visibility != Visibility.Visible) return;
            var steps = FindNeedlePluginLib.FlowProgress.Steps();
            if (steps.Count == 0) { StepList.Visibility = Visibility.Collapsed; return; }

            // Advance the robot loader to the frame for the current step (sweep→papers→shelves→sort).
            if (UseRobotLoader)
            {
                var cur = steps.FirstOrDefault(s => s.Current);
                if (cur != null) SetRobotFrame(cur.Number - 1);
            }

            // "Show completed steps" off → only the current step row; on → the whole checklist.
            var view = ResultsViewerSettings.ShowStepHistory ? steps : steps.Where(s => s.Current).ToList();
            while (_stepRows.Count > view.Count) _stepRows.RemoveAt(_stepRows.Count - 1);
            while (_stepRows.Count < view.Count) _stepRows.Add(new StepRow());
            for (int i = 0; i < view.Count; i++)
            {
                var s = view[i];
                var r = _stepRows[i];
                r.Glyph = s.Done ? "✓" : s.Current ? "▶" : "•";
                r.Name = $"{s.Number}. {s.Name}";
                r.Detail = s.Detail ?? "";
                r.PercentText = s.Percent.HasValue ? (s.PercentIsEstimate ? $"~{s.Percent}%" : $"{s.Percent}%") : "";
                r.Opacity = s.Current ? 1.0 : s.Done ? 0.85 : 0.4;
                r.Weight = s.Current ? FontWeights.SemiBold : FontWeights.Normal;
            }
            StepList.Visibility = Visibility.Visible;
            SpinnerText.Visibility = Visibility.Collapsed;
        });
    }

    private async Task RunSearchWithProgress(bool surfaceScan = false)
    {
        _quickActionCts = new CancellationTokenSource();
        ShowSpinner(true, "Running search...", showCancel:true);
        // Register for progress updates
        var sink = MiddleLayerService.GetProgressEventSink();
        // The spinner is driven uniformly by FlowProgress (OnFlowProgress), which carries a clean,
        // consistent per-phase metric set directly by the engine. The sink's verbose freeform text
        // is intentionally not piped in (it duplicated/clashed with the phase names).
        void OnTextProgress(string text) { /* superseded by FlowProgress per-phase detail */ }
        void OnNumericProgress(int percent) { /* superseded by Step X of N */ }
        sink.RegisterForTextProgress(OnTextProgress);
        sink.RegisterForNumericProgress(OnNumericProgress);
        try
        {
            await Task.Run(() => MiddleLayerService.RunSearch(surfaceScan, _quickActionCts.Token).Wait(), _quickActionCts.Token);
            var stats = MiddleLayerService.GetStats();
            var count = MiddleLayerService.GetFilteredRowCount();
            var cacheSuffix = MiddleLayerService.LastSearchReusedCache ? " (from cache)" : " (scanned)";
            _lastRunSummary = $"{count} results{cacheSuffix}";
        }
        catch (OperationCanceledException)
        {
            DispatcherQueue.TryEnqueue(() => SpinnerText.Text = "Search cancelled.");
            _lastRunSummary = "cancelled";
        }
        finally
        {
            ShowSpinner(false);
            _quickActionCts?.Dispose();
            _quickActionCts = null;
            DispatcherQueue.TryEnqueue(RefreshStatusStrip);
        }
    }

    /// <summary>
    /// Re-run the current search forcing a full decode (no pre-scan/fail-fast) and bypassing the
    /// cached empty result, then open the viewer. Uses the standard progress spinner so the user
    /// gets live step status and a cancel button during the (slow) full decode. Backs the result
    /// viewer's "Decode anyway" button.
    /// </summary>
    public async void RerunWithFullDecode()
    {
        FindNeedlePluginLib.DecodeOptions.ForceFullDecode = true;
        var prevCacheMode = MiddleLayerService.CacheModeOverride;
        MiddleLayerService.CacheModeOverride = FindPluginCore.Searching.CacheReuseMode.Never;
        try
        {
            ShowSpinner(true, "Decoding anyway…", showCancel: true);
            await RunSearchWithProgress();
            ShowSpinner(false);
            await OpenViewerAsync();
        }
        catch (Exception ex)
        {
            // Never let a decode failure crash the app (this runs in an async void). Surface it.
            ShowSpinner(false);
            Logger.Instance.Log($"Decode anyway failed: {ex}");
            try
            {
                await new ContentDialog
                {
                    Title = "Decode anyway failed",
                    Content = ex.Message,
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot,
                }.ShowAsync();
            }
            catch { /* dialog couldn't show — already logged */ }
        }
        finally
        {
            FindNeedlePluginLib.DecodeOptions.ForceFullDecode = false;
            MiddleLayerService.CacheModeOverride = prevCacheMode;
        }
    }

    /// <summary>Re-run the search with a fresh scan (so a just-added rule actually applies) and reopen
    /// the viewer. Used by the column-header "quick rule" Apply.</summary>
    public async void RerunSearch()
    {
        var prevCacheMode = MiddleLayerService.CacheModeOverride;
        MiddleLayerService.CacheModeOverride = FindPluginCore.Searching.CacheReuseMode.Never;
        try
        {
            ShowSpinner(true, "Applying rule…", showCancel: true);
            await RunSearchWithProgress();
            ShowSpinner(false);
            await OpenViewerAsync();
        }
        catch (Exception ex)
        {
            ShowSpinner(false);
            Logger.Instance.Log($"Apply rule / rerun failed: {ex}");
        }
        finally { MiddleLayerService.CacheModeOverride = prevCacheMode; }
    }

    public async void QuickFileOpen()
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var file = Win32FileDialog.OpenFile(hWnd, new (string, string)[]
        {
            ("Log files", "*.txt;*.etl;*.log;*.zip;*.evtx"),
            ("All files", "*.*"),
        });
        if (file != null)
        {
            MiddleLayerService.NewWorkspace();
            MiddleLayerService.AddFolderLocation(file);
            await OpenWithOptionalStreamingAsync("Opening file...");
        }
    }

    public async void QuickFolderOpen()
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var folderPath = Win32FileDialog.PickFolder(hWnd);
        if (folderPath != null)
        {
            MiddleLayerService.NewWorkspace();
            MiddleLayerService.AddFolderLocation(folderPath);
            await OpenWithOptionalStreamingAsync("Opening folder...");
        }
    }

    /// <summary>
    /// Open a single file (or folder) path directly — used by file activation ("Open with → Find
    /// Needle") and the command line. Mirrors <see cref="QuickFileOpen"/> but takes the path instead
    /// of prompting. No-op for a missing path.
    /// </summary>
    public async System.Threading.Tasks.Task OpenPathAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !(System.IO.File.Exists(path) || System.IO.Directory.Exists(path)))
            return;
        MiddleLayerService.NewWorkspace();
        MiddleLayerService.AddFolderLocation(path); // handles a single file or a folder
        await OpenWithOptionalStreamingAsync("Opening file...");
    }

    /// <summary>Open one or more dropped paths: new workspace, add each existing file/folder, open the
    /// viewer once. Shared by drag-and-drop.</summary>
    public System.Threading.Tasks.Task OpenPathsAsync(System.Collections.Generic.IReadOnlyList<string> paths)
        => LoadPathsAsync(paths, clearFirst: true);

    private async System.Threading.Tasks.Task LoadPathsAsync(
        System.Collections.Generic.IReadOnlyList<string> paths, bool clearFirst)
    {
        var valid = new System.Collections.Generic.List<string>();
        if (paths != null)
            foreach (var p in paths)
                if (!string.IsNullOrWhiteSpace(p) && (System.IO.File.Exists(p) || System.IO.Directory.Exists(p)))
                    valid.Add(p);
        if (valid.Count == 0) return;
        if (clearFirst) MiddleLayerService.NewWorkspace();
        foreach (var p in valid) MiddleLayerService.AddFolderLocation(p);
        await OpenWithOptionalStreamingAsync(valid.Count == 1 ? "Opening file..." : $"Opening {valid.Count} files...");
    }

    /// <summary>Decide what a drop does when a workspace is already loaded: clear-and-open, add-to-existing,
    /// or ask — per the user's "drag and drop" setting (default: prompt). Empty workspace always just opens.</summary>
    private async System.Threading.Tasks.Task HandleDroppedPathsAsync(
        System.Collections.Generic.IReadOnlyList<string> paths)
    {
        if (MiddleLayerService.Locations.Count == 0) { await LoadPathsAsync(paths, clearFirst: true); return; }

        var mode = ResultsViewerSettings.DragDropMode;
        if (mode == DragDropMode.Prompt)
        {
            var choice = await PromptDropChoiceAsync(paths.Count);
            if (choice == null) return; // cancelled
            mode = choice.Value;
        }
        await LoadPathsAsync(paths, clearFirst: mode == DragDropMode.ClearAndAdd);
    }

    private async System.Threading.Tasks.Task<DragDropMode?> PromptDropChoiceAsync(int count)
    {
        try
        {
            var dlg = new ContentDialog
            {
                Title = count == 1 ? "Open dropped file" : $"Open {count} dropped files",
                Content = "A workspace is already loaded. Add the file(s) to it, or clear it and open fresh?",
                PrimaryButtonText = "Add to workspace",
                SecondaryButtonText = "Clear & open",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot,
            };
            return await dlg.ShowAsync() switch
            {
                ContentDialogResult.Primary => DragDropMode.AddToExisting,
                ContentDialogResult.Secondary => DragDropMode.ClearAndAdd,
                _ => (DragDropMode?)null,
            };
        }
        catch { return DragDropMode.ClearAndAdd; } // dialog failed → safe default
    }

    // ----- Drag & drop: drop log files/folders onto the viewer to open them -----
    private void Content_DragOver(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        if (e.DataView.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            try
            {
                e.DragUIOverride.Caption = "Open in Find Needle";
                e.DragUIOverride.IsCaptionVisible = true;
                e.DragUIOverride.IsGlyphVisible = true;
            }
            catch { /* DragUIOverride can be unavailable for some sources */ }
            if (DropOverlay != null) DropOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            e.AcceptedOperation = global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
        }
    }

    private void Content_DragLeave(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        if (DropOverlay != null) DropOverlay.Visibility = Visibility.Collapsed;
    }

    private async void Content_Drop(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        if (DropOverlay != null) DropOverlay.Visibility = Visibility.Collapsed;
        if (!e.DataView.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            return;
        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = new System.Collections.Generic.List<string>();
            foreach (var it in items)
                if (!string.IsNullOrWhiteSpace(it.Path)) paths.Add(it.Path);
            if (paths.Count > 0) await HandleDroppedPathsAsync(paths);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Drop failed: {ex.Message}"); }
        finally { deferral.Complete(); }
    }

    /// <summary>
    /// Run the search and show the viewer. When "open progressively" is enabled, start a streaming
    /// search and open the viewer as soon as the first page is ready (it keeps filling in the
    /// background with a banner); otherwise run the full search behind the spinner, then open.
    /// </summary>
    private async System.Threading.Tasks.Task OpenWithOptionalStreamingAsync(string label)
    {
        if (ResultsViewerSettings.StreamWhileLoading)
        {
            ShowSpinner(true, label);
            var handle = MiddleLayerService.RunSearchStreaming();
            await WaitForFirstRowsAsync(handle);
            ShowSpinner(false);
            await OpenViewerAsync();
        }
        else
        {
            ShowSpinner(true, label, showCancel: true);
            await RunSearchWithProgress();
            ShowSpinner(false);
            await OpenViewerAsync();
        }
    }

    /// <summary>Wait until a streaming search has produced its first rows (or finished / timed out),
    /// so the viewer opens with content instead of an empty grid.</summary>
    private static async System.Threading.Tasks.Task WaitForFirstRowsAsync(
        MiddleLayerService.StreamingSearchHandle handle, int timeoutMs = 8000)
    {
        var src = handle?.Source;
        if (src == null) return;
        var tcs = new System.Threading.Tasks.TaskCompletionSource();
        void OnRows() { if (src.TotalCount > 0) tcs.TrySetResult(); }
        src.RowsAvailable += OnRows;
        try
        {
            if (src.TotalCount > 0) tcs.TrySetResult();
            if (handle.SearchTask != null)
                _ = handle.SearchTask.ContinueWith(_ => tcs.TrySetResult());
            await System.Threading.Tasks.Task.WhenAny(tcs.Task, System.Threading.Tasks.Task.Delay(timeoutMs));
        }
        finally { src.RowsAvailable -= OnRows; }
    }

    /// <summary>
    /// Show the result viewer for a freshly-completed search. If the viewer is already the current page,
    /// NavigationCacheMode.Required means a re-navigation won't reload it — so refresh it in place;
    /// otherwise navigate to it normally.
    /// </summary>
    private async System.Threading.Tasks.Task OpenViewerAsync()
    {
        if (contentFrame.Content is FindNeedleUX.Pages.NativeResultsPage viewer)
            await viewer.ReloadResultsAsync();
        else
            contentFrame.Navigate(ResolveDefaultViewerType());
    }

    private async void LoadCommand()
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var path = Win32FileDialog.OpenFile(hWnd, new (string, string)[] { ("Workspace JSON", "*.json") });
        if (path == null) return;
        try
        {
            ShowSpinner(true, "Loading workspace...");
            MiddleLayerService.OpenWorkspace(path);
        }
        catch (Exception ex)
        {
            await ShowWorkspaceError("Couldn't open workspace", path, ex);
        }
        finally { ShowSpinner(false); }
    }

    private async void SaveCommand()
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var path = Win32FileDialog.SaveFile(hWnd, "SearchQuery",
            new (string, string)[] { ("Workspace JSON", "*.json") }, ".json");
        if (path == null) return;
        try
        {
            ShowSpinner(true, "Saving workspace...");
            MiddleLayerService.SaveWorkspace(path);
        }
        catch (Exception ex)
        {
            await ShowWorkspaceError("Couldn't save workspace", path, ex);
        }
        finally { ShowSpinner(false); }
    }

    // ----- Title bar color (Settings → Appearance → Title bar) -----
    private void ApplyTitleBarColor()
    {
        try
        {
            if (!Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported()) return; // Win11+
            var tb = AppWindow?.TitleBar;
            if (tb == null) return;

            global::Windows.UI.Color? bg = ResolveTitleBarColor();
            tb.BackgroundColor = bg;
            tb.ButtonBackgroundColor = bg;
            tb.InactiveBackgroundColor = bg;
            tb.ButtonInactiveBackgroundColor = bg;

            if (bg is global::Windows.UI.Color c)
            {
                // Pick black/white text for contrast against the chosen color.
                var lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
                var fg = lum > 0.6 ? global::Windows.UI.Color.FromArgb(255, 0, 0, 0)
                                   : global::Windows.UI.Color.FromArgb(255, 255, 255, 255);
                tb.ForegroundColor = fg;
                tb.ButtonForegroundColor = fg;
                tb.ButtonHoverForegroundColor = fg;
            }
            else
            {
                tb.ForegroundColor = null;
                tb.ButtonForegroundColor = null;
                tb.ButtonHoverForegroundColor = null;
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"ApplyTitleBarColor failed: {ex.Message}"); }
    }

    private static global::Windows.UI.Color? ResolveTitleBarColor()
    {
        switch (ResultsViewerSettings.TitleBarColorMode)
        {
            case "Accent":
                try { return new global::Windows.UI.ViewManagement.UISettings()
                        .GetColorValue(global::Windows.UI.ViewManagement.UIColorType.Accent); }
                catch { return null; }
            case "Custom":
                return FindNeedleUX.Pages.NativeResultViewer.HexToBrushConverter.ParseColor(
                    ResultsViewerSettings.TitleBarCustomColor);
            default:
                return null; // System default
        }
    }

    private async System.Threading.Tasks.Task ShowWorkspaceError(string title, string path, Exception ex)
    {
        try
        {
            await new ContentDialog
            {
                Title = title,
                Content = $"{path}\n\n{ex.Message}",
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot,
            }.ShowAsync();
        }
        catch { /* dialog itself failed — nothing more we can do */ }
    }

    // Step-aware robot loader: sweep → scan → papers → shelve → sort → type (see RobotLoader).
    private int _currentRobotIndex = -1;

    private static bool UseRobotLoader => RobotLoader.IsAnimated(ResultsViewerSettings.LoadingAnimation);

    /// <summary>Point the robot Image at the GIF for the given step (0-based, clamped). Only swaps the
    /// source when the step actually changes, so the animation isn't restarted on every progress tick.</summary>
    private void SetRobotFrame(int stepIndex)
    {
        if (SpinnerGif == null) return;
        int idx = Math.Clamp(stepIndex, 0, RobotLoader.Steps.Length - 1);
        if (idx == _currentRobotIndex) return;
        _currentRobotIndex = idx;
        SpinnerGif.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(RobotLoader.Uri(idx)));
    }

    private void ShowSpinner(bool show, string text = null, bool showCancel = false)
    {
        SpinnerPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        var mode = ResultsViewerSettings.LoadingAnimation;
        bool animated = RobotLoader.IsAnimated(mode);
        bool bar = mode == "Bar";

        SpinnerGifBorder.Visibility = (show && animated) ? Visibility.Visible : Visibility.Collapsed;
        SpinnerBar.Visibility = (show && bar) ? Visibility.Visible : Visibility.Collapsed;
        SpinnerBar.IsIndeterminate = show && bar;
        bool ring = show && !animated && !bar;
        EtlSpinner.Visibility = ring ? Visibility.Visible : Visibility.Collapsed;
        EtlSpinner.IsActive = ring;
        if (show && animated)
        {
            _currentRobotIndex = -1;   // force a fresh frame for this run
            SetRobotFrame(0);          // start on "sweep"
        }
        if (text != null)
            SpinnerText.Text = text;
        // Non-flow spinners (ETL inspect, workspace load) show the plain text line; the structured
        // step checklist is hidden until a search flow drives it via OnFlowProgress.
        StepList.Visibility = Visibility.Collapsed;
        SpinnerText.Visibility = Visibility.Visible;
        if (show && showCancel)
        {
            // Reset the button for a fresh operation.
            CancelQuickActionButton.IsEnabled = true;
            CancelQuickActionButton.Content = "Cancel";
        }
        CancelQuickActionButton.Visibility = show && showCancel ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CancelQuickActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_quickActionCts != null && !_quickActionCts.IsCancellationRequested)
        {
            // Cancellation only takes effect at the next checkpoint in the worker, which can be a
            // moment away — give immediate visual confirmation that the click registered.
            CancelQuickActionButton.IsEnabled = false;
            CancelQuickActionButton.Content = "Cancelling…";
            SpinnerText.Text = "Cancelling…";
            _quickActionCts.Cancel();
        }
    }
}

/// <summary>One row in the structured search-flow checklist (fixed columns: glyph · name · metric · %).</summary>
public sealed class StepRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    private void Set<T>(ref T field, T value, string name)
    {
        if (!Equals(field, value)) { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
    }

    private string _glyph = ""; public string Glyph { get => _glyph; set => Set(ref _glyph, value, nameof(Glyph)); }
    private string _name = ""; public string Name { get => _name; set => Set(ref _name, value, nameof(Name)); }
    private string _detail = ""; public string Detail { get => _detail; set => Set(ref _detail, value, nameof(Detail)); }
    private string _percentText = ""; public string PercentText { get => _percentText; set => Set(ref _percentText, value, nameof(PercentText)); }
    private double _opacity = 1.0; public double Opacity { get => _opacity; set => Set(ref _opacity, value, nameof(Opacity)); }
    private global::Windows.UI.Text.FontWeight _weight = FontWeights.Normal;
    public global::Windows.UI.Text.FontWeight Weight { get => _weight; set => Set(ref _weight, value, nameof(Weight)); }
}

// Add the following public static class to expose the required PInvoke methods and constants.

public static class PInvoke
{
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    public static extern IntPtr LoadImage(IntPtr hInst, string lpszName, GDI_IMAGE_TYPE uType, int cxDesired, int cyDesired, IMAGE_FLAGS fuLoad);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessage(HWND hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    public const uint WM_SETICON = 0x0080;
}

// Update the HWND struct to include a constructor that takes a single argument.
public struct HWND : IEquatable<HWND>
{
    public nint Value;

    public HWND(nint value)
    {
        Value = value;
    }

    public bool Equals(HWND other) => Value == other.Value;

    public override bool Equals(object obj) => obj is HWND other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value.ToString();
}

// To fix the CS0122 errors, the issue is that the `GDI_IMAGE_TYPE` and `IMAGE_FLAGS` enums are inaccessible due to their protection level.  
// These enums are likely defined as `internal` in the `PInvoke` class.  
// To resolve this, you can either make these enums `public` in their definition or create new public enums in your code.  
// Below is the fix by defining public enums in the same file:

public enum GDI_IMAGE_TYPE
{
    IMAGE_BITMAP = 0,
    IMAGE_CURSOR = 2,
    IMAGE_ICON = 1
}

public enum IMAGE_FLAGS
{
    LR_CREATEDIBSECTION = 8192,
    LR_DEFAULTCOLOR = 0,
    LR_DEFAULTSIZE = 64,
    LR_LOADFROMFILE = 16,
    LR_LOADMAP3DCOLORS = 4096,
    LR_LOADTRANSPARENT = 32,
    LR_MONOCHROME = 1,
    LR_SHARED = 32768,
    LR_VGACOLOR = 128,
    LR_COPYDELETEORG = 8,
    LR_COPYFROMRESOURCE = 16384,
    LR_COPYRETURNORG = 4
}
