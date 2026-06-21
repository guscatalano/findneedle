using System;
using System.Threading.Tasks;
using FindNeedleUX.Services;
using FindNeedleUX.Utils;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using System.Diagnostics;
using WinRT.Interop;

namespace FindNeedleUX.Pages;

/// <summary>The "Log Finder": a catalog of apps/components and where their logs live. Built-in common
/// Windows locations plus the user's own. "Open" loads the folder/file as a location and runs it.</summary>
public sealed partial class LogFinderPage : Page
{
    public LogFinderPage()
    {
        this.InitializeComponent();
        Loaded += (_, _) => RenderList();
    }

    private void RenderList()
    {
        ListHost.Children.Clear();
        bool showHidden = ShowHiddenCheck?.IsChecked == true;
        foreach (var entry in LogCatalog.GetAll(includeHidden: showHidden))
            ListHost.Children.Add(BuildRow(entry));
    }

    private void ShowHidden_Changed(object sender, RoutedEventArgs e) => RenderList();

    private FrameworkElement BuildRow(LogCatalogEntry e)
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

        var icon = new SymbolIcon { Symbol = e.IsFolder ? Symbol.Folder : Symbol.Document, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(icon, 0); grid.Children.Add(icon);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        bool hidden = e.BuiltIn && LogCatalog.IsHiddenBuiltIn(e.Id);
        if (hidden) card.Opacity = 0.55;
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleRow.Children.Add(new TextBlock { Text = e.Name, FontWeight = FontWeights.SemiBold });
        if (e.BuiltIn) titleRow.Children.Add(new TextBlock { Text = "built-in", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Foreground = Dim() });
        if (hidden) titleRow.Children.Add(new TextBlock { Text = "· hidden", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Foreground = Dim() });
        if (!e.Exists) titleRow.Children.Add(new TextBlock { Text = "· not found on this machine", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"] });
        text.Children.Add(titleRow);
        if (!string.IsNullOrWhiteSpace(e.Description))
            text.Children.Add(new TextBlock { Text = e.Description, FontSize = 12, Foreground = Dim() });
        text.Children.Add(new TextBlock { Text = e.ExpandedPath, FontSize = 11, Foreground = Dim(), TextTrimming = TextTrimming.CharacterEllipsis, IsTextSelectionEnabled = true });
        Grid.SetColumn(text, 1); grid.Children.Add(text);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var open = new Button { Content = WithIcon(e.IsFolder ? Symbol.OpenLocal : Symbol.OpenFile, e.IsFolder ? "Open folder" : "Open file"), IsEnabled = e.Exists };
        open.Click += (_, _) => OpenEntry(e);
        actions.Children.Add(open);
        var reveal = new Button { Content = new SymbolIcon { Symbol = Symbol.View }, IsEnabled = e.Exists };
        ToolTipService.SetToolTip(reveal, "Reveal in Explorer");
        reveal.Click += (_, _) => Reveal(e);
        actions.Children.Add(reveal);
        if (e.BuiltIn)
        {
            // Built-ins can't be removed — only hidden (shown again via "Show hidden built-ins").
            var hide = new Button { Content = hidden ? "Unhide" : "Hide" };
            hide.Click += (_, _) => { LogCatalog.SetBuiltInHidden(e.Id, !hidden); RenderList(); };
            actions.Children.Add(hide);
        }
        else
        {
            var edit = new Button { Content = "Edit" };
            edit.Click += async (_, _) => { var u = await ShowEntryDialog(e); if (u != null) { LogCatalog.Upsert(u); RenderList(); } };
            actions.Children.Add(edit);
            var remove = new Button { Content = "Remove" };
            remove.Click += (_, _) => { LogCatalog.Remove(e.Id); RenderList(); };
            actions.Children.Add(remove);
        }
        Grid.SetColumn(actions, 2); grid.Children.Add(actions);

        card.Child = grid;
        return card;
    }

    private static Brush Dim() => (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

    private static StackPanel WithIcon(Symbol s, string text)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        sp.Children.Add(new SymbolIcon { Symbol = s });
        sp.Children.Add(new TextBlock { Text = text });
        return sp;
    }

    /// <summary>Load this catalog entry's folder/file as a fresh search and open the results.</summary>
    private void OpenEntry(LogCatalogEntry e)
    {
        var path = e.ExpandedPath;
        if (string.IsNullOrWhiteSpace(path)) return;
        MiddleLayerService.NewWorkspace();
        MiddleLayerService.AddFolderLocation(path); // handles both a folder and a single file path
        (WindowUtil.GetMainWindow() as MainWindow)?.RunAndViewResults();
    }

    private static void Reveal(LogCatalogEntry e)
    {
        try
        {
            var p = e.ExpandedPath;
            if (e.IsFolder) Process.Start(new ProcessStartInfo { FileName = p, UseShellExecute = true });
            else Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{p}\"", UseShellExecute = true });
        }
        catch { }
    }

