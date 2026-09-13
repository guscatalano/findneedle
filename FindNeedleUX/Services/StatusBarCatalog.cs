using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FindNeedleUX.Services;

/// <summary>One configurable status-bar item: an id and a display label. Info items show a count and
/// navigate; action items (e.g. run_view) perform a command. Rendering/behavior lives in MainWindow.
/// Ids are stable (persisted in the user's status-bar.json) — only labels change. There is deliberately
/// no "run without opening the viewer" item: one verb, "Run".</summary>
public sealed record StatusBarItem(string Id, string Label);

/// <summary>
/// The catalog of available status-bar items plus the user's chosen subset/order, persisted to a JSON
/// file under <c>%LocalAppData%\FindNeedle\</c> (works packaged or unpackaged). Mirrors
/// <see cref="QuickActionCatalog"/>. A storage seam lets tests redirect to a temp file.
/// </summary>
public static class StatusBarCatalog
{
    public static readonly IReadOnlyList<StatusBarItem> All = new[]
    {
        new StatusBarItem("locations",   "Sources"),
        new StatusBarItem("rules",       "Rule files"),
        new StatusBarItem("lastrun",     "Last run"),
        new StatusBarItem("outputfiles", "Outputs"),
        new StatusBarItem("run_view",    "Run"),
        new StatusBarItem("stop",        "Stop"),
        new StatusBarItem("perf",        "Storage / timing"), // renders as "Storage: <tier>"; click = timing report
        new StatusBarItem("connections", "Connections"),
        new StatusBarItem("autorules",   "Auto rules"),
        new StatusBarItem("diagram",     "Diagram tools"),
        new StatusBarItem("mcp",         "MCP server"),
    };

    // The strip is the RUN-STATE surface: what is running, what the last run produced, where it is
    // stored, what it wrote. Workspace composition (sources / rule files) is the workspace chip's job
    // in the breadcrumb bar (and the Home card's, with the actual items), so those two segments are not
    // defaults any more - they stay in the catalog for anyone who wants them via the pencil. Order:
    // action, then state, then artefacts.
    public static readonly IReadOnlyList<string> Defaults =
        new[] { "run_view", "lastrun", "perf", "outputfiles" };

    private static readonly string DefaultPath = Path.Combine(
        FindNeedleCoreUtils.PackagedAppPaths.LocalAppData,
        "FindNeedle", "status-bar.json");

    private static string _path = DefaultPath;

    internal static void SetStorageLocationForTests(string path) => _path = path;
    internal static void ResetStorageForTests() => _path = DefaultPath;

    public static bool IsValidId(string id) => All.Any(a => a.Id == id);
    public static StatusBarItem Find(string id) => All.FirstOrDefault(a => a.Id == id);

    /// <summary>The default set before the strip became run-state only. A stored selection that is
    /// exactly this (optionally with the Storage segment appended) was never really customized - the
    /// file is written whenever the pencil is used, even to add one item - so it follows the new defaults
    /// rather than pinning the old duplication forever. Any other stored selection is the user's and is
    /// left alone.</summary>
    internal static readonly IReadOnlyList<string> LegacyDefaults =
        new[] { "locations", "rules", "lastrun", "run_view", "outputfiles" };

    public static List<string> GetSelectedIds()
    {
        var ids = ReadRaw().Where(IsValidId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ids.Count == 0) return Defaults.ToList();
        if (IsLegacyDefaultSelection(ids)) return Defaults.ToList();
        return ids;
    }

    private static bool IsLegacyDefaultSelection(List<string> ids)
    {
        if (ids.Count < LegacyDefaults.Count || ids.Count > LegacyDefaults.Count + 1) return false;
        for (int i = 0; i < LegacyDefaults.Count; i++)
            if (!string.Equals(ids[i], LegacyDefaults[i], StringComparison.OrdinalIgnoreCase)) return false;
        return ids.Count == LegacyDefaults.Count || string.Equals(ids[^1], "perf", StringComparison.OrdinalIgnoreCase);
    }

    public static void SetSelectedIds(IEnumerable<string> ids)
        => WriteRaw((ids ?? Enumerable.Empty<string>()).Where(IsValidId).Distinct(StringComparer.OrdinalIgnoreCase).ToList());

    public static List<string> Toggle(string id)
    {
        var ids = GetSelectedIds();
        if (ids.Contains(id, StringComparer.OrdinalIgnoreCase))
            ids.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
        else if (IsValidId(id))
            ids.Add(id);
        SetSelectedIds(ids);
        return ids;
    }

    public static List<string> Move(string id, int delta)
    {
        var ids = GetSelectedIds();
        int i = ids.FindIndex(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
        int target = i + delta;
        if (i >= 0 && target >= 0 && target < ids.Count)
        {
            (ids[i], ids[target]) = (ids[target], ids[i]);
            SetSelectedIds(ids);
        }
        return ids;
    }

    public static bool IsSelected(string id) =>
        GetSelectedIds().Contains(id, StringComparer.OrdinalIgnoreCase);

    private static List<string> ReadRaw()
    {
        try
        {
            if (!File.Exists(_path)) return new List<string>();
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_path)) ?? new List<string>();
        }
        catch { return new List<string>(); }
    }

    private static void WriteRaw(List<string> ids)
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_path, System.Text.Json.JsonSerializer.Serialize(ids));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"StatusBarCatalog.WriteRaw failed: {ex.Message}"); }
    }
}
