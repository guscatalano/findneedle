using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace FindNeedleUX.Services.Mcp;

/// <summary>
/// The MCP tool catalog. Each entry has a name, a human description, a JSON-schema for its
/// arguments (for <c>tools/list</c>), and an async handler that calls <see cref="McpViewerBridge"/>.
/// Handlers return plain objects which <see cref="McpServer"/> serializes to JSON text content.
///
/// Workspace tools drive the locations/rules/search; viewer tools read and drive the active result
/// viewer. Viewer tools surface a clear error when no viewer is open (the bridge throws
/// <see cref="McpNoViewerException"/>).
/// </summary>
internal static class McpTools
{
    internal sealed class ToolDef
    {
        public string Name;
        public string Description;
        public object InputSchema;
        public Func<JsonElement, Task<object>> Invoke;
    }

    private static McpViewerBridge B => McpViewerBridge.Instance;

    // ----- arg helpers (tolerant of missing/null) -----
    private static bool Has(JsonElement a, string n) =>
        a.ValueKind == JsonValueKind.Object && a.TryGetProperty(n, out var v) && v.ValueKind != JsonValueKind.Null;
    private static string Str(JsonElement a, string n, string def = null)
        => Has(a, n) ? (a.GetProperty(n).ValueKind == JsonValueKind.String ? a.GetProperty(n).GetString() : a.GetProperty(n).ToString()) : def;
    private static int Int(JsonElement a, string n, int def)
        => Has(a, n) && a.GetProperty(n).TryGetInt32(out var v) ? v : def;
    private static long Lng(JsonElement a, string n, long def)
        => Has(a, n) && a.GetProperty(n).TryGetInt64(out var v) ? v : def;
    private static bool Bool(JsonElement a, string n, bool def)
        => Has(a, n) && (a.GetProperty(n).ValueKind == JsonValueKind.True || a.GetProperty(n).ValueKind == JsonValueKind.False)
            ? a.GetProperty(n).GetBoolean() : def;

    private static object Ok(object extra = null) => extra ?? new { ok = true };

    /// <summary>
    /// Read FindNeedle's own diagnostic logs for the <c>get_diagnostics</c> tool: the perf log
    /// (from <see cref="FindPluginCore.Diagnostics.PerfLog.FilePath"/>) and/or the app message log
    /// (in-memory <see cref="FindNeedlePluginLib.Logger.LogCache"/> for this run). Pure read; never
    /// throws — a failure on one source is reported in-band so the other still returns.
    /// </summary>
    private static object ReadDiagnostics(string which, int maxLines, string contains)
    {
        bool wantPerf = which is "both" or "perf";
        bool wantApp = which is "both" or "app";
        var perfPath = FindPluginCore.Diagnostics.PerfLog.FilePath;

        IEnumerable<string> Filter(IEnumerable<string> lines) =>
            string.IsNullOrEmpty(contains)
                ? lines
                : lines.Where(l => l != null && l.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0);

        List<string> perf = null, app = null;
        if (wantPerf)
        {
            try { perf = Filter(ReadAllLinesShared(perfPath)).TakeLast(maxLines).ToList(); }
            catch (Exception ex) { perf = new List<string> { "(perf log unreadable: " + ex.Message + ")" }; }
        }
        if (wantApp)
        {
            try { app = Filter(FindNeedlePluginLib.Logger.Instance.LogCache).TakeLast(maxLines).ToList(); }
            catch (Exception ex) { app = new List<string> { "(app log unreadable: " + ex.Message + ")" }; }
        }

        return new { perfLogPath = perfPath, perfLineCount = perf?.Count ?? 0, appLineCount = app?.Count ?? 0, perf, app };
    }

    /// <summary>Read a text file's lines with a shared read lock, so reading the perf log never
    /// fights the append-writer (which holds the file open during a write).</summary>
    private static string[] ReadAllLinesShared(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return Array.Empty<string>();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        var lines = new List<string>();
        string line;
        while ((line = sr.ReadLine()) != null) lines.Add(line);
        return lines.ToArray();
    }

    // schema fragments
    private static object Obj(object properties, params string[] required)
        => new { type = "object", properties, required };
    private static object S(string desc) => new { type = "string", description = desc };
    private static object I(string desc) => new { type = "integer", description = desc };
    private static object Bn(string desc) => new { type = "boolean", description = desc };

