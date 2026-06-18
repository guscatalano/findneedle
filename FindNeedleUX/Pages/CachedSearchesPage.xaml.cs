using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using FindNeedleCoreUtils;
using FindNeedleUX.Services;
using FindPluginCore.Implementations.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FindNeedleUX.Pages;

/// <summary>
/// Lists the on-disk search caches and lets the user open one into the result viewer (without
/// rescanning the source) or delete it. Backed by <see cref="CachedSearchCatalog"/>. Caches with no
/// recorded source (mostly test runs) are hidden by default behind the "Show unnamed" toggle.
/// </summary>
public sealed partial class CachedSearchesPage : Page
{
    public sealed class CachedSearchItem
    {
        public string DbPath { get; set; } = "";
        public string SourcePath { get; set; } = "";
        public string SourceName { get; set; } = "";
        public string Rows { get; set; } = "";
        public string When { get; set; } = "";
        public string Size { get; set; } = "";
        public string Status { get; set; } = "";
        public bool Named { get; set; }
        public bool CanDelete { get; set; } = true; // false while this cache is the one being viewed
    }

    private const int MaxShown = 500;

    private readonly ObservableCollection<CachedSearchItem> _items = new();
    private readonly List<CachedSearchItem> _all = new();
    private int _totalFiles;

    public CachedSearchesPage()
    {
        this.InitializeComponent();
        CacheList.ItemsSource = _items;
        Loaded += (_, _) => Load();
    }

    private async void Load()
    {
        _items.Clear();
        _all.Clear();
        EmptyState.Visibility = Visibility.Collapsed;
        RefreshButton.IsEnabled = false;
        ClearAllButton.IsEnabled = false;
        SubtitleText.Text = "Loading…";

        List<CachedSearchEntry> entries;
        try
        {
            // Opening SQLite files can take a moment on a large cache dir — do it off the UI thread.
            _totalFiles = await System.Threading.Tasks.Task.Run(() => CachedSearchCatalog.CountFiles());
            entries = await System.Threading.Tasks.Task.Run(() => CachedSearchCatalog.List(MaxShown));
        }
        catch (Exception ex)
        {
            SubtitleText.Text = "Failed to read cache: " + ex.Message;
            RefreshButton.IsEnabled = true;
            ClearAllButton.IsEnabled = true;
            return;
        }

        var openPath = MiddleLayerService.OpenCacheDbPath;
        foreach (var e in entries)
        {
            bool named = !string.IsNullOrEmpty(e.SourcePath);
            bool isOpen = !string.IsNullOrEmpty(openPath) && string.Equals(e.DbPath, openPath, StringComparison.OrdinalIgnoreCase);
            _all.Add(new CachedSearchItem
            {
                DbPath = e.DbPath,
                Named = named,
                CanDelete = !isOpen, // can't delete the cache that's currently open (file is locked)
                SourcePath = named ? e.SourcePath : "(no recorded source — likely a test run)",
                SourceName = named ? SafeFileName(e.SourcePath) : "(unnamed cache)",
                Rows = $"{e.Rows:N0} rows",
                When = e.CompletedAt.HasValue ? e.CompletedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "—",
                Size = ByteUtils.BytesToFriendlyString(e.SizeOnDiskBytes),
                Status = string.Join("  ·  ",
                    new[]
                    {
                        isOpen ? "open now" : null,
                        e.FtsBuilt ? "indexed" : null,
                        (named && !e.SourceExists) ? "source missing" : null,
                    }.Where(s => !string.IsNullOrEmpty(s))),
            });
        }

        RefreshButton.IsEnabled = true;
        ClearAllButton.IsEnabled = true;
        Refilter();
    }

    private void Refilter()
    {
        bool showUnnamed = ShowUnnamedCheck.IsChecked == true;
        _items.Clear();
        foreach (var it in _all)
            if (it.Named || showUnnamed) _items.Add(it);

        int named = _all.Count(i => i.Named);
        int unnamed = _all.Count - named;
        EmptyState.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var capped = _totalFiles > _all.Count ? $" (newest {_all.Count:N0} of {_totalFiles:N0} files)" : "";
        SubtitleText.Text = $"{named:N0} named · {unnamed:N0} unnamed{capped} · {CachedSearchCatalog.Directory}";
    }

    private static string SafeFileName(string path)
    {
        try { return Path.GetFileName(path.TrimEnd('\\', '/')); }
        catch { return path; }
    }

    private void ShowUnnamed_Changed(object sender, RoutedEventArgs e)
    {
        if (_all.Count == 0) return; // not loaded yet
        Refilter();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not CachedSearchItem item) return;
        try
        {
            MiddleLayerService.OpenCachedResult(item.DbPath);
            this.Frame?.Navigate(typeof(FindNeedleUX.Pages.NativeResultsPage));
        }
        catch (Exception ex)
        {
            _ = ShowMessageAsync("Couldn't open cache", ex.Message);
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not CachedSearchItem item) return;
        var confirm = new ContentDialog
        {
            Title = "Delete cached search?",
            Content = $"Delete the cache for:\n{item.SourcePath}\n\nThis only removes the cached copy — the original log is untouched.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        try { CachedSearchCatalog.Delete(item.DbPath); }
        catch (Exception ex) { await ShowMessageAsync("Couldn't delete", ex.Message); }
        Load();
    }

    private async void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            Title = "Clear all caches?",
            Content = $"Delete all {_totalFiles:N0} cache files in:\n{CachedSearchCatalog.Directory}\n\n" +
                      "Original logs are untouched. Reopening a file will rescan it (slower the first time).",
            PrimaryButtonText = "Delete all",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        int removed;
        try { removed = await System.Threading.Tasks.Task.Run(() => CachedSearchCatalog.DeleteAll()); }
        catch (Exception ex) { await ShowMessageAsync("Couldn't clear cache", ex.Message); return; }
        await ShowMessageAsync("Cache cleared", $"Removed {removed:N0} cache files.");
        Load();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = CachedSearchCatalog.Directory,
                UseShellExecute = true,
            });
        }
        catch { /* ignore */ }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Load();

    private System.Threading.Tasks.Task ShowMessageAsync(string title, string msg)
        => new ContentDialog { Title = title, Content = msg, CloseButtonText = "OK", XamlRoot = this.XamlRoot }.ShowAsync().AsTask();
}
