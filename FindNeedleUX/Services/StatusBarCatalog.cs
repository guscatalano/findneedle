using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FindNeedleUX.Services;

/// <summary>One configurable status-bar item: an id and a display label. Info items show a count and
/// navigate; action items (e.g. run_view) perform a command. Rendering/behavior lives in MainWindow.</summary>
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
        new StatusBarItem("locations",   "Locations"),
        new StatusBarItem("filters",     "Filters"),
        new StatusBarItem("rules",       "Rules"),
        new StatusBarItem("lastrun",     "Last run"),
        new StatusBarItem("outputfiles", "Output files"),
        new StatusBarItem("run_view",    "Run → View Results"),
    };

    public static readonly IReadOnlyList<string> Defaults =
        new[] { "locations", "rules", "lastrun", "run_view", "outputfiles" };

    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FindNeedle", "status-bar.json");

    private static string _path = DefaultPath;

    internal static void SetStorageLocationForTests(string path) => _path = path;
    internal static void ResetStorageForTests() => _path = DefaultPath;

    public static bool IsValidId(string id) => All.Any(a => a.Id == id);
    public static StatusBarItem Find(string id) => All.FirstOrDefault(a => a.Id == id);

    public static List<string> GetSelectedIds()
    {
        var ids = ReadRaw().Where(IsValidId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return ids.Count > 0 ? ids : Defaults.ToList();
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