    private async void Button_AddEntry(object sender, RoutedEventArgs e)
    {
        var entry = await ShowEntryDialog(null);
        if (entry != null) { LogCatalog.Upsert(entry); RenderList(); }
    }

    /// <summary>Add/edit dialog: name, kind (folder/file), path with a Browse button, description.</summary>
    private async Task<LogCatalogEntry> ShowEntryDialog(LogCatalogEntry existing)
    {
        var name = new TextBox { Header = "Name", PlaceholderText = "e.g. MyApp logs", Text = existing?.Name ?? "" };
        var kind = new RadioButtons { Header = "These logs are a…", MaxColumns = 2 };
        kind.Items.Add("Folder");
        kind.Items.Add("Single file");
        kind.SelectedIndex = existing != null && !existing.IsFolder ? 1 : 0;

        var pathBox = new TextBox { Header = "Path (environment variables OK, e.g. %LOCALAPPDATA%\\MyApp)", Text = existing?.Path ?? "" };
        var browse = new Button { Content = "Browse…", VerticalAlignment = VerticalAlignment.Bottom };
        browse.Click += (_, _) =>
        {
            var hWnd = WindowNative.GetWindowHandle(WindowUtil.GetWindowForElement(this));
            string picked = kind.SelectedIndex == 1
                ? Win32FileDialog.OpenFile(hWnd, new (string, string)[] { ("All files", "*.*") })
                : Win32FileDialog.PickFolder(hWnd);
            if (!string.IsNullOrEmpty(picked)) pathBox.Text = picked;
        };
        var pathRow = new Grid { ColumnSpacing = 8 };
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(pathBox, 0); Grid.SetColumn(browse, 1);
        pathRow.Children.Add(pathBox); pathRow.Children.Add(browse);

        var desc = new TextBox { Header = "Description (optional)", Text = existing?.Description ?? "" };
        var error = new TextBlock { Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"], Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap };

        var panel = new StackPanel { Spacing = 10, MinWidth = 480 };
        panel.Children.Add(name); panel.Children.Add(kind); panel.Children.Add(pathRow); panel.Children.Add(desc); panel.Children.Add(error);

        var dialog = new ContentDialog
        {
            Title = existing == null ? "Add log location" : "Edit log location",
            Content = new ScrollViewer { Content = panel, MaxHeight = 520, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 16, 0) },
            PrimaryButtonText = existing == null ? "Add" : "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(pathBox.Text))
            { error.Text = "Name and path are required."; error.Visibility = Visibility.Visible; args.Cancel = true; }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;

        var result = existing ?? new LogCatalogEntry();
        result.Name = name.Text.Trim();
        result.Path = pathBox.Text.Trim();
        result.Kind = kind.SelectedIndex == 1 ? "file" : "folder";
        result.Description = desc.Text?.Trim() ?? "";
        return result;
    }
}
