using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FindNeedleUX.Services;

/// <summary>One customizable Home "Tools" tile: a stable id, a display label, and an emoji.
/// The id maps to <see cref="MainWindow.RunQuickAction"/>.</summary>
public sealed record QuickAction(string Id, string Label, string Emoji);

/// <summary>
/// The catalog of available Home "Tools" tiles plus the user's chosen subset/order. The tiles are
/// purely "jump to a tool": anything that already has a button in the Home cards (open a file / folder /
/// with rules, Known logs, Recent searches, Run search, Open results) is deliberately NOT in this catalog,
/// so the row never repeats the cards above it. Ids that used to be here are still accepted by
/// <see cref="MainWindow.RunQuickAction"/> (the palette and MCP use them); they are just not pinnable.
/// Persisted to a JSON file under <c>%LocalAppData%\FindNeedle\</c> (not WinRT LocalSettings, which
/// throws when the app runs unpackaged) so customization actually sticks. A storage seam lets tests
/// redirect persistence to a temp file.
/// </summary>
public static class QuickActionCatalog
{
    /// <summary>Every tool a user can pin to the Home Tools row. Ids are stable (persisted).</summary>
    public static readonly IReadOnlyList<QuickAction> All = new[]
    {
        new QuickAction("locations",        "Sources",             "📍"),
        new QuickAction("rules_config",     "Rule files",          "⚙️"),
        new QuickAction("auto_rules",       "Auto rules",          "✨"),
        new QuickAction("inspect_etl",      "Inspect ETL",         "🔬"),
        new QuickAction("diagram",          "Diagram tools",       "📈"),
        new QuickAction("processor_output", "Outputs",             "🖼️"),
        new QuickAction("open_ado",         "Open ADO work item",  "🔷"),
        new QuickAction("open_github",      "Open GitHub issue",   "🐙"),
        new QuickAction("open_kusto",       "Open Kusto query",    "🔎"),
    };

    /// <summary>The default set shown until the user customizes: the tools you reach for once a log is
    /// loaded. (A stored selection that only names retired ids falls back to this.)</summary>
    public static readonly IReadOnlyList<string> Defaults = new[] { "locations", "rules_config", "inspect_etl", "diagram", "processor_output" };

    private static readonly string DefaultPath = Path.Combine(
        FindNeedleCoreUtils.PackagedAppPaths.LocalAppData,
        "FindNeedle", "quick-actions.json");

    private static string _path = DefaultPath;

    // --- Test seam ---
    internal static void SetStorageLocationForTests(string path) => _path = path;
    internal static void ResetStorageForTests() => _path = DefaultPath;

    public static bool IsValidId(string id) => All.Any(a => a.Id == id);

    public static QuickAction Find(string id) => All.FirstOrDefault(a => a.Id == id);

    /// <summary>The user's chosen action ids, in order. Drops unknown/duplicate ids; falls back to the
    /// defaults when nothing valid is stored.</summary>
    public static List<string> GetSelectedIds()
    {
        var ids = ReadRaw()
            .Where(IsValidId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return ids.Count > 0 ? ids : Defaults.ToList();
    }

    /// <summary>Persist the given order (cleaned of unknown/duplicate ids).</summary>
    public static void SetSelectedIds(IEnumerable<string> ids)
    {
        var clean = (ids ?? Enumerable.Empty<string>())
            .Where(IsValidId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        WriteRaw(clean);
        Changed?.Invoke();
    }

    /// <summary>Raised after the selection changes (add/remove/reorder), so anything mirroring the
    /// welcome page's tiles can refresh.</summary>
    public static event Action Changed;

    /// <summary>Add an action to the end (no-op if unknown or already present). Returns the new list.</summary>
    public static List<string> Add(string id)
    {
        var ids = GetSelectedIds();
        if (IsValidId(id) && !ids.Contains(id, StringComparer.OrdinalIgnoreCase))
            ids.Add(id);
        SetSelectedIds(ids);
        return ids;
    }

    /// <summary>Remove an action. Returns the new list.</summary>
    public static List<string> Remove(string id)
    {
        var ids = GetSelectedIds();
        ids.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
        SetSelectedIds(ids);
        return ids;
    }

    /// <summary>Move an action by <paramref name="delta"/> positions (clamped). Returns the new list.</summary>
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

    /// <summary>Actions not currently selected, in catalog order (for the "add" picker).</summary>
    public static List<QuickAction> Available()
    {
        var selected = new HashSet<string>(GetSelectedIds(), StringComparer.OrdinalIgnoreCase);
        return All.Where(a => !selected.Contains(a.Id)).ToList();
    }

    // --- storage ---
    private static List<string> ReadRaw()
    {
        try
        {
            if (!File.Exists(_path)) return new List<string>();
            var json = File.ReadAllText(_path);
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
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
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"QuickActionCatalog.WriteRaw failed: {ex.Message}"); }
    }
}
