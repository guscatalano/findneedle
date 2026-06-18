// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

using System;
using System.Linq;
using Microsoft.UI.Xaml;
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
    }

    private Window m_window;
}
