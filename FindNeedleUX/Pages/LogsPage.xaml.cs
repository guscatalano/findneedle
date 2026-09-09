using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using FindPluginCore.GlobalConfiguration;
using FindNeedlePluginLib; // For Logger

namespace FindNeedleUX.Pages;

/// <summary>Severity of a diagnostic log line, inferred from its text (the Logger writes plain strings).</summary>
public enum LogSeverity { Info, Warning, Error }

/// <summary>
/// One parsed diagnostic log line: the compact time, the message, and an inferred severity used to colour it.
/// Keeps the full <see cref="Raw"/> line for copy. Brushes are shared static instances (created on the UI
/// thread when the first entry is built) so a large log doesn't allocate a brush per row.
/// </summary>
public sealed class LogEntry
{
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xE7, 0x4C, 0x3C));
    private static readonly Brush WarnBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xE0, 0x8A, 0x00));
    private static readonly Brush NoAccent = new SolidColorBrush(Colors.Transparent);
    private static Brush _infoBrush;
    private static Brush InfoBrush => _infoBrush ??=
        (Application.Current.Resources.TryGetValue("TextFillColorPrimaryBrush", out var b) && b is Brush br)
            ? br : new SolidColorBrush(Colors.Gray);

    public string Raw { get; }
    public string Time { get; }
    public string Message { get; }
    public LogSeverity Severity { get; }
    public Brush MessageBrush => Severity switch
    {
        LogSeverity.Error => ErrorBrush,
        LogSeverity.Warning => WarnBrush,
        _ => InfoBrush,
    };
    public Brush AccentBrush => Severity switch
    {
        LogSeverity.Error => ErrorBrush,
        LogSeverity.Warning => WarnBrush,
        _ => NoAccent,
    };

    public LogEntry(string raw)
    {
        Raw = raw ?? "";
        string time = "", msg = Raw;
        // The Logger writes "[yyyy-MM-dd HH:mm:ss] message". Show just the time; keep the message.
        if (Raw.StartsWith("[", StringComparison.Ordinal))
        {
            int end = Raw.IndexOf(']');
            if (end > 1)
            {
                var stamp = Raw.Substring(1, end - 1);
                int sp = stamp.LastIndexOf(' ');
                time = sp >= 0 ? stamp.Substring(sp + 1) : stamp;
                msg = Raw.Substring(end + 1).TrimStart();
            }
        }
        Time = time;
        Message = msg;
        Severity = Classify(msg);
    }

    private static LogSeverity Classify(string m)
    {
        if (string.IsNullOrEmpty(m)) return LogSeverity.Info;
        var l = m.ToLowerInvariant();
        if (m.Contains("UNHANDLED", StringComparison.Ordinal)
            || l.Contains("error") || l.Contains("exception") || l.Contains("failed") || l.Contains("fatal"))
            return LogSeverity.Error;
        if (l.Contains("warning") || l.Contains("warn"))
            return LogSeverity.Warning;
        return LogSeverity.Info;
    }
}

/// <summary>
/// Diagnostic-log viewer: severity-coloured entries with a live text filter, a level filter, follow-tail, and
/// copy/clear. Shows the app's OWN log (Logger), not the logs being analysed.
/// </summary>
public sealed partial class LogsPage : Page
{
    private readonly List<LogEntry> _all = new();          // every entry, unfiltered
    public ObservableCollection<LogEntry> LogLines { get; } = new();  // the filtered view bound to the list

    private string _filter = "";
    private LogSeverity _minLevel = LogSeverity.Info;
    // Guards the filter/follow handlers: the ComboBox's SelectedIndex="0" raises SelectionChanged DURING
    // InitializeComponent — before FollowButton and the rest exist — so the handlers must no-op until the
    // constructor has finished wiring everything (then it applies the filter once itself).
    private bool _ready;

    public LogsPage()
    {
        InitializeComponent();
        LogListView.ItemsSource = LogLines;
        foreach (var line in Logger.Instance.LogCache)
            _all.Add(new LogEntry(line));
        Logger.Instance.LogCallback = AddLogLine;
        DebugToggleSwitch.IsOn = GlobalSettings.Debug;
        UpdateDebugStatusText();
        _ready = true;
        ApplyFilter();
    }

