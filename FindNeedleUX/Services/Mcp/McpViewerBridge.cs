using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FindNeedlePluginLib.Interfaces;

namespace FindNeedleUX.Services.Mcp;

/// <summary>
/// Single entry point the MCP host calls to read and drive the app's one live workspace — the same
/// locations, search query, and active result viewer the user sees. There is no separate "session":
/// agent actions mutate the shared GUI state and show up immediately.
///
/// Two kinds of operation:
///   * Workspace (locations / rules / run search) go through <see cref="MiddleLayerService"/>,
///     marshaled onto the UI thread.
///   * Viewer (filter / sort / page / record / tag / select) are delegated to the registered
///     <see cref="IMcpViewerController"/> (the active <c>NativeResultsPage</c>). When no viewer is
///     registered these throw <see cref="McpNoViewerException"/> so the host can report
///     "no active view".
/// </summary>
public sealed class McpViewerBridge
{
    public static McpViewerBridge Instance { get; } = new();

    private McpViewerBridge() { }

    private IMcpViewerController _controller;
    private readonly object _sync = new();

    /// <summary>
    /// UI-thread dispatcher used to marshal workspace mutations. Set once at app startup (MainWindow)
    /// so workspace tools work even before any viewer is open.
    /// </summary>
    public Microsoft.UI.Dispatching.DispatcherQueue UiDispatcher { get; set; }

    /// <summary>Port the MCP server is listening on (0 if stopped). Set by <c>McpServerHost</c>.</summary>
    public int ServerPort { get; set; }

    /// <summary>
    /// Optional handler that runs a search the same way the Search button does (run + navigate to the
    /// viewer). Wired by the search page. When unset, <see cref="RunSearchAsync"/> falls back to a
    /// direct streaming search via <see cref="MiddleLayerService"/>.
    /// </summary>
    public Func<Task> RunSearchHandler { get; set; }

    // ----- viewer registration -----

    public void RegisterViewer(IMcpViewerController controller)
    {
        lock (_sync) _controller = controller;
    }

    public void UnregisterViewer(IMcpViewerController controller)
    {
        lock (_sync) { if (ReferenceEquals(_controller, controller)) _controller = null; }
    }

    public bool HasViewer { get { lock (_sync) return _controller != null; } }

