using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FindNeedleUX.Services;

/// <summary>Which column of Home a section lives in. Sections reorder within their column only; the
/// columns themselves are the page's structure (open on the left, workspace on the right, tools below).</summary>
public enum HomeColumn { Left, Right, Bottom }

/// <summary>One Home section: a stable id (persisted), its title, its column, and whether it is fixed
/// (the Open card: always first, never hidden or collapsed - Home's job is opening a log).</summary>
public sealed record HomeSection(string Id, string Title, HomeColumn Column, bool Fixed = false);

/// <summary>
/// The catalog of Home's sections plus the user's arrangement: which are hidden, which are collapsed to
/// their header, and their order within each column. The light version of "arrange Home": no widget
/// grid, no layout editor - a section list with show / hide / move, the same pencil pattern as the
/// status bar and the Tools row. Persisted to a JSON file (LocalAppData, or beside the viewer settings
/// file when FINDNEEDLE_VIEWER_SETTINGS redirects it, so UI tests are isolated). Pure, unit-tested.
/// </summary>
public static class HomeSectionCatalog
{
    public static readonly IReadOnlyList<HomeSection> All = new[]
    {
        new HomeSection("open",       "Open a log",        HomeColumn.Left, Fixed: true),
        new HomeSection("recent",     "Recent searches",   HomeColumn.Left),
        new HomeSection("known",      "Known logs",        HomeColumn.Left),
        new HomeSection("workspace",  "Current workspace", HomeColumn.Right),
        new HomeSection("workspaces", "Workspaces",        HomeColumn.Right),
        new HomeSection("tools",      "Tools",             HomeColumn.Bottom),
    };

    public static HomeSection Find(string id) => All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
    public static bool IsValidId(string id) => Find(id) != null;

    private sealed class Data
    {
        public List<string> Hidden { get; set; } = new();
        public List<string> Collapsed { get; set; } = new();
        /// <summary>Per column, the section ids in display order. Ids missing here (new sections) go at the end in catalog order.</summary>
        public Dictionary<string, List<string>> Order { get; set; } = new();
    }

    private static string DefaultPath
    {
        get
        {
            var viewer = Environment.GetEnvironmentVariable("FINDNEEDLE_VIEWER_SETTINGS");
            if (!string.IsNullOrWhiteSpace(viewer))
                return viewer + ".home-sections.json"; // isolated alongside the redirected viewer settings
            return Path.Combine(FindNeedleCoreUtils.PackagedAppPaths.LocalAppData, "FindNeedle", "home-sections.json");
        }
    }

    private static string _path;
    private static string PathToUse => _path ?? DefaultPath;

    internal static void SetStorageLocationForTests(string path) => _path = path;
    internal static void ResetStorageForTests() => _path = null;

    /// <summary>Raised after any change so an open Home can re-apply the layout.</summary>
    public static event Action Changed;

    // ----- reads -----

    public static bool IsHidden(string id) => !IsFixed(id) && Read().Hidden.Contains(id, StringComparer.OrdinalIgnoreCase);
    public static bool IsCollapsed(string id) => !IsFixed(id) && Read().Collapsed.Contains(id, StringComparer.OrdinalIgnoreCase);
    private static bool IsFixed(string id) => Find(id)?.Fixed == true;

    /// <summary>The sections of a column in display order: a fixed section first, then the stored order,
    /// then any section the stored order does not know (in catalog order). Hidden sections are included;
    /// the caller decides how to render them (Home skips them, the pencil lists them unchecked).</summary>
    public static List<HomeSection> Order(HomeColumn column)
    {
        var inColumn = All.Where(s => s.Column == column).ToList();
        var stored = Read().Order.TryGetValue(column.ToString(), out var ids) ? ids : new List<string>();
        var result = new List<HomeSection>();
        foreach (var s in inColumn.Where(s => s.Fixed)) result.Add(s);
        foreach (var id in stored)
        {
            var s = inColumn.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (s != null && !s.Fixed && !result.Contains(s)) result.Add(s);
        }
        foreach (var s in inColumn) if (!result.Contains(s)) result.Add(s);
        return result;
    }

    // ----- writes -----

    public static void SetHidden(string id, bool hidden)
    {
        if (!IsValidId(id) || IsFixed(id)) return;
        var d = Read();
        d.Hidden.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
        if (hidden) d.Hidden.Add(Find(id).Id);
        Write(d);
    }

    public static void SetCollapsed(string id, bool collapsed)
    {
        if (!IsValidId(id) || IsFixed(id)) return;
        var d = Read();
        d.Collapsed.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
        if (collapsed) d.Collapsed.Add(Find(id).Id);
        Write(d);
    }

    /// <summary>Move a section one step within its column (delta -1 = up, +1 = down). A fixed section
    /// never moves and nothing moves above it. Returns true when something changed.</summary>
    public static bool Move(string id, int delta)
    {
        var s = Find(id);
        if (s == null || s.Fixed) return false;
        var order = Order(s.Column).Where(x => !x.Fixed).Select(x => x.Id).ToList();
        int i = order.FindIndex(x => string.Equals(x, s.Id, StringComparison.OrdinalIgnoreCase));
        int target = i + delta;
        if (i < 0 || target < 0 || target >= order.Count) return false;
        (order[i], order[target]) = (order[target], order[i]);
        var d = Read();
        d.Order[s.Column.ToString()] = order;
        Write(d);
        return true;
    }

    /// <summary>Back to the shipped layout: everything shown, expanded, in catalog order.</summary>
    public static void Reset() => Write(new Data());

    // ----- storage -----

    private static Data Read()
    {
        try
        {
            var p = PathToUse;
            if (!File.Exists(p)) return new Data();
            return JsonSerializer.Deserialize<Data>(File.ReadAllText(p)) ?? new Data();
        }
        catch { return new Data(); }
    }

    private static void Write(Data d)
    {
        try
        {
            var p = PathToUse;
            Directory.CreateDirectory(Path.GetDirectoryName(p) ?? ".");
            File.WriteAllText(p, JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { FindNeedlePluginLib.Logger.Instance.Log($"HomeSectionCatalog: could not save: {ex.Message}"); }
        Changed?.Invoke();
    }
}