    private static List<ToolDef> _all;
    public static IReadOnlyList<ToolDef> All => _all ??= Build();

    private static List<ToolDef> Build() => new()
    {
        // ---------- workspace ----------
        new ToolDef
        {
            Name = "list_locations",
            Description = "List the data sources (folders, files, Kusto) currently loaded in the workspace.",
            InputSchema = Obj(new { }),
            Invoke = async _ => await B.ListLocationsAsync(),
        },
        new ToolDef
        {
            Name = "list_rules",
            Description = "List the RuleDSL rule files applied to the current search.",
            InputSchema = Obj(new { }),
            Invoke = async _ => await B.ListRulesAsync(),
        },
        new ToolDef
        {
            Name = "add_folder",
            Description = "Add a folder or file as a data source location.",
            InputSchema = Obj(new { path = S("Absolute path to a folder or log file.") }, "path"),
            Invoke = async a => { await B.AddFolderAsync(Str(a, "path")); return Ok(); },
        },
        new ToolDef
        {
            Name = "add_kusto",
            Description = "Add a live Kusto (Azure Data Explorer) cluster query as a data source location.",
            InputSchema = Obj(new
            {
                cluster = S("Cluster URI, e.g. https://help.kusto.windows.net"),
                database = S("Database name."),
                kql = S("KQL query to run."),
                auth = S("Auth mode: Interactive, DeviceCode, or AzureCli. Default Interactive."),
                rowLimit = I("0=default 500k cap, a number raises the cap, -1 = no limit."),
            }, "cluster", "database", "kql"),
            Invoke = async a =>
            {
                await B.AddKustoAsync(Str(a, "cluster"), Str(a, "database"), Str(a, "kql"),
                    Str(a, "auth", "Interactive"), Lng(a, "rowLimit", 0));
                return Ok();
            },
        },
        new ToolDef
        {
            Name = "remove_location",
            Description = "Remove a loaded location by name (see list_locations).",
            InputSchema = Obj(new { name = S("Location name to remove.") }, "name"),
            Invoke = async a => new { removed = await B.RemoveLocationAsync(Str(a, "name")) },
        },
        new ToolDef
        {
            Name = "run_search",
            Description = "Run the search over the loaded locations and refresh the viewer with the results.",
            InputSchema = Obj(new { }),
            Invoke = async _ => { await B.RunSearchAsync(); return Ok(); },
        },
        new ToolDef
        {
            Name = "cancel_search",
            Description = "Cancel an in-flight (streaming) search.",
            InputSchema = Obj(new { }),
            Invoke = async _ => { await B.CancelSearchAsync(); return Ok(); },
        },

        // ---------- status / readiness ----------
        new ToolDef
        {
            Name = "status",
            Description = "Health/orientation: whether the MCP server is running, its port, whether a result viewer is open (viewer tools usable), how many locations are loaded, and current row counts. Always succeeds.",
            InputSchema = Obj(new { }),
            Invoke = async _ => await B.GetStatusAsync(),
        },
        new ToolDef
        {
            Name = "wait_for_viewer",
            Description = "Wait until a result viewer is open (e.g. just after app launch or a search), so viewer tools don't hit the 'no viewer' race. Returns whether one is ready.",
            InputSchema = Obj(new { timeoutMs = I("Max time to wait in ms (default 10000, max 120000).") }),
            Invoke = async a => new { ready = await B.WaitForViewerAsync(Int(a, "timeoutMs", 10_000)) },
        },
        new ToolDef
        {
            Name = "get_diagnostics",
            Description = "Read FindNeedle's OWN diagnostic logs — NOT the logs being analyzed. Two sources: 'perf' is the structured timing log (search.run / location.* / viewer.* / cache.* / storage.* events with elapsed_ms) that shows how the app is performing — load times, cache hits, storage tier; 'app' is the human-readable message log (info / warnings / errors) for this run. Use this to understand the user's experience and diagnose slow loads or failures. Always succeeds.",
            InputSchema = Obj(new
            {
                which = S("Which log: 'perf' (timings), 'app' (messages), or 'both' (default)."),
                lines = I("Max lines per log, newest last (default 100, max 2000)."),
                contains = S("Optional case-insensitive substring filter applied per line (e.g. 'cache', 'location.end', 'error')."),
            }),
            Invoke = async a => await Task.FromResult(ReadDiagnostics(
                (Str(a, "which", "both") ?? "both").Trim().ToLowerInvariant(),
                Math.Clamp(Int(a, "lines", 100), 1, 2000),
                Str(a, "contains"))),
        },

        // ---------- viewer: read ----------
        new ToolDef
        {
            Name = "get_view",
            Description = "Get the viewer's current filter, sort, page, and totals.",
            InputSchema = Obj(new { }),
            Invoke = async _ => await B.GetViewAsync(),
        },
        new ToolDef
        {
            Name = "get_page",
            Description = "Get a page of the current filtered/sorted result (compact rows, Message truncated). Omit offset for the viewer's current page.",
            InputSchema = Obj(new { offset = I("0-based row offset; omit for current page."), limit = I("Max rows (default page size, hard cap 500).") }),
            Invoke = async a =>
            {
                int? offset = Has(a, "offset") ? Int(a, "offset", 0) : (int?)null;
                return await B.GetPageAsync(offset, Int(a, "limit", 0));
            },
        },
        new ToolDef
        {
            Name = "get_record",
            Description = "Get one row by its stable id (from get_page), with all fields and full Message.",
            InputSchema = Obj(new { id = I("Stable row id (RowId).") }, "id"),
            Invoke = async a => await B.GetRecordAsync(Lng(a, "id", -1)),
        },
        new ToolDef
        {
            Name = "summary",
            Description = "Totals, per-level counts, time range, and the loaded sources/rules for the current filtered set.",
            InputSchema = Obj(new { }),
            Invoke = async _ => await B.GetSummaryAsync(),
        },
        new ToolDef
        {
            Name = "histogram",
            Description = "Counts of the current filtered set bucketed evenly across its time range.",
            InputSchema = Obj(new { buckets = I("Number of time buckets (default 20, max 200).") }),
            Invoke = async a => await B.GetHistogramAsync(Int(a, "buckets", 20)),
        },
        new ToolDef
        {
            Name = "facets",
            Description = "Top distinct values (with counts) of a field over the current filtered set — e.g. which providers/sources/levels/tasks dominate. Fields: provider, source, level, taskName, processName, processId, channel, eventId, machineName, username. Scans up to sampleCap rows (counts beyond it are approximate; the result flags 'truncated').",
            InputSchema = Obj(new
            {
                field = S("Field to group by: provider, source, level, taskName, processName, processId, channel, eventId, machineName, username."),
                limit = I("Max values to return (default 20)."),
                sampleCap = I("Max rows to scan (default 500000)."),
            }, "field"),
            Invoke = async a => await B.GetFacetsAsync(Str(a, "field"), Int(a, "limit", 20), Int(a, "sampleCap", 500_000)),
        },
        new ToolDef
        {
            Name = "top_patterns",
            Description = "Most common message templates over the current filtered set — the generic 'what is this log mostly saying?' view. Messages are normalized (numbers, GUIDs, hex, paths, quoted strings → placeholders) so near-identical lines collapse into one template with a count and an example. Scans up to sampleCap rows (default 200000; result flags 'truncated' if more exist).",
            InputSchema = Obj(new
            {
                limit = I("Max templates to return (default 20)."),
                sampleCap = I("Max rows to scan (default 200000)."),
            }),
            Invoke = async a => await B.GetTopPatternsAsync(Int(a, "limit", 20), Int(a, "sampleCap", 200_000)),
        },

        // ---------- viewer: drive ----------
        new ToolDef
        {
            Name = "search",
            Description = "Set the global search term (and optional column/time filters), then return the first page. Convenience over set_filter + get_page.",
            InputSchema = Obj(new
            {
                query = S("Global substring search across all columns."),
                provider = S("Provider column filter."),
                taskName = S("TaskName column filter."),
                message = S("Message column filter."),
                source = S("Source column filter."),
                level = S("Level filter (e.g. Error). Empty clears it."),
                from = S("ISO 8601 lower time bound; \"\" clears."),
                to = S("ISO 8601 upper time bound; \"\" clears."),
                limit = I("Rows to return (default page size)."),
            }),
            Invoke = async a =>
            {
                await B.SetFilterAsync(Str(a, "query"), Str(a, "provider"), Str(a, "taskName"),
                    Str(a, "message"), Str(a, "source"), Str(a, "level"), Str(a, "from"), Str(a, "to"));
                return await B.GetPageAsync(0, Int(a, "limit", 0));
            },
        },
        new ToolDef
        {
            Name = "set_filter",
            Description = "Set any subset of the viewer's filters (null/omitted = unchanged, \"\" = clear). Returns the new filtered count.",
            InputSchema = Obj(new
            {
                search = S("Global search term."),
                provider = S("Provider filter."),
                taskName = S("TaskName filter."),
                message = S("Message filter."),
                source = S("Source filter."),
                level = S("Level filter."),
                from = S("ISO 8601 lower time bound."),
                to = S("ISO 8601 upper time bound."),
            }),
            Invoke = async a => new
            {
                filteredCount = await B.SetFilterAsync(Str(a, "search"), Str(a, "provider"),
                    Str(a, "taskName"), Str(a, "message"), Str(a, "source"), Str(a, "level"),
                    Str(a, "from"), Str(a, "to"))
            },
        },
        new ToolDef
        {
            Name = "clear_filters",
            Description = "Clear all viewer filters. Returns the new (unfiltered) count.",
            InputSchema = Obj(new { }),
            Invoke = async _ => new { filteredCount = await B.ClearFiltersAsync() },
        },
        new ToolDef
        {
            Name = "set_sort",
            Description = "Sort the result by a column (Index/Time/Provider/TaskName/Message/Source/Level).",
            InputSchema = Obj(new { column = S("Column name."), descending = Bn("Descending order.") }, "column"),
            Invoke = async a => { await B.SetSortAsync(Str(a, "column"), Bool(a, "descending", false)); return Ok(); },
        },
        new ToolDef
        {
            Name = "goto_page",
            Description = "Navigate the viewer to a 1-based page number.",
            InputSchema = Obj(new { page = I("1-based page number.") }, "page"),
            Invoke = async a => { await B.GoToPageAsync(Int(a, "page", 1)); return Ok(); },
        },
        new ToolDef
        {
            Name = "set_page_size",
            Description = "Set how many rows the viewer shows per page.",
            InputSchema = Obj(new { pageSize = I("Rows per page.") }, "pageSize"),
            Invoke = async a => { await B.SetPageSizeAsync(Int(a, "pageSize", 100)); return Ok(); },
        },
        new ToolDef
        {
            Name = "select_row",
            Description = "Select (highlight + scroll to) a row by stable id, if it's on the current page.",
            InputSchema = Obj(new { id = I("Stable row id.") }, "id"),
            Invoke = async a => new { selected = await B.SelectRowAsync(Lng(a, "id", -1)) },
        },
        new ToolDef
        {
            Name = "tag_row",
            Description = "Tag a row by stable id, with an optional free-text note. Category must be one of: Important, Question, Resolved, Note (omit to keep the row's existing category). The note is shown in the row tooltip and details.",
            InputSchema = Obj(new
            {
                id = I("Stable row id."),
                tag = S("Tag category: Important, Question, Resolved, or Note."),
                text = S("Optional free-text note to attach to the tag."),
            }, "id"),
            Invoke = async a => new { tagged = await B.TagRowAsync(Lng(a, "id", -1), Str(a, "tag"), Str(a, "text")) },
        },
        new ToolDef
        {
            Name = "clear_tag",
            Description = "Remove the tag from a row by stable id.",
            InputSchema = Obj(new { id = I("Stable row id.") }, "id"),
            Invoke = async a => new { cleared = await B.ClearTagAsync(Lng(a, "id", -1)) },
        },
        new ToolDef
        {
            Name = "set_details_mode",
            Description = "Set the viewer's row-details mode: inrow, bottom, or popup.",
            InputSchema = Obj(new { mode = S("inrow | bottom | popup") }, "mode"),
            Invoke = async a => { await B.SetDetailsModeAsync(Str(a, "mode")); return Ok(); },
        },
        new ToolDef
        {
            Name = "export",
            Description = "Export the current filtered/sorted set (visible columns) to a file. Returns the path and row count.",
            InputSchema = Obj(new
            {
                format = S("csv | json | xml (default csv)."),
                path = S("Destination path; omit for a timestamped temp file."),
            }),
            Invoke = async a => await B.ExportAsync(Str(a, "format", "csv"), Str(a, "path")),
        },
    };
}
