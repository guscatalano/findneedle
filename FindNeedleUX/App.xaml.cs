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
    // Startup profiling: a clock that starts as early as managed code runs (this static initializer fires when
    // the App type is first touched, right after the WinUI bootstrapper). Mark() logs elapsed time at each
    // startup milestone to the perf log (phase=startup.*), so "what takes time on launch" is measurable.
    internal static readonly System.Diagnostics.Stopwatch StartupClock = System.Diagnostics.Stopwatch.StartNew();
    internal static void Mark(string phase)
    {
        try { FindPluginCore.Diagnostics.PerfLog.Log("startup." + phase, ("t_ms", StartupClock.ElapsedMilliseconds)); }
        catch { }
    }

    public App()
    {
        // Bootstrapper cost: process start → our first managed line (WinUI/CLR init we can't instrument from here).
        try
        {
            var sinceProc = (DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalMilliseconds;
            FindPluginCore.Diagnostics.PerfLog.Log("startup.app_ctor", ("since_proc_start_ms", (long)sinceProc));
        }
        catch { }
        this.InitializeComponent();
        Mark("app_initcomponent");

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
        Mark("app_ctor_end");
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        Mark("onlaunched_start");
        // Bring forward any settings written by an earlier packaged build (which wrote to raw %LocalAppData%)
        // into the app's own store, BEFORE MainWindow reads any settings. No-op when unpackaged. Best-effort.
        FindNeedleCoreUtils.PackagedAppPaths.MigrateLegacyPerUserState();
        m_window = new MainWindow();
        Mark("mainwindow_created"); // includes InitializeComponent + first WelcomePage navigate
        m_window.Activate();
        Mark("window_activated");

        // Start the in-app MCP server if the user enabled it (localhost-only; off by default).
        try { FindNeedleUX.Services.Mcp.McpServerHost.Initialize(); }
        catch (Exception ex) { Logger.Instance.Log($"MCP host init failed: {ex.Message}"); }
        Mark("mcp_init");

        // Feed the user's WPP TMF search path to tracefmt (via TRACE_FORMAT_SEARCH_PATH) so WPP ETLs
        // decode without the user setting the env var by hand. Re-apply when settings change.
        try
        {
            FindNeedleUX.Services.TraceFormatConfig.Apply();
            FindNeedleUX.Services.ResultsViewerSettings.Changed += FindNeedleUX.Services.TraceFormatConfig.Apply;

            // On-demand WPP symbol provisioning: when a WPP ETL fails to decode for missing TMFs, the ETL
            // processor calls this to resolve them (built-in lookup + the ISymbolResolver plugins), extract
            // TMFs, and retry the decode — so custom resolvers now run on the DECODE path, not only the
            // manual "WPP Symbol Resolution" page. Registered here because ETWPlugin can't reference the UX.
            // The provisioning core moved to FindPluginCore; the UX supplies the user's symbol settings here.
            FindNeedlePluginLib.WppSymbolProvisioning.Handler =
                req => FindPluginCore.Wpp.Symbols.WppSymbolResolver.TryProvision(
                    req, ResultsViewerSettings.SymbolSourcePath, ResultsViewerSettings.SymbolPath);

            // Manual raw-event decoders (IWppEventDecoder): for a provider with no TMF at all, a plugin can
            // format the raw event itself. The managed WPP decoder consults these at the TMF-miss branch.
            // Registered here for the same reason — ETWPlugin can't reference the UX or the plugin subsystem.
            FindNeedlePluginLib.WppEventDecoding.Provider = () =>
                findneedle.PluginSubsystem.PluginManager.GetSingleton()
                    .GetAllPluginsInstancesOfAType<FindNeedlePluginLib.IWppEventDecoder>();
        }
        catch (Exception ex) { Logger.Instance.Log($"TraceFormat config init failed: {ex.Message}"); }
        Mark("traceformat_and_seams");

        // Warm the plugin load (~2s) in the BACKGROUND now that the window is up — it used to run synchronously
        // during MainWindow construction and was ~85% of launch time. Every search entry awaits PluginsReady
        // (with a spinner) so a search that beats the warm shows a spinner rather than freezing. See
        // MiddleLayerService.PluginsReady.
        try { FindNeedleUX.Services.MiddleLayerService.WarmPluginsInBackground(); } catch { }
        Mark("plugins_warm_kicked");

        // Apply the persisted "index timestamps in search" preference to the storage layer before any
        // search runs (default off — see ResultsViewerSettings.IndexTimestampsInSearch).
        try
        {
            FindPluginCore.Implementations.Storage.SqliteStorage.IndexLogTimeInFts =
                FindNeedleUX.Services.ResultsViewerSettings.IndexTimestampsInSearch;
        }
        catch (Exception ex) { Logger.Instance.Log($"Apply IndexTimestampsInSearch failed: {ex.Message}"); }

        // Apply the persisted "parallel fan-out ingest" preference (default on — first-open speed). Off
        // falls back to the serial single-writer insert. See ResultsViewerSettings.ParallelIngest.
        try
        {
            FindPluginCore.Implementations.Storage.SqliteStorage.ParallelIngestEnabled =
                FindNeedleUX.Services.ResultsViewerSettings.ParallelIngest;
        }
        catch (Exception ex) { Logger.Instance.Log($"Apply ParallelIngest failed: {ex.Message}"); }

        // Apply the persisted WPP decoder choice (Auto / Managed / Tracefmt) to the ambient decode option the
        // ETL processor reads, and re-apply on settings change.
        try
        {
            ApplyWppDecoderSetting();
            FindNeedleUX.Services.ResultsViewerSettings.Changed += ApplyWppDecoderSetting;
        }
        catch (Exception ex) { Logger.Instance.Log($"Apply WppDecoder failed: {ex.Message}"); }

        // Apply the persisted "fast bulk ingest" preference (default on — defers secondary indexes to a
        // bulk post-pass + drops AUTOINCREMENT, ~29% faster raw ingest). See ResultsViewerSettings.FastBulkIngest.
        try
        {
            FindPluginCore.Implementations.Storage.SqliteStorage.FastBulkIngest =
                FindNeedleUX.Services.ResultsViewerSettings.FastBulkIngest;
        }
        catch (Exception ex) { Logger.Instance.Log($"Apply FastBulkIngest failed: {ex.Message}"); }

        // Disk hygiene, off the UI thread so it never blocks launch. Two parts, deliberately different:
        //  • Stale %Temp% session dirs from killed/crashed runs are pure garbage → always swept (cheap, and
        //    it keeps temp from leaking unbounded).
        //  • The reopen-cache (cached searches) is the USER's data, and clearing it makes reopens re-scan —
        //    so it's gated by ResultsViewerSettings.StartupCacheCleanup and only touched when it's genuinely
        //    large (≥ CacheMaintenance.ThresholdBytes). "Always" clears it silently, "Ask" prompts after the
        //    window is up (never a startup-blocking dialog), "Never" leaves it alone. This is why launch no
        //    longer churns an 8 GB cache on every start.
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                long tempFreed = FindNeedleCoreUtils.TempStorage.CleanupStaleSessions(TimeSpan.FromHours(2));
                if (tempFreed > 0)
                    Logger.Instance.Log($"Startup: swept {tempFreed / (1024 * 1024)} MB stale temp");

                var mode = FindNeedleUX.Services.ResultsViewerSettings.StartupCacheCleanup; // Ask | Always | Never
                if (string.Equals(mode, "Never", StringComparison.OrdinalIgnoreCase)) return;

                var (files, bytes) = FindNeedleUX.Services.CacheMaintenance.GetStats();
                FindPluginCore.Diagnostics.PerfLog.Log("cache.maintenance",
                    ("temp_freed_mb", tempFreed / (1024 * 1024)), ("cache_files", files),
                    ("cache_mb", bytes / (1024 * 1024)), ("mode", mode));
                if (bytes < FindNeedleUX.Services.CacheMaintenance.ThresholdBytes) return; // modest cache → leave it

                if (string.Equals(mode, "Always", StringComparison.OrdinalIgnoreCase))
                {
                    var (cleared, before) = FindNeedleUX.Services.CacheMaintenance.ClearAllCachedSearches();
                    Logger.Instance.Log($"Startup cache cleanup (Always): cleared {cleared} cached searches " +
                        $"(~{before / (1024 * 1024)} MB)");
                }
                else // Ask — surface a dismissible prompt once the window exists
                {
                    m_window?.DispatcherQueue.TryEnqueue(() =>
                        (m_window as MainWindow)?.ShowCacheCleanupPrompt(files, bytes));
                }
            }
            catch (Exception ex) { Logger.Instance.Log($"Startup cache maintenance failed: {ex.Message}"); }
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

    /// <summary>Map the persisted WPP-decoder string setting to the ambient <see cref="FindNeedlePluginLib.DecodeOptions.WppDecoder"/>.</summary>
    private static void ApplyWppDecoderSetting()
    {
        var mode = FindNeedleUX.Services.ResultsViewerSettings.WppDecoderMode;
        FindNeedlePluginLib.DecodeOptions.WppDecoder = mode switch
        {
            "Managed" => FindNeedlePluginLib.WppDecoder.Managed,
            "Tracefmt" => FindNeedlePluginLib.WppDecoder.Tracefmt,
            "Compare" => FindNeedlePluginLib.WppDecoder.Compare,
            _ => FindNeedlePluginLib.WppDecoder.Auto,
        };
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
