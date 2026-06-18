using System;
using FindNeedleUX.Services;

namespace FindNeedleUX.Services.Mcp;

/// <summary>
/// Owns the lifecycle of the in-app <see cref="McpServer"/>, driven by
/// <see cref="ResultsViewerSettings.McpServerEnabled"/> / <see cref="ResultsViewerSettings.McpServerPort"/>.
/// Call <see cref="Initialize"/> once at app startup; it starts the server if enabled and keeps it in
/// sync whenever the settings change (toggle / port edit broadcast <c>Changed</c>).
/// </summary>
public static class McpServerHost
{
    private static readonly object _sync = new();
    private static McpServer _server;
    private static bool _initialized;
    private static int _runningPort;

    /// <summary>Last status/diagnostic line (shown on the settings page).</summary>
    public static string Status { get; private set; } = "Stopped";

    public static event Action StatusChanged;

    public static void Initialize()
    {
        lock (_sync)
        {
            if (_initialized) return;
            _initialized = true;
            ResultsViewerSettings.Changed += ApplyFromSettings;
        }
        ApplyFromSettings();
    }

    public static void ApplyFromSettings()
    {
        bool enabled = ResultsViewerSettings.McpServerEnabled;
        int port = ResultsViewerSettings.McpServerPort;

        lock (_sync)
        {
            bool running = _server?.IsRunning == true;

            if (!enabled)
            {
                if (running) StopLocked();
                return;
            }

            // Enabled: (re)start if not running or the port changed.
            if (running && _runningPort == port) return;
            if (running) StopLocked();
            StartLocked(port);
        }
    }

    private static void StartLocked(int port)
    {
        try
        {
            _server = new McpServer { Log = SetStatus };
            _server.Start(port);
            _runningPort = port;
            SetStatus($"Listening on http://127.0.0.1:{port}/");
        }
        catch (Exception ex)
        {
            _server = null;
            SetStatus($"Failed to start on port {port}: {ex.Message}");
        }
    }

    private static void StopLocked()
    {
        try { _server?.Stop(); } catch { }
        _server = null;
        _runningPort = 0;
        SetStatus("Stopped");
    }

    private static void SetStatus(string s)
    {
        Status = s;
        try { StatusChanged?.Invoke(); } catch { }
    }
}
