using System;
using System.Threading.Tasks;
using FindPluginCore.Searching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FindNeedleUX.Services;

/// <summary>
/// Bridge between <see cref="NuSearchQuery"/>'s synchronous "should I reuse the cache?"
/// callback and a ContentDialog on the UI thread. The query runs on a threadpool task;
/// this service marshals to the main window's dispatcher, shows the dialog, and blocks
/// the search thread on a <see cref="TaskCompletionSource{Boolean}"/> until the user picks.
/// </summary>
public static class CacheReusePromptService
{
    /// <summary>
    /// Show the prompt and wait for the user's answer. Called from the search background
    /// thread; safe to block. Returns true if the user wants to reuse the cache, false to
    /// rescan. If the dialog can't be shown (no main window, dispatcher gone), defaults to
    /// true — better to be quick and let the user re-run if needed than to waste a scan.
    /// </summary>
    public static bool Prompt(CacheReusePromptInfo info)
    {
        if (info == null) return true;

        var mainWindow = WindowUtil.GetMainWindow();
        if (mainWindow == null) return true;
        var dispatcher = mainWindow.DispatcherQueue;
        var xamlRoot = (mainWindow.Content as FrameworkElement)?.XamlRoot;
        if (dispatcher == null || xamlRoot == null) return true;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool enqueued = dispatcher.TryEnqueue(async () =>
        {
            try
            {
                var dialog = BuildDialog(info, xamlRoot);
                var result = await dialog.ShowAsync();
                tcs.TrySetResult(result == ContentDialogResult.Primary);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Cache reuse prompt failed: {ex.Message}");
                tcs.TrySetResult(true);
            }
        });
        if (!enqueued)
        {
            // Dispatcher refused — default to reuse.
            return true;
        }

        try { return tcs.Task.GetAwaiter().GetResult(); }
        catch { return true; }
    }

    private static ContentDialog BuildDialog(CacheReusePromptInfo info, XamlRoot xamlRoot)
    {
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock
        {
            Text = "You've searched this file before. Opening the previous results skips the search and shows them right away; rescanning re-reads the file so it's fully up to date.",
            TextWrapping = TextWrapping.Wrap,
        });

        var details = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 12,
            Text = FormatDetails(info),
        };
        body.Children.Add(details);

        return new ContentDialog
        {
            Title = "Open previous results?",
            Content = body,
            PrimaryButtonText = "Open previous (fast)",
            SecondaryButtonText = "Rescan now (up to date)",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };
    }

    private static string FormatDetails(CacheReusePromptInfo info)
    {
        string ageText = "";
        if (info.CacheCompletedAtUtc.HasValue)
        {
            var age = DateTime.UtcNow - info.CacheCompletedAtUtc.Value;
            ageText = age.TotalSeconds < 60 ? "just now"
                    : age.TotalMinutes < 60 ? $"{(int)age.TotalMinutes} min ago"
                    : age.TotalHours < 24   ? $"{(int)age.TotalHours} hr ago"
                    : $"{(int)age.TotalDays} day(s) ago";
        }

        var lines = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(info.SourceFilePath))
            lines.AppendLine("File:   " + ShortPath(info.SourceFilePath));
        if (info.CachedRowCount > 0)
            lines.AppendLine("Rows:   " + info.CachedRowCount.ToString("N0"));
        if (!string.IsNullOrEmpty(ageText))
            lines.AppendLine("Cached: " + ageText);
        if (info.SourceFileSize > 0)
            lines.AppendLine("Size:   " + FormatSize(info.SourceFileSize));
        return lines.ToString().TrimEnd();
    }

    private static string ShortPath(string p)
    {
        if (string.IsNullOrEmpty(p)) return "";
        // Show the last 2 segments so the user knows which file without wrapping a full path.
        var i = p.LastIndexOfAny(new[] { '\\', '/' });
        if (i <= 0) return p;
        var j = p.LastIndexOfAny(new[] { '\\', '/' }, i - 1);
        return j <= 0 ? p : "…" + p.Substring(j);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024L * 1024) return (bytes / 1024.0).ToString("F1") + " KB";
        if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024.0).ToString("F1") + " MB";
        return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("F2") + " GB";
    }
}
