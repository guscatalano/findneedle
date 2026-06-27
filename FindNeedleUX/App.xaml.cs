// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using FindPluginCore;
using FindNeedleUX.Services;
using FindNeedlePluginLib;

namespace FindNeedleUX;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        this.InitializeComponent();

        // Capture any unhandled UI exception with its full stack (the normal Logger doesn't see UI-
        // thread crashes), and keep the session alive — a transient render/collection exception
        // shouldn't tear down a log-viewing session. The logged stack is how we diagnose such crashes.
        this.UnhandledException += (s, e) =>
        {
            try { Logger.Instance.Log($"UNHANDLED EXCEPTION: {e.Message}\n{e.Exception}"); } catch { }
            e.Handled = true;
        };

        Logger.Instance.Log("Application launched");
        // Precompute system info at app startup
        _ = SystemInfoMiddleware.GetPanelText();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        m_window = new MainWindow();
        m_window.Activate();

        // Start the in-app MCP server if the user enabled it (localhost-only; off by default).
        try { FindNeedleUX.Services.Mcp.McpServerHost.Initialize(); }
        catch (Exception ex) { Logger.Instance.Log($"MCP host init failed: {ex.Message}"); }

        // Feed the user's WPP TMF search path to tracefmt (via TRACE_FORMAT_SEARCH_PATH) so WPP ETLs
        // decode without the user setting the env var by hand. Re-apply when settings change.
        try
        {
            FindNeedleUX.Services.TraceFormatConfig.Apply();
            FindNeedleUX.Services.ResultsViewerSettings.Changed += FindNeedleUX.Services.TraceFormatConfig.Apply;
        }
        catch (Exception ex) { Logger.Instance.Log($"TraceFormat config init failed: {ex.Message}"); }

        // Disk hygiene: the result cache had no eviction (it grew into the hundreds of GB) and a
        // killed/crashed run leaks its %Temp% extraction dir. Prune both on startup, off the UI thread,
        // and log the outcome so we can prove the cache stays bounded.
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                long cacheFreed = FindNeedleCoreUtils.CachedStorage.Prune(FindNeedleCoreUtils.CachedStorage.DefaultMaxCacheBytes);
                long tempFreed = FindNeedleCoreUtils.TempStorage.CleanupStaleSessions(TimeSpan.FromHours(2));
                var (files, bytes) = FindNeedleCoreUtils.CachedStorage.GetCacheStats();
                FindPluginCore.Diagnostics.PerfLog.Log("cache.maintenance",
                    ("cache_freed_mb", cacheFreed / (1024 * 1024)), ("temp_freed_mb", tempFreed / (1024 * 1024)),
                    ("cache_files", files), ("cache_mb", bytes / (1024 * 1024)));
                Logger.Instance.Log($"Cache maintenance: freed {cacheFreed / (1024 * 1024)} MB cache + " +
                    $"{tempFreed / (1024 * 1024)} MB temp; cache now {files} files / {bytes / (1024 * 1024)} MB");
            }
            catch (Exception ex) { Logger.Instance.Log($"Cache maintenance failed: {ex.Message}"); }
        });

        // GUI equivalent of the findneedle.exe CLI: if a log file/folder was passed on the
        // command line, load it, run the search, and open straight to the viewer — no file
        // picker. Usage: FindNeedleUX.exe "C:\path\log.etl" [--rules=rules.json] [--viewer=native|web]
        // Also lets the FlaUI UI tests drive the real load→search→grid pipeline deterministically.
        try
        {
            var cmd = Environment.GetCommandLineArgs();
            if (cmd != null && cmd.Length > 1)
            {
                ((MainWindow)m_window).LoadFromCommandLine(cmd.Skip(1).ToArray());
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"CLI argument handling failed: {ex.Message}");
        }

        // File activation ("Open with → Find Needle"): unlike a command-line launch, the file path does
        // NOT arrive via argv — it's carried on the activation args. Read it and open it.
        try
        {
            var activated = AppInstance.GetCurrent().GetActivatedEventArgs();
            if (activated?.Kind == ExtendedActivationKind.File)
                OpenActivatedFiles(activated);
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"File activation handling failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Handle an activation redirected from a second instance (e.g. a second "Open with → Find Needle"):
    /// bring the existing window forward and open the file in it. Called by <c>Program.OnActivated</c> on
    /// a background thread, so marshal to the UI thread first.
    /// </summary>
    public void HandleActivation(AppActivationArguments args)
    {
        var window = m_window;
        if (window == null) return;
        window.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                window.Activate(); // bring the existing window to the foreground
                OpenActivatedFiles(args);
            }
            catch (Exception ex) { Logger.Instance.Log($"Redirected activation failed: {ex.Message}"); }
        });
    }

    /// <summary>Open the first supported file carried by a File/Launch activation in the main window.</summary>
    private void OpenActivatedFiles(AppActivationArguments args)
    {
        if (m_window is not MainWindow mw) return;
        foreach (var path in ExtractPaths(args))
        {
            _ = mw.OpenPathAsync(path);
            break; // one workspace per window — open the first file
        }
    }

    /// <summary>Pull openable file paths out of a File activation (Explorer "Open with") or a Launch
    /// activation (command line). Only existing files are yielded.</summary>
    private static IEnumerable<string> ExtractPaths(AppActivationArguments args)
    {
        if (args == null) yield break;

        if (args.Kind == ExtendedActivationKind.File
            && args.Data is global::Windows.ApplicationModel.Activation.IFileActivatedEventArgs fileArgs)
        {
            foreach (var item in fileArgs.Files)
            {
                var p = item?.Path;
                if (!string.IsNullOrWhiteSpace(p) && System.IO.File.Exists(p)) yield return p;
            }
        }
        else if (args.Kind == ExtendedActivationKind.Launch
            && args.Data is global::Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launchArgs)
        {
            var arguments = launchArgs.Arguments;
            if (string.IsNullOrWhiteSpace(arguments)) yield break;

            // For a command-line / right-click ("Open in Find Needle") launch, Arguments usually begins
            // with this exe's OWN path (argv[0]). Taking the first existing path blindly would open the
            // app's .exe as a "log" (0 rows). Tokenize quote-aware, skip our own exe + flags, yield files.
            var self = Environment.ProcessPath ?? "";
            foreach (var token in TokenizeArgs(arguments))
            {
                var t = token.Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(t) || t.StartsWith("--")) continue;
                if (!string.IsNullOrEmpty(self) && string.Equals(t, self, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(System.IO.Path.GetFileName(t), "FindNeedleUX.exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (System.IO.File.Exists(t) || System.IO.Directory.Exists(t)) yield return t;
            }
        }
    }

    /// <summary>Split a command-line string into tokens, honoring double-quoted segments so paths with
    /// spaces stay intact (a naive space-split would break "C:\Program Files\…").</summary>
    private static IEnumerable<string> TokenizeArgs(string s)
    {
        var sb = new System.Text.StringBuilder();
        bool inQuotes = false;
        foreach (var ch in s)
        {
            if (ch == '"') { inQuotes = !inQuotes; continue; }
            if (ch == ' ' && !inQuotes)
            {
                if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
                continue;
            }
            sb.Append(ch);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    private Window m_window;
}