    private bool Passes(LogEntry e)
        => e.Severity >= _minLevel
           && (_filter.Length == 0 || e.Raw.Contains(_filter, StringComparison.OrdinalIgnoreCase));

    private void ApplyFilter()
    {
        LogLines.Clear();
        foreach (var e in _all)
            if (Passes(e)) LogLines.Add(e);
        UpdateStatus();
        if (FollowButton.IsChecked == true) ScrollToEnd();
    }

    public void AddLogLine(string line)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => AddLogLine(line));
            return;
        }
        var e = new LogEntry(line);
        _all.Add(e);
        if (Passes(e))
        {
            LogLines.Add(e);
            if (FollowButton.IsChecked == true) ScrollToEnd();
        }
        UpdateStatus();
    }

    private void ScrollToEnd()
    {
        if (LogLines.Count == 0) return;
        try { LogListView.ScrollIntoView(LogLines[LogLines.Count - 1]); } catch { /* best-effort scroll */ }
    }

    private void UpdateStatus()
    {
        CountText.Text = _all.Count == LogLines.Count
            ? $"{_all.Count} entries"
            : $"{LogLines.Count} of {_all.Count} entries";
        EmptyLogsText.Visibility = LogLines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        _filter = SearchBox.Text ?? "";
        ApplyFilter();
    }

    private void LevelFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _minLevel = LevelFilter.SelectedIndex switch
        {
            1 => LogSeverity.Warning,
            2 => LogSeverity.Error,
            _ => LogSeverity.Info,
        };
        ApplyFilter();
    }

    private void Follow_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (FollowButton.IsChecked == true) ScrollToEnd();
    }

    private void DebugToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        GlobalSettings.ToggleDebug();
        UpdateDebugStatusText();
        Logger.Instance.Log($"Debug logging toggled: {GlobalSettings.Debug}");
    }

    private void UpdateDebugStatusText()
        => DebugStatusText.Text = GlobalSettings.Debug ? "on" : "off";

    private void CopySelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = LogListView.SelectedItems.Cast<LogEntry>().Select(x => x.Raw).ToList();
        if (selected.Count == 0)
        {
            Logger.Instance.Log("No log lines selected to copy");
            return;
        }
        CopyToClipboard(string.Join(Environment.NewLine, selected));
        Logger.Instance.Log($"Copied {selected.Count} log lines to clipboard");
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        if (LogLines.Count == 0)
        {
            Logger.Instance.Log("No log lines to copy");
            return;
        }
        // Copy what's currently shown (respects the active filter), full raw lines.
        CopyToClipboard(string.Join(Environment.NewLine, LogLines.Select(x => x.Raw)));
        Logger.Instance.Log($"Copied {LogLines.Count} log lines to clipboard");
    }

    private static void CopyToClipboard(string text)
    {
        try
        {
            var pkg = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            pkg.SetText(text);
            Clipboard.SetContent(pkg);
            Clipboard.Flush(); // persists after the app closes
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"Failed to copy to clipboard: {ex.Message}");
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (LogLines.Count == 0)
        {
            Logger.Instance.Log("No log lines to save");
            return;
        }
        try
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(FindNeedleUX.WindowUtil.GetMainWindow());
            var name = $"findneedle-app-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
            var path = FindNeedleUX.Services.Win32FileDialog.SaveFile(
                hWnd, name, new (string, string)[] { ("Text file", "*.txt") }, ".txt");
            if (path == null) return; // user cancelled
            // Saves what's currently shown (respects the active filter); full raw lines.
            System.IO.File.WriteAllLines(path, LogLines.Select(x => x.Raw));
            Logger.Instance.Log($"Saved {LogLines.Count} log lines to {path}");
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { /* reveal is best-effort */ }
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"Failed to save log: {ex.Message}");
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _all.Clear();
        LogLines.Clear();
        UpdateStatus();
        Logger.Instance.Log("Log view cleared");
    }
}