    /// <summary>
    /// Wait up to <paramref name="timeoutMs"/> for a result viewer to register (e.g. just after app
    /// launch or a search that opens the viewer). Returns true if one is registered by then. Lets an
    /// agent avoid the "no viewer" race without polling.
    /// </summary>
    public async Task<bool> WaitForViewerAsync(int timeoutMs)
    {
        if (timeoutMs <= 0) timeoutMs = 10_000;
        if (timeoutMs > 120_000) timeoutMs = 120_000;
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!HasViewer && Environment.TickCount64 < deadline)
            await Task.Delay(100).ConfigureAwait(false);
        return HasViewer;
    }

    /// <summary>Health/orientation snapshot. Never throws on the no-viewer path.</summary>
    public async Task<StatusDto> GetStatusAsync()
    {
        var dto = new StatusDto
        {
            ServerRunning = ServerPort > 0,
            Port = ServerPort,
            HasViewer = HasViewer,
            Locations = await RunOnUiAsync(() => MiddleLayerService.Locations.Count).ConfigureAwait(false),
        };
        if (dto.HasViewer)
        {
            try { var v = await GetViewAsync().ConfigureAwait(false); dto.Total = v.Total; dto.TotalFiltered = v.TotalFiltered; }
            catch (McpNoViewerException) { dto.HasViewer = false; } // viewer closed between the check and the call
        }
        return dto;
    }

    private IMcpViewerController Viewer
    {
        get
        {
            lock (_sync)
            {
                return _controller ?? throw new McpNoViewerException(
                    "No result viewer is open. Run a search to open one before using viewer tools.");
            }
        }
    }

    // ----- viewer ops (delegated) -----

    public Task<ViewStateDto> GetViewAsync() => Viewer.GetViewAsync();
    public Task<PageDto> GetPageAsync(int? offset, int limit) => Viewer.GetPageAsync(offset, limit);
    public Task<RecordDto> GetRecordAsync(long rowId) => Viewer.GetRecordAsync(rowId);
    public Task<SummaryDto> GetSummaryAsync() => Viewer.GetSummaryAsync();
    public Task<List<HistogramBucketDto>> GetHistogramAsync(int buckets) => Viewer.GetHistogramAsync(buckets);
    public Task<LogAnalysis.FacetResult> GetFacetsAsync(string field, int limit, int sampleCap) => Viewer.GetFacetsAsync(field, limit, sampleCap);
    public Task<LogAnalysis.PatternResult> GetTopPatternsAsync(int limit, int sampleCap) => Viewer.GetTopPatternsAsync(limit, sampleCap);
    public Task<int> SetFilterAsync(string search, string provider, string taskName, string message,
        string source, string level, string fromTime, string toTime)
        => Viewer.SetFilterAsync(search, provider, taskName, message, source, level, fromTime, toTime);
    public Task<int> ClearFiltersAsync() => Viewer.ClearFiltersAsync();
    public Task SetSortAsync(string column, bool descending) => Viewer.SetSortAsync(column, descending);
    public Task GoToPageAsync(int page) => Viewer.GoToPageAsync(page);
    public Task SetPageSizeAsync(int pageSize) => Viewer.SetPageSizeAsync(pageSize);
    public Task<bool> SelectRowAsync(long rowId) => Viewer.SelectRowAsync(rowId);
    public Task<bool> TagRowAsync(long rowId, string tag, string text) => Viewer.TagRowAsync(rowId, tag, text);
    public Task<bool> ClearTagAsync(long rowId) => Viewer.ClearTagAsync(rowId);
    public Task SetDetailsModeAsync(string mode) => Viewer.SetDetailsModeAsync(mode);
    public Task<ExportResultDto> ExportAsync(string format, string destPath) => Viewer.ExportAsync(format, destPath);

    // ----- workspace ops (MiddleLayerService, marshaled to the UI thread) -----

    public Task<List<LocationDto>> ListLocationsAsync() => RunOnUiAsync(() =>
    {
        var list = new List<LocationDto>();
        foreach (var loc in MiddleLayerService.Locations)
        {
            string name, desc;
            try { name = loc.GetName(); } catch { name = "(unknown)"; }
            try { desc = loc.GetDescription(); } catch { desc = ""; }
            list.Add(new LocationDto
            {
                Name = name,
                Description = desc,
                IsEditable = loc is KustoPlugin.Location.KustoLocation,
            });
        }
        return list;
    });

    public Task<List<string>> ListRulesAsync() => RunOnUiAsync(() =>
        MiddleLayerService.SearchQueryUX?.CurrentQuery?.RulesConfigPaths is { } r
            ? new List<string>(r) : new List<string>());

    public Task AddFolderAsync(string path) => RunOnUiAsync(() =>
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required");
        MiddleLayerService.AddFolderLocation(path);
    });

    public Task AddKustoAsync(string cluster, string database, string kql, string authMode, long rowLimit)
        => RunOnUiAsync(() =>
    {
        if (string.IsNullOrWhiteSpace(cluster))  throw new ArgumentException("cluster is required");
        if (string.IsNullOrWhiteSpace(database)) throw new ArgumentException("database is required");
        if (string.IsNullOrWhiteSpace(kql))      throw new ArgumentException("query (kql) is required");
        var mode = Enum.TryParse<KustoPlugin.Location.KustoAuthMode>(authMode, ignoreCase: true, out var m)
            ? m : KustoPlugin.Location.KustoAuthMode.Interactive;
        MiddleLayerService.AddLocation(new KustoPlugin.Location.KustoLocation(cluster, database, kql, mode, rowLimit));
    });

    public Task<bool> RemoveLocationAsync(string name) => RunOnUiAsync(() =>
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        int before = MiddleLayerService.Locations.Count;
        MiddleLayerService.RemoveLocationByName(name);
        return MiddleLayerService.Locations.Count < before;
    });

    public async Task RunSearchAsync()
    {
        var handler = RunSearchHandler;
        if (handler != null) { await handler().ConfigureAwait(false); }
        else
        {
            // Fallback: trigger a streaming search directly (no navigation). The active viewer, if
            // any, picks up the new results on its next load.
            await RunOnUiAsync(() => { MiddleLayerService.RunSearchStreaming(); }).ConfigureAwait(false);
        }
        // If a viewer isn't up yet (e.g. it's opening as part of this search), give it a moment to
        // register so a follow-up viewer tool doesn't hit the "no viewer" race. No-op if one's open.
        if (!HasViewer) await WaitForViewerAsync(8_000).ConfigureAwait(false);
    }

    public Task CancelSearchAsync() => RunOnUiAsync(() => MiddleLayerService.CurrentStreamingSearch?.Stop());

    // ----- UI-thread marshaling -----

    internal Task RunOnUiAsync(Action action)
    {
        var dq = UiDispatcher;
        if (dq == null || dq.HasThreadAccess) { action(); return Task.CompletedTask; }
        var tcs = new TaskCompletionSource();
        if (!dq.TryEnqueue(() => { try { action(); tcs.TrySetResult(); } catch (Exception ex) { tcs.TrySetException(ex); } }))
            action();
        return tcs.Task;
    }

    internal Task<T> RunOnUiAsync<T>(Func<T> func)
    {
        var dq = UiDispatcher;
        if (dq == null || dq.HasThreadAccess) return Task.FromResult(func());
        var tcs = new TaskCompletionSource<T>();
        if (!dq.TryEnqueue(() => { try { tcs.TrySetResult(func()); } catch (Exception ex) { tcs.TrySetException(ex); } }))
            return Task.FromResult(func());
        return tcs.Task;
    }
}

/// <summary>Thrown by viewer tools when no result viewer is registered with the bridge.</summary>
public sealed class McpNoViewerException : Exception
{
    public McpNoViewerException(string message) : base(message) { }
}
