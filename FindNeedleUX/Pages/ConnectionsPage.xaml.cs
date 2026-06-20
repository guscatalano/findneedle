using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FindNeedleUX.Services;
using KustoPlugin.Location;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Text;

namespace FindNeedleUX.Pages;

/// <summary>Manage saved online-source connections (ADO / GitHub / Kusto): the reusable endpoint +
/// credentials, separate from the per-query locations. Add/edit/remove here; pick them when adding a
/// location on the Locations page.</summary>
public sealed partial class ConnectionsPage : Page
{
    public ConnectionsPage()
    {
        this.InitializeComponent();
        Loaded += (_, _) => RenderList();
    }

    private void RenderList()
    {
        ConnList.Items.Clear();
        var all = ConnectionStore.GetAll();
        EmptyHint.Visibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var c in all)
            ConnList.Items.Add(BuildRow(c));
    }

    private FrameworkElement BuildRow(SavedConnection c)
    {
        var grid = new Grid { ColumnSpacing = 8, Padding = new Thickness(8, 6, 8, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new TextBlock { Text = KindEmoji(c.Kind), FontSize = 18, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(icon, 0); grid.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = c.Name, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = KindLabel(c.Kind) + Detail(c), FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(text, 1); grid.Children.Add(text);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var edit = new Button { Content = "Edit" };
        edit.Click += async (_, _) => { await EditAsync(c); };
        var remove = new Button { Content = "Remove" };
        remove.Click += (_, _) => { ConnectionStore.Remove(c.Id); RenderList(); };
        actions.Children.Add(edit); actions.Children.Add(remove);
        Grid.SetColumn(actions, 2); grid.Children.Add(actions);

        return grid;
    }

    private static string KindEmoji(string kind) => kind switch { "ado" => "🔷", "github" => "🐙", "kusto" => "🔎", _ => "🔗" };
    private static string KindLabel(string kind) => kind switch { "ado" => "Azure DevOps", "github" => "GitHub", "kusto" => "Kusto", _ => kind };
    private static string Detail(SavedConnection c) => c.Kind switch
    {
        "ado"    => $" · {(c.AdoAuthMode == 1 ? "Azure AD" : "PAT")}",
        "github" => string.IsNullOrEmpty(c.GithubTokenEnc) ? " · no token" : " · token saved",
        "kusto"  => string.IsNullOrEmpty(c.KustoDatabase) ? "" : $" · {c.KustoDatabase}",
        _        => "",
    };

    private async Task EditAsync(SavedConnection c)
    {
        SavedConnection updated = c.Kind switch
        {
            "ado"    => await ShowAdoConnectionDialog(c),
            "github" => await ShowGithubConnectionDialog(c),
            "kusto"  => await ShowKustoConnectionDialog(c),
            _ => null,
        };
        if (updated != null) { ConnectionStore.Upsert(updated); RenderList(); }
    }

    private async void Button_AddAdo(object sender, RoutedEventArgs e)
    {
        var c = await ShowAdoConnectionDialog(null);
        if (c != null) { ConnectionStore.Upsert(c); RenderList(); }
    }

    private async void Button_AddGithub(object sender, RoutedEventArgs e)
    {
        var c = await ShowGithubConnectionDialog(null);
        if (c != null) { ConnectionStore.Upsert(c); RenderList(); }
    }

    private async void Button_AddKusto(object sender, RoutedEventArgs e)
    {
        var c = await ShowKustoConnectionDialog(null);
        if (c != null) { ConnectionStore.Upsert(c); RenderList(); }
    }

    // ── connection editor dialogs (fields only — no query) ──

    private async Task<SavedConnection> ShowAdoConnectionDialog(SavedConnection existing)
    {
        var name = new TextBox { Header = "Name (optional)", PlaceholderText = "e.g. MyOrg / MyProject", Text = existing?.Name ?? "" };
        var org = new TextBox { Header = "Organization URL",
            PlaceholderText = "https://dev.azure.com/org  or  https://org.visualstudio.com", Text = existing?.AdoOrg ?? "" };
        var project = new TextBox { Header = "Project", PlaceholderText = "MyProject", Text = existing?.AdoProject ?? "" };
        var auth = new RadioButtons { Header = "Sign-in", MaxColumns = 1 };
        auth.Items.Add("Personal Access Token (PAT)");
        auth.Items.Add("Azure AD interactive sign-in");
        auth.SelectedIndex = existing?.AdoAuthMode ?? 0;
        var pat = new PasswordBox { Header = "PAT (needs Work Items: Read)", Password = existing?.AdoPat ?? "" };

        var panel = new StackPanel { Spacing = 10, MinWidth = 460 };
        panel.Children.Add(name); panel.Children.Add(org); panel.Children.Add(project); panel.Children.Add(auth); panel.Children.Add(pat);

        if (!await ShowEditor(existing == null ? "Add Azure DevOps connection" : "Edit connection", panel,
                () => !string.IsNullOrWhiteSpace(org.Text) && !string.IsNullOrWhiteSpace(project.Text),
                "Organization URL and project are required.")) return null;

        var c = existing ?? new SavedConnection { Kind = "ado" };
        c.AdoOrg = org.Text; c.AdoProject = project.Text; c.AdoAuthMode = auth.SelectedIndex; c.AdoPat = pat.Password;
        c.Name = string.IsNullOrWhiteSpace(name.Text) ? c.DefaultName() : name.Text.Trim();
        return c;
    }

    private async Task<SavedConnection> ShowGithubConnectionDialog(SavedConnection existing)
    {
        var name = new TextBox { Header = "Name (optional)", PlaceholderText = "e.g. owner/repo", Text = existing?.Name ?? "" };
        var repo = new TextBox { Header = "Repository (URL or owner/repo)",
            PlaceholderText = "https://github.com/owner/repo  or  owner/repo", Text = existing?.GithubRepo ?? "" };
        var token = new PasswordBox { Header = "Token (optional; private repos / higher rate limit)", Password = existing?.GithubToken ?? "" };

        var panel = new StackPanel { Spacing = 10, MinWidth = 460 };
        panel.Children.Add(name); panel.Children.Add(repo); panel.Children.Add(token);

        if (!await ShowEditor(existing == null ? "Add GitHub connection" : "Edit connection", panel,
                () => !string.IsNullOrWhiteSpace(repo.Text), "Repository is required.")) return null;

        var c = existing ?? new SavedConnection { Kind = "github" };
        c.GithubRepo = repo.Text; c.GithubToken = token.Password;
        c.Name = string.IsNullOrWhiteSpace(name.Text) ? c.DefaultName() : name.Text.Trim();
        return c;
    }

    private async Task<SavedConnection> ShowKustoConnectionDialog(SavedConnection existing)
    {
        var name = new TextBox { Header = "Name (optional)", Text = existing?.Name ?? "" };
        var cluster = new TextBox { Header = "Cluster URI",
            PlaceholderText = "https://<name>.<region>.kusto.windows.net", Text = existing?.KustoCluster ?? "" };
        var auth = new RadioButtons { Header = "Sign-in", MaxColumns = 1 };
        auth.Items.Add("Interactive browser sign-in");
        auth.Items.Add("Azure CLI (az login)");
        auth.Items.Add("Device code");
        auth.SelectedIndex = existing?.KustoAuthMode ?? 0;
        KustoAuthMode Mode() => auth.SelectedIndex switch { 1 => KustoAuthMode.AzureCli, 2 => KustoAuthMode.DeviceCode, _ => KustoAuthMode.Interactive };

        var db = new TextBox { Header = "Default database", PlaceholderText = "Database (type, or Load below)", Text = existing?.KustoDatabase ?? "" };
        var loadBtn = new Button { Content = "Load databases" };
        var dbList = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 140 };
        var status = new TextBlock { FontSize = 12, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"], TextWrapping = TextWrapping.Wrap };
        loadBtn.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(cluster.Text)) { status.Text = "Enter a cluster URI first."; return; }
            loadBtn.IsEnabled = false; var prev = loadBtn.Content; loadBtn.Content = "Loading…"; status.Text = "Connecting…";
            try
            {
                var uri = cluster.Text; var mode = Mode();
                var dbs = await Task.Run(() => KustoLocation.GetDatabases(uri, mode));
                dbList.ItemsSource = dbs;
                status.Text = dbs.Count > 0 ? $"{dbs.Count} databases — click to choose." : "No databases found.";
            }
            catch (Exception ex) { status.Text = "Failed: " + ex.Message; }
            finally { loadBtn.Content = prev; loadBtn.IsEnabled = true; }
        };
        dbList.SelectionChanged += (_, _) => { if (dbList.SelectedItem is string s) db.Text = s; };

        var panel = new StackPanel { Spacing = 10, MinWidth = 480 };
        panel.Children.Add(name); panel.Children.Add(cluster); panel.Children.Add(auth);
        panel.Children.Add(db); panel.Children.Add(loadBtn); panel.Children.Add(dbList); panel.Children.Add(status);

        if (!await ShowEditor(existing == null ? "Add Kusto connection" : "Edit connection", panel,
                () => !string.IsNullOrWhiteSpace(cluster.Text), "Cluster URI is required.")) return null;

        var c = existing ?? new SavedConnection { Kind = "kusto" };
        c.KustoCluster = cluster.Text; c.KustoDatabase = db.Text.Trim(); c.KustoAuthMode = auth.SelectedIndex;
        c.Name = string.IsNullOrWhiteSpace(name.Text) ? c.DefaultName() : name.Text.Trim();
        return c;
    }

    /// <summary>Show a connection editor dialog with a validation gate on the primary button.</summary>
    private async Task<bool> ShowEditor(string title, FrameworkElement content, Func<bool> isValid, string error)
    {
        var errBlock = new TextBlock
        {
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap, Text = error,
        };
        var host = new StackPanel { Spacing = 10 };
        host.Children.Add(content);
        host.Children.Add(errBlock);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer { Content = host, MaxHeight = 560,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 16, 0) },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!isValid()) { errBlock.Visibility = Visibility.Visible; args.Cancel = true; }
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
