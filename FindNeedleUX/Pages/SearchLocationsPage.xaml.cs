using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FindNeedleUX.Services;
using FindNeedleUX.Utils;
using FindNeedleUX.ViewModels;
using ADOPlugin.Location;
using FindNeedlePluginLib;
using GithubPlugin.Location;
using KustoPlugin.Location;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FindNeedleUX.Pages;

public sealed partial class SearchLocationsPage : Page
{
    private readonly SearchLocationsViewModel _viewModel = new();

    public SearchLocationsPage()
    {
        this.InitializeComponent();
        CheckOtherDLLs.AreWeInstalledOk();
        // Bind the repeater once; the VM refreshes its collection in place.
        VariedImageSizeRepeater.ItemsSource = _viewModel.Locations;
    }

    private void Button_Remove(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name })
        {
            _viewModel.RemoveLocation(name);
        }
    }

    private void Button_AddFolder(object sender, RoutedEventArgs e)
    {
        var window = WindowUtil.GetWindowForElement(this);
        var hWnd = WindowNative.GetWindowHandle(window);
        var path = FindNeedleUX.Services.Win32FileDialog.PickFolder(hWnd);
        if (path != null)
        {
            _viewModel.AddLocation(path);
        }
    }

    private void Button_AddFile(object sender, RoutedEventArgs e)
    {
        var window = WindowUtil.GetWindowForElement(this);
        var hWnd = WindowNative.GetWindowHandle(window);
        var path = FindNeedleUX.Services.Win32FileDialog.OpenFile(hWnd, new (string, string)[]
        {
            ("Log files", "*.txt;*.etl;*.log;*.zip;*.evtx"),
            ("All files", "*.*"),
        });
        if (path != null)
        {
            _viewModel.AddLocation(path);
        }
    }

    private async void Button_AddKusto(object sender, RoutedEventArgs e)
    {
        var loc = await ShowKustoDialogAsync(null);
        if (loc != null) { MiddleLayerService.AddLocation(loc); _viewModel.Refresh(); }
    }

    private async void Button_AddAdo(object sender, RoutedEventArgs e)
    {
        var loc = await ShowAdoDialogAsync(null);
        if (loc == null) return;
        // Do the interactive Azure AD sign-in now (while adding) rather than surprising the user with a
        // browser pop-up during the first search. Non-fatal: if it doesn't complete, add anyway — the
        // search will prompt again.
        if (loc.AuthMode == AdoAuthMode.Interactive)
        {
            try { await Task.Run(() => AdoLocation.PrimeInteractiveCredential()); }
            catch (Exception ex)
            {
                await new ContentDialog
                {
                    Title = "Azure DevOps sign-in",
                    Content = "Sign-in didn't complete — it'll prompt again when you run the search.\n\n" + ex.Message,
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot,
                }.ShowAsync();
            }
        }
        if (await AddIfReachableAsync("Azure DevOps", () => loc.TestConnection())) { MiddleLayerService.AddLocation(loc); _viewModel.Refresh(); }
    }

    /// <summary>Validate an online source's connection (off the UI thread) before adding it, surfacing
    /// any error at add time. On failure the user can still add it anyway. Returns true if it should be
    /// added.</summary>
    private async Task<bool> AddIfReachableAsync(string what, Func<string> test)
    {
        string err;
        try { err = await Task.Run(test); }
        catch (Exception ex) { err = ex.Message; }
        if (string.IsNullOrEmpty(err)) return true;

        var dialog = new ContentDialog
        {
            Title = $"Couldn't reach this {what} source",
            Content = err + "\n\nAdd it anyway?",
            PrimaryButtonText = "Add anyway",
            CloseButtonText = "Go back",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async void Button_AddGithub(object sender, RoutedEventArgs e)
    {
        var loc = await ShowGithubDialogAsync(null);
        if (loc == null) return;
        if (await AddIfReachableAsync("GitHub", () => loc.TestConnection())) { MiddleLayerService.AddLocation(loc); _viewModel.Refresh(); }
    }

    private async void Button_Edit(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name }) return;
        // The online (configurable) locations are editable; local file/folder ones aren't.
        ISearchLocation updated = MiddleLayerService.Locations.FirstOrDefault(l => l.GetName() == name) switch
        {
            KustoLocation k        => await ShowKustoDialogAsync(k),
            AdoLocation a          => await ShowAdoDialogAsync(a),
            GithubIssuesLocation g => await ShowGithubDialogAsync(g),
            _ => null,
        };
        if (updated == null) return;
        MiddleLayerService.RemoveLocationByName(name); // replace the old one (its name may have changed)
        MiddleLayerService.AddLocation(updated);
        _viewModel.Refresh();
    }

    /// <summary>Azure DevOps location dialog: org URL, project, auth (PAT or AAD), and either a WIQL
    /// query or an explicit list of work item ids.</summary>
    private async Task<AdoLocation> ShowAdoDialogAsync(AdoLocation existing)
    {
        var org = new TextBox
        {
            Header = "Organization URL",
            PlaceholderText = "https://dev.azure.com/org  or  https://org.visualstudio.com",
            Text = existing?.OrganizationUrl ?? "",
        };
        var project = new TextBox { Header = "Project", PlaceholderText = "MyProject", Text = existing?.Project ?? "" };

        var auth = new RadioButtons { Header = "Sign-in", MaxColumns = 1 };
        auth.Items.Add("Personal Access Token (PAT)");
        auth.Items.Add("Azure AD interactive sign-in");
        auth.SelectedIndex = existing != null ? (int)existing.AuthMode : 0;
        AdoAuthMode Mode() => auth.SelectedIndex == 1 ? AdoAuthMode.Interactive : AdoAuthMode.Pat;

        var pat = new PasswordBox { Header = "PAT (needs Work Items: Read)", Password = existing?.Pat ?? "" };
        var ids = new TextBox
        {
            Header = "Work item IDs (optional, comma-separated)",
            PlaceholderText = "e.g. 1, 2", Text = existing?.Ids ?? "",
        };
        var wiql = new TextBox
        {
            Header = "WIQL query (used when no IDs given; blank = recently changed)",
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60,
            Text = existing?.Wiql ?? "",
        };
        var attach = new CheckBox
        {
            Content = "Open the work items' log attachments (download & parse the files)",
            IsChecked = existing?.OpenAttachments ?? false,
        };

        var panel = new StackPanel { Spacing = 10, MinWidth = 480 };
        panel.Children.Add(org);
        panel.Children.Add(project);
        panel.Children.Add(auth);
        panel.Children.Add(pat);
        panel.Children.Add(ids);
        panel.Children.Add(wiql);
        panel.Children.Add(attach);

        var dialog = new ContentDialog
        {
            Title = existing == null ? "Add Azure DevOps location" : "Edit Azure DevOps location",
            Content = new ScrollViewer { Content = panel, MaxHeight = 560,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 16, 0) },
            PrimaryButtonText = existing == null ? "Add" : "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        if (string.IsNullOrWhiteSpace(org.Text) || string.IsNullOrWhiteSpace(project.Text)) return null;

        return new AdoLocation(org.Text, project.Text, Mode(), pat.Password, wiql.Text, ids.Text,
            top: 200, openAttachments: attach.IsChecked == true);
    }

    /// <summary>GitHub issues location dialog: repo (URL or owner/repo), optional token, state.</summary>
    private async Task<GithubIssuesLocation> ShowGithubDialogAsync(GithubIssuesLocation existing)
    {
        var repo = new TextBox
        {
            Header = "Repository (URL or owner/repo)",
            PlaceholderText = "https://github.com/owner/repo  or  owner/repo",
            Text = existing == null ? "" : $"{existing.Owner}/{existing.Repo}",
        };
        var token = new PasswordBox { Header = "Token (optional; for private repos / higher rate limit)", Password = existing?.Token ?? "" };
        var issue = new TextBox
        {
            Header = "Issue number — open its log attachments (blank = list issues)",
            PlaceholderText = "e.g. 3",
            Text = existing != null && existing.IssueNumber > 0 ? existing.IssueNumber.ToString() : "",
        };
        var state = new RadioButtons { Header = "Issue state (when listing)", MaxColumns = 3 };
        state.Items.Add("all");
        state.Items.Add("open");
        state.Items.Add("closed");
        state.SelectedIndex = existing?.State switch { "open" => 1, "closed" => 2, _ => 0 };
        string State() => state.SelectedIndex switch { 1 => "open", 2 => "closed", _ => "all" };

        var panel = new StackPanel { Spacing = 10, MinWidth = 460 };
        panel.Children.Add(repo);
        panel.Children.Add(token);
        panel.Children.Add(issue);
        panel.Children.Add(state);

        var dialog = new ContentDialog
        {
            Title = existing == null ? "Add GitHub issues location" : "Edit GitHub issues location",
            Content = new ScrollViewer { Content = panel, MaxHeight = 400,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 16, 0) },
            PrimaryButtonText = existing == null ? "Add" : "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        if (string.IsNullOrWhiteSpace(repo.Text)) return null;

        int.TryParse(issue.Text?.Trim(), out var issueNum);
        return new GithubIssuesLocation(repo.Text, token.Password, State(), 500, issueNum);
    }

    /// <summary>
    /// Shows the Kusto cluster/database/query/auth dialog. Pre-filled from <paramref name="existing"/>
    /// when editing. Returns the configured location, or null if cancelled/incomplete.
    /// </summary>
    private async Task<KustoLocation> ShowKustoDialogAsync(KustoLocation existing)
    {
        var cluster = new TextBox
        {
            Header = "Cluster URI",
            PlaceholderText = "https://<name>.<region>.kusto.windows.net",
            Text = existing?.ClusterUri ?? "https://kvc-6u9h87febddjedr8ed.southcentralus.kusto.windows.net",
        };
        // RadioButtons (not a ComboBox) so there's no dropdown to get clipped by the dialog scroll.
        var auth = new RadioButtons { Header = "Sign-in", MaxColumns = 1 };
        auth.Items.Add("Interactive browser sign-in");
        auth.Items.Add("Azure CLI (az login)");
        auth.Items.Add("Device code");
        auth.SelectedIndex = existing != null ? (int)existing.AuthMode : 0;

        KustoAuthMode Mode() => auth.SelectedIndex switch
        {
            1 => KustoAuthMode.AzureCli,
            2 => KustoAuthMode.DeviceCode,
            _ => KustoAuthMode.Interactive,
        };

        // Row limit — Kusto caps results at 500k; raise or remove it (results stream to disk, so even
        // millions stay low-memory, but a big pull takes longer and loads the cluster).
        var rowLimit = new RadioButtons { Header = "Row limit", MaxColumns = 2 };
        rowLimit.Items.Add("Default (500,000)");
        rowLimit.Items.Add("Up to 1,000,000");
        rowLimit.Items.Add("Up to 5,000,000");
        rowLimit.Items.Add("All rows (no limit)");
        rowLimit.SelectedIndex = existing == null ? 0 : existing.RowLimit switch
        {
            1_000_000 => 1,
            5_000_000 => 2,
            < 0 => 3,
            _ => 0,
        };
        long SelectedRowLimit() => rowLimit.SelectedIndex switch
        {
            1 => 1_000_000,
            2 => 5_000_000,
            3 => -1,
            _ => 0,
        };

        Microsoft.UI.Xaml.Media.Brush Dim()
            => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

        // ---- small layout helpers for a cleaner, clearly-labelled dialog ----
        TextBlock Section(string t) => new()
        {
            Text = t,
            FontWeight = global::Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 6, 0, 0),
        };
        TextBlock Hint(string t) => new() { Text = t, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Dim() };
        Border Boxed(FrameworkElement child)
        {
            child.MinHeight = 64;
            return new Border
            {
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = child,
            };
        }

        // Database — type it, or Load and click from the inline list (no dropdown → nothing to clip).
        var dbBox = new TextBox { PlaceholderText = "Selected database (type, or pick below)", Text = existing?.Database ?? string.Empty };
        var dbFilter = new TextBox { PlaceholderText = "Filter databases…" };
        var dbList = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 160 };
        var loadDbBtn = new Button { Content = "Load", VerticalAlignment = VerticalAlignment.Bottom, MinWidth = 72 };
        var dbStatus = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Dim() };
        var allDbs = new List<string>();
        // Filter the (possibly very large) list as you type; ListView virtualizes the rest.
        void ApplyDbFilter()
        {
            var f = (dbFilter.Text ?? string.Empty).Trim();
            var items = string.IsNullOrEmpty(f)
                ? allDbs
                : allDbs.Where(d => d.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            dbList.ItemsSource = items;
            if (!string.IsNullOrWhiteSpace(dbBox.Text) && items.Contains(dbBox.Text)) dbList.SelectedItem = dbBox.Text;
        }
        dbFilter.TextChanged += (_, __) => ApplyDbFilter();
        loadDbBtn.Click += async (_, __) =>
        {
            if (string.IsNullOrWhiteSpace(cluster.Text)) { dbStatus.Text = "Enter a cluster URI first."; return; }
            var prev = loadDbBtn.Content; loadDbBtn.IsEnabled = false; loadDbBtn.Content = "…";
            dbStatus.Text = "Connecting… (you may be prompted to sign in the first time)";
            try
            {
                var clusterUri = cluster.Text; var mode = Mode();
                allDbs = await Task.Run(() => KustoLocation.GetDatabases(clusterUri, mode));
                ApplyDbFilter();
                dbStatus.Text = allDbs.Count > 0 ? $"{allDbs.Count} databases — filter/click to choose." : "No databases found.";
            }
            catch (Exception ex) { dbStatus.Text = "Failed to load databases: " + ex.Message; }
            finally { loadDbBtn.Content = prev; loadDbBtn.IsEnabled = true; }
        };
        dbList.SelectionChanged += (_, __) => { if (dbList.SelectedItem is string d) dbBox.Text = d; };

        // dbBox stretches; Load sits to its right, baseline-aligned.
        var dbRow = new Grid { ColumnSpacing = 8 };
        dbRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dbRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(dbBox, 0); Grid.SetColumn(loadDbBtn, 1);
        dbRow.Children.Add(dbBox); dbRow.Children.Add(loadDbBtn);

        var query = new TextBox
        {
            PlaceholderText = "MyTable | where Timestamp > ago(1h) | take 1000",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 110,
            Text = existing?.Query ?? string.Empty,
        };

        // Tables in the chosen database — click one to seed a query from it.
        var tables = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 160 };
        var tableFilter = new TextBox { PlaceholderText = "Filter tables…" };
        var loadTablesBtn = new Button { Content = "Load tables" };
        var tablesStatus = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Dim() };
        var allTables = new List<string>();
        void ApplyTableFilter()
        {
            var f = (tableFilter.Text ?? string.Empty).Trim();
            tables.ItemsSource = string.IsNullOrEmpty(f)
                ? allTables
                : allTables.Where(t => t.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }
        tableFilter.TextChanged += (_, __) => ApplyTableFilter();
        loadTablesBtn.Click += async (_, __) =>
        {
            var db = (dbBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(cluster.Text) || string.IsNullOrWhiteSpace(db))
            { tablesStatus.Text = "Pick a cluster + database first."; return; }
            var prev = loadTablesBtn.Content; loadTablesBtn.IsEnabled = false; loadTablesBtn.Content = "Loading…"; tablesStatus.Text = "Loading tables…";
            try
            {
                var clusterUri = cluster.Text; var mode = Mode();
                allTables = await Task.Run(() => KustoLocation.GetTables(clusterUri, db, mode));
                ApplyTableFilter();
                tablesStatus.Text = allTables.Count > 0 ? $"{allTables.Count} tables — filter/click to build a query." : "No tables found.";
            }
            catch (Exception ex) { tablesStatus.Text = "Failed to load tables: " + ex.Message; }
            finally { loadTablesBtn.Content = prev; loadTablesBtn.IsEnabled = true; }
        };
        tables.SelectionChanged += (_, __) =>
        {
            if (tables.SelectedItem is string t) query.Text = $"['{t}']\n| take 100";
        };

        // Preview the query's first rows before adding it.
        var previewBtn = new Button { Content = "Run preview" };
        var previewOut = new TextBox
        {
            IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 12,
            MinHeight = 80, MaxHeight = 180, PlaceholderText = "Preview results appear here",
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(previewOut, ScrollBarVisibility.Auto);
        previewBtn.Click += async (_, __) =>
        {
            var db = (dbBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(cluster.Text) || string.IsNullOrWhiteSpace(db) || string.IsNullOrWhiteSpace(query.Text))
            { previewOut.Text = "Enter cluster + database + query first."; return; }
            var prev = previewBtn.Content; previewBtn.IsEnabled = false; previewBtn.Content = "Running…"; previewOut.Text = "Running…";
            try
            {
                var clusterUri = cluster.Text; var q = query.Text; var mode = Mode();
                var (cols, rows, truncated) = await Task.Run(() => KustoLocation.PreviewQuery(clusterUri, db, q, mode, 50));
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(string.Join("\t", cols));
                foreach (var r in rows) sb.AppendLine(string.Join("\t", r));
                sb.AppendLine();
                sb.Append(truncated ? $"(showing first {rows.Count} rows)" : $"({rows.Count} rows)");
                previewOut.Text = sb.ToString();
            }
            catch (Exception ex) { previewOut.Text = KustoLocation.FriendlyError(ex, query.Text); }
            finally { previewBtn.Content = prev; previewBtn.IsEnabled = true; }
        };

        var dbGroup = new StackPanel { Spacing = 4 };
        dbGroup.Children.Add(Section("Database"));
        dbGroup.Children.Add(dbRow);
        dbGroup.Children.Add(Hint("Databases on the cluster (filter, then click to choose):"));
        dbGroup.Children.Add(dbFilter);
        dbGroup.Children.Add(Boxed(dbList));
        dbGroup.Children.Add(dbStatus);

        var tablesGroup = new StackPanel { Spacing = 4 };
        tablesGroup.Children.Add(Section("Tables"));
        tablesGroup.Children.Add(loadTablesBtn);
        tablesGroup.Children.Add(Hint("Tables in the database (filter, then click to start a query):"));
        tablesGroup.Children.Add(tableFilter);
        tablesGroup.Children.Add(Boxed(tables));
        tablesGroup.Children.Add(tablesStatus);

        var queryGroup = new StackPanel { Spacing = 4 };
        queryGroup.Children.Add(Section("KQL query"));
        queryGroup.Children.Add(query);

        var previewGroup = new StackPanel { Spacing = 4 };
        previewGroup.Children.Add(Section("Preview"));
        previewGroup.Children.Add(previewBtn);
        previewGroup.Children.Add(previewOut);

        var panel = new StackPanel { Spacing = 8, MinWidth = 540 };
        panel.Children.Add(cluster);
        panel.Children.Add(auth);
        panel.Children.Add(rowLimit);
        panel.Children.Add(dbGroup);
        panel.Children.Add(tablesGroup);
        panel.Children.Add(queryGroup);
        panel.Children.Add(previewGroup);

        var dialog = new ContentDialog
        {
            Title = existing == null ? "Add Kusto location" : "Edit Kusto location",
            Content = new ScrollViewer
            {
                Content = panel, MaxHeight = 620,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                // Inset the content so the vertical scrollbar doesn't sit on top of the textboxes.
                Padding = new Thickness(0, 0, 16, 0),
            },
            PrimaryButtonText = existing == null ? "Add" : "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };
        // Let the dialog grow wider than the default (~548px) so the inset content has room.
        dialog.Resources["ContentDialogMaxWidth"] = 760.0;
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;

        var database = (dbBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cluster.Text) || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(query.Text))
            return null;

        return new KustoLocation(cluster.Text, database, query.Text, Mode(), SelectedRowLimit());
    }
}
