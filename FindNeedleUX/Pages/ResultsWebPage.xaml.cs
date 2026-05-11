using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using FindNeedleUX.Services;
using FindNeedleUX.Services.PagedLogSource;
using FindPluginCore.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;
using Windows.Foundation.Collections;

namespace FindNeedleUX.Pages;
/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class ResultsWebPage : Page
{
    public ResultsWebPage()
    {
        this.InitializeComponent();
        try
        {
            //MyWebView.Source = new Uri("ms-appx-web:///assets/www/index.html");
            Init();
        }
        catch (Exception)
        {

        }
        // Live-apply theme / level-color edits made in the settings page.
        ResultsViewerSettings.Changed += OnViewerSettingsChanged;
    }

    private void OnViewerSettingsChanged()
    {
        // Marshal back to the UI thread; PostWebMessageAsJson must be called on the WebView's
        // dispatcher.
        DispatcherQueue.TryEnqueue(() => SendLevelColorsToWebView());
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        // Dispose and release the WebView2 control when navigating away
        try
        {
            if (MyWebView != null)
            {
                MyWebView.CoreWebView2?.Stop();
                if (MyWebView.CoreWebView2 != null)
                    MyWebView.CoreWebView2.WebMessageReceived -= MessageReceived;
            }
        }
        catch { }
        // Release the paged source built for server-side mode. Native viewer doesn't own
        // the storage, but our source is per-page-instance.
        try { _pagedSource?.Dispose(); } catch { }
        _pagedSource = null;
        // Stop reacting to settings edits — the viewer is gone.
        ResultsViewerSettings.Changed -= OnViewerSettingsChanged;
    }

    private async void Init()
    {
        try
        {
            await MyWebView.EnsureCoreWebView2Async();
            MyWebView.NavigationCompleted += (sender, e) =>
            {
                if (e.IsSuccess == false)
                {
                    Console.WriteLine($"Navigation failed: {e.WebErrorStatus}");
                }
                // Always send theme after navigation completes
                SendThemeToWebView();
                // Show devtools if debug mode is on
                if (FindPluginCore.GlobalConfiguration.GlobalSettings.Debug)
                {
                    try { MyWebView.CoreWebView2.OpenDevToolsWindow(); } catch { }
                }
            };

            MyWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "appassets", "WebContent", CoreWebView2HostResourceAccessKind.Allow);

            MyWebView.Source = new Uri("http://appassets/resultsweb.html");
            MyWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
            MyWebView.CoreWebView2.WebMessageReceived += MessageReceived;
        }
        catch (Exception)
        {
           
        }
    }

    private void SendThemeToWebView()
    {
        // Send theme colors to the web page
        var backgroundBrush = Application.Current.Resources["ApplicationPageBackgroundThemeBrush"] as SolidColorBrush;
        var foregroundBrush = Application.Current.Resources["TextFillColorPrimaryBrush"] as SolidColorBrush;
        var bgHex = backgroundBrush != null ? ColorToHex(backgroundBrush) : "#FFFFFF";
        var fgHex = foregroundBrush != null ? ColorToHex(foregroundBrush) : "#000000";
        var colorMessage = new {
            verb = "setTheme",
            data = new {
                background = bgHex,
                foreground = fgHex
            }
        };
        try
        {
            MyWebView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(colorMessage));
        }
        catch { }

        SendLevelColorsToWebView();
    }

    /// <summary>
    /// Push the user's per-level row colors (theme preset + overrides from
    /// <see cref="ResultsViewerSettings"/>) into the WebView2 page. Colors are converted from
    /// WinUI's <c>#AARRGGBB</c> format (alpha-first) to CSS <c>rgba(...)</c> so the browser
    /// renders them with the same transparency as the native viewer.
    /// </summary>
    private void SendLevelColorsToWebView()
    {
        try
        {
            var themeName = ResultsViewerSettings.ThemeName;
            var presets = FindNeedleUX.Pages.NativeResultViewer.NativeResultsPageViewModel.ThemePresets;
            var preset = presets.TryGetValue(themeName, out var p)
                ? p
                : presets[FindNeedleUX.Pages.NativeResultViewer.NativeResultsPageViewModel.DefaultThemeName];

            var overrides = ResultsViewerSettings.LevelColors;

            var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // Theme preset first so every known level gets a base color.
            foreach (var kv in preset) resolved[kv.Key] = WinUIHexToCss(kv.Value);
            // Per-level overrides win.
            foreach (var kv in overrides) resolved[kv.Key] = WinUIHexToCss(kv.Value);

            var msg = new { verb = "setLevelColors", data = resolved };
            MyWebView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(msg));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SendLevelColorsToWebView failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Convert a WinUI/WPF colour literal into a CSS-friendly form.
    ///   <c>#AARRGGBB</c>     → <c>rgba(R, G, B, A/255)</c>
    ///   <c>#RRGGBB</c>       → unchanged
    ///   <c>"Transparent"</c> → <c>"transparent"</c>
    /// Anything else is returned verbatim and let the browser parse / drop.
    /// </summary>
    private static string WinUIHexToCss(string winuiHex)
    {
        if (string.IsNullOrWhiteSpace(winuiHex)) return "";
        if (string.Equals(winuiHex, "Transparent", StringComparison.OrdinalIgnoreCase)) return "transparent";
        var s = winuiHex.TrimStart('#');
        try
        {
            if (s.Length == 8)
            {
                int a = Convert.ToInt32(s.Substring(0, 2), 16);
                int r = Convert.ToInt32(s.Substring(2, 2), 16);
                int g = Convert.ToInt32(s.Substring(4, 2), 16);
                int b = Convert.ToInt32(s.Substring(6, 2), 16);
                double alpha = a / 255.0;
                return string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"rgba({r},{g},{b},{alpha:F3})");
            }
            if (s.Length == 6) return "#" + s;
        }
        catch { /* fall through */ }
        return winuiHex;
    }

    private static string ColorToHex(SolidColorBrush brush)
    {
        var color = brush.Color;
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    // Cached paged source for the active viewer session. Built once on first 'getData' for
    // server-side mode and reused across every getPage round-trip. Disposed when the page
    // navigates away.
    private IPagedLogSource _pagedSource;

    private void MessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        // JS sends two kinds of messages:
        //   1) bare string `'getData'` (legacy bootstrap, via postMessage("getData"))
        //   2) JSON envelope  `{verb,id,data}` (page / level-count requests, via postMessage({...}))
        //
        // CoreWebView2 surfaces these differently: TryGetWebMessageAsString returns the string
        // for #1 and throws/returns null for #2; WebMessageAsJson always returns the JSON form
        // (for #1 that's a quoted "\"getData\"" string literal).
        string stringMessage = null;
        try { stringMessage = args.TryGetWebMessageAsString(); } catch { /* JSON path */ }

        if (stringMessage == "getData")
        {
            LoadResults();
            return;
        }

        // Anything else must be JSON. Don't fall back to LoadResults for unknown shapes — that
        // would re-send setMode and force JS to re-init DataTables, which throws
        // "Cannot reinitialise DataTable" and starves the original request's response.
        string json;
        try { json = args.WebMessageAsJson; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ResultsWebPage: failed to read WebMessageAsJson: {ex.Message}");
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("verb", out var verbEl))
            {
                System.Diagnostics.Debug.WriteLine($"ResultsWebPage: ignoring non-envelope message: {json}");
                return;
            }
            var verb = verbEl.GetString();
            int requestId = root.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var i) ? i : 0;

            switch (verb)
            {
                case "getData":
                    LoadResults();
                    break;
                case "getPage":
                    HandleGetPage(requestId, root.GetProperty("data"));
                    break;
                case "getLevelCounts":
                    HandleGetLevelCounts(requestId, root.GetProperty("data"));
                    break;
                default:
                    System.Diagnostics.Debug.WriteLine($"ResultsWebPage: unknown verb '{verb}'");
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ResultsWebPage MessageReceived parse error: {ex.Message}; json={json}");
        }
    }


    public static string SerializeAndEncodeLogLine(LogLine logLine)
    {
        // Dynamically get all public properties of LogLine
        var dict = new Dictionary<string, object?>();
        foreach (var prop in typeof(LogLine).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            var value = prop.GetValue(logLine);
            if (value is string str)
                dict[prop.Name] = System.Web.HttpUtility.JavaScriptStringEncode(str);
            else if (value != null)
                dict[prop.Name] = System.Web.HttpUtility.JavaScriptStringEncode(value.ToString());
            else
                dict[prop.Name] = "null";
        }
        // Also include all fields (not just properties)
        foreach (var field in typeof(LogLine).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            var value = field.GetValue(logLine);
            if (value is string str)
                dict[field.Name] = System.Web.HttpUtility.JavaScriptStringEncode(str);
            else if (value != null)
                dict[field.Name] = System.Web.HttpUtility.JavaScriptStringEncode(value.ToString());
            else
                dict[field.Name] = "null";
        }
        return JsonSerializer.Serialize(dict);
    }

    private const int BatchSize = 1000;

    /// <summary>
    /// Hybrid entry point. Decides whether to stream every row to DataTables (client-side mode,
    /// fastest for small data) or hand it a paged endpoint and serve one page per ajax request
    /// (server-side mode, scales to millions). Cutover is governed by
    /// <see cref="ResultsViewerSettings.WebViewerServerSideThreshold"/>.
    /// </summary>
    private void LoadResults()
    {
        // Counting from storage gives us the total cheaply (one SQL COUNT on indexed table).
        // GetLogLines() would materialize every row just to read its length, which is exactly
        // what server-side mode exists to avoid.
        int total = CountAvailableRows();
        int threshold = ResultsViewerSettings.WebViewerServerSideThreshold;
        bool useServer = total > threshold;

        using var _ = PerfLog.Scope("viewer.web.load",
            ("total", total), ("threshold", threshold), ("mode", useServer ? "server" : "client"));

        if (useServer) LoadServerSide(total);
        else LoadClientSide();
    }

    private static int CountAvailableRows()
    {
        try
        {
            var storage = MiddleLayerService.GetSearchStorage();
            switch (storage)
            {
                case FindPluginCore.Implementations.Storage.SqliteStorage sql:
                    return sql.GetStatistics().filteredRecordCount;
                case FindPluginCore.Implementations.Storage.HybridStorage hybrid:
                    // Count without forcing settlement to disk — Hybrid exposes the underlying
                    // SQLite store, which holds whatever has been flushed; in-memory rows are
                    // counted via GetLogLines fallback below.
                    return hybrid.InnerSqliteStorage?.GetStatistics().filteredRecordCount
                        ?? MiddleLayerService.GetFilteredRowCount();
                default:
                    return MiddleLayerService.GetFilteredRowCount();
            }
        }
        catch
        {
            return MiddleLayerService.GetFilteredRowCount();
        }
    }

    private void LoadClientSide()
    {
        // Tell JS we're in client mode so it skips serverSide DataTables config.
        MyWebView.CoreWebView2.PostWebMessageAsJson(
            JsonSerializer.Serialize(new { verb = "setMode", data = new { mode = "client" } }));

        var lines = MiddleLayerService.GetLogLines();
        var total = lines.Count;

        MyWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { verb = "total", data = new { total } }));

        var batch = new List<Dictionary<string, object>>(BatchSize);
        foreach (var line in lines)
        {
            batch.Add(JsonSerializer.Deserialize<Dictionary<string, object>>(SerializeAndEncodeLogLine(line)));
            if (batch.Count >= BatchSize)
            {
                MyWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { verb = "newresults", data = batch }));
                batch = new List<Dictionary<string, object>>(BatchSize);
            }
        }
        if (batch.Count > 0)
            MyWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { verb = "newresults", data = batch }));

        MyWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { verb = "done", data = new { id = 0 } }));
        SendThemeToWebView();
    }

    /// <summary>
    /// Server-side mode bootstrap. Builds the paged source once, then tells JS to switch
    /// DataTables into <c>serverSide:true</c> with the total row count + the list of distinct
    /// levels (needed so the Level dropdown filter has options on first render). Subsequent
    /// row data flows in via <see cref="HandleGetPage"/> on each DataTables ajax request.
    /// </summary>
    private void LoadServerSide(int total)
    {
        EnsurePagedSource();

        // Seed levels so the level dropdown can be populated even before the first ajax draw.
        List<string> levels;
        try { levels = _pagedSource.GetDistinctLevels(); }
        catch { levels = new List<string>(); }

        var msg = new
        {
            verb = "setMode",
            data = new
            {
                mode = "server",
                total,
                levels,
            }
        };
        MyWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(msg));
        SendThemeToWebView();
    }

    private void EnsurePagedSource()
    {
        if (_pagedSource != null) return;
        var storage = MiddleLayerService.GetSearchStorage();
        var fallback = MiddleLayerService.GetLogLines() ?? new List<LogLine>();
        _pagedSource = PagedLogSourceFactory.Create(storage, fallback);
    }

    /// <summary>
    /// Translate one DataTables ajax request into an <see cref="IPagedLogSource"/> query and
    /// post the result back. DataTables sends:
    ///   draw    int     echo back so it ignores stale responses
    ///   start   int     offset
    ///   length  int     page size (-1 = all)
    ///   search.value  string  global search
    ///   columns[N].search.value  per-column search
    ///   order[0].column int     column index to sort by
    ///   order[0].dir    string  "asc" | "desc"
    /// Extras we pass from JS as custom fields:
    ///   timeFrom / timeTo (ISO strings, optional)
    /// </summary>
    private void HandleGetPage(int requestId, JsonElement req)
    {
        EnsurePagedSource();

        int draw  = req.TryGetProperty("draw",  out var d) && d.TryGetInt32(out var di) ? di : 0;
        int start = req.TryGetProperty("start", out var s) && s.TryGetInt32(out var si) ? si : 0;
        int length = req.TryGetProperty("length", out var l) && l.TryGetInt32(out var li) ? li : 100;
        if (length <= 0) length = 100; // -1 (= "All") would try to materialise everything; refuse

        var filters = BuildFilterSpec(req);
        var sort    = BuildSortSpec(req);

        int filtered;
        List<LogLine> page;
        var t0 = Environment.TickCount64;
        try
        {
            filtered = _pagedSource.GetFilteredCount(filters);
            page = _pagedSource.GetPage(filters, sort, start, length);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"HandleGetPage error: {ex.Message}");
            filtered = 0;
            page = new List<LogLine>();
        }
        PerfLog.Log("viewer.web.page",
            ("start", start), ("length", length),
            ("filtered", filtered), ("returned", page.Count),
            ("has_search", !string.IsNullOrEmpty(filters.Search)),
            ("elapsed_ms", Environment.TickCount64 - t0));

        // DataTables expects each row as an array of column values OR an object keyed by column
        // data names. We use objects so the column config in resultsweb.js stays the same.
        var data = new List<Dictionary<string, object>>(page.Count);
        foreach (var line in page)
        {
            data.Add(JsonSerializer.Deserialize<Dictionary<string, object>>(SerializeAndEncodeLogLine(line)));
        }

        var resp = new
        {
            verb = "pageResult",
            id = requestId,
            data = new
            {
                draw,
                recordsTotal = _pagedSource.TotalCount,
                recordsFiltered = filtered,
                data,
            }
        };
        MyWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(resp));
    }

    private void HandleGetLevelCounts(int requestId, JsonElement req)
    {
        EnsurePagedSource();
        var filters = BuildFilterSpec(req);
        Dictionary<string, int> counts;
        try { counts = _pagedSource.GetLevelCounts(filters); }
        catch { counts = new Dictionary<string, int>(); }

        var resp = new
        {
            verb = "levelCountsResult",
            id = requestId,
            data = new { counts }
        };
        MyWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(resp));
    }

    // ----- request → spec translators -----

    private static FilterSpec BuildFilterSpec(JsonElement req)
    {
        string search = "";
        if (req.TryGetProperty("search", out var sEl)
            && sEl.TryGetProperty("value", out var sv))
            search = sv.GetString() ?? "";

        // Per-column searches. DataTables indexes the columns in our config:
        // 0 Index, 1 Time, 2 Provider, 3 TaskName, 4 Message, 5 Source, 6 Level, 7 More.
        string provider = "", taskName = "", message = "", source = "", level = "";
        if (req.TryGetProperty("columns", out var cols) && cols.ValueKind == JsonValueKind.Array)
        {
            int i = 0;
            foreach (var col in cols.EnumerateArray())
            {
                if (col.TryGetProperty("search", out var cs)
                    && cs.TryGetProperty("value", out var cv)
                    && cv.ValueKind == JsonValueKind.String)
                {
                    var v = cv.GetString() ?? "";
                    if (!string.IsNullOrEmpty(v))
                    {
                        switch (i)
                        {
                            case 2: provider = v; break;
                            case 3: taskName = v; break;
                            case 4: message  = v; break;
                            case 5: source   = v; break;
                            case 6:
                                // DataTables sends Level with regex anchors (^value$) when using
                                // a dropdown filter. Strip them; our filter is already exact-match
                                // on the level dimension.
                                if (v.StartsWith("^") && v.EndsWith("$") && v.Length >= 2)
                                    v = v.Substring(1, v.Length - 2);
                                level = v;
                                break;
                        }
                    }
                }
                i++;
            }
        }

        // Custom extras from our JS ajax.data hook.
        DateTime? from = null, to = null;
        if (req.TryGetProperty("timeFrom", out var tf) && tf.ValueKind == JsonValueKind.String
            && DateTime.TryParse(tf.GetString(), out var fromParsed))
            from = fromParsed;
        if (req.TryGetProperty("timeTo", out var tt) && tt.ValueKind == JsonValueKind.String
            && DateTime.TryParse(tt.GetString(), out var toParsed))
            to = toParsed;

        return new FilterSpec(search, provider, taskName, message, source, level, from, to);
    }

    private static SortSpec BuildSortSpec(JsonElement req)
    {
        if (!req.TryGetProperty("order", out var ord) || ord.ValueKind != JsonValueKind.Array)
            return SortSpec.None;
        var first = ord.EnumerateArray().FirstOrDefault();
        if (first.ValueKind != JsonValueKind.Object) return SortSpec.None;
        if (!first.TryGetProperty("column", out var colEl) || !colEl.TryGetInt32(out var col))
            return SortSpec.None;
        bool desc = first.TryGetProperty("dir", out var dirEl)
                    && string.Equals(dirEl.GetString(), "desc", StringComparison.OrdinalIgnoreCase);

        // Index → viewer column name. Mirrors the columns array used by both viewers.
        var name = col switch
        {
            0 => "Index", 1 => "Time", 2 => "Provider", 3 => "TaskName",
            4 => "Message", 5 => "Source", 6 => "Level",
            _ => null
        };
        return string.IsNullOrEmpty(name) ? SortSpec.None : new SortSpec(name, desc);
    }
}