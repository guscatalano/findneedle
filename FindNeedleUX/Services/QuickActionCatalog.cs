using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FindNeedleUX.Services;

/// <summary>One customizable welcome-page quick action: a stable id, a display label, and an emoji.
/// The id maps to <see cref="MainWindow.RunQuickAction"/>.</summary>
public sealed record QuickAction(string Id, string Label, string Emoji);

/// <summary>
/// The catalog of available welcome-page quick actions plus the user's chosen subset/order.
/// Persisted to a JSON file under <c>%LocalAppData%\FindNeedle\</c> (not WinRT LocalSettings, which
/// throws when the app runs unpackaged) so customization actually sticks. A storage seam lets tests
/// redirect persistence to a temp file.
/// </summary>
public static class QuickActionCatalog
{
    /// <summary>Every action a user can pin to the welcome page.</summary>
    public static readonly IReadOnlyList<QuickAction> All = new[]
    {
        new QuickAction("open_file",        "Open Log File",       "📁"),
        new QuickAction("open_folder",      "Open Folder",         "📂"),
        new QuickAction("open_rules",       "Open Log with Rules", "📝"),
        new QuickAction("open_ado",         "Open ADO Work Item",  "🔷"),
        new QuickAction("open_github",      "Open GitHub Issue",   "🐙"),
        new QuickAction("open_kusto",       "Open Kusto Query",    "🔎"),
        new QuickAction("cached",           "Recent Searches",     "🕑"),
        new QuickAction("locations",        "Configure Locations", "📍"),
        new QuickAction("rules_config",     "Configure Rules",     "⚙️"),
        new QuickAction("auto_rules",       "Auto-add Rules",      "✨"),
        new QuickAction("run_search",       "Run Search",          "▶️"),
        new QuickAction("results",          "View Results",        "📊"),
        new QuickAction("processor_output", "Processor Output",    "🖼️"),
        new QuickAction("diagram",          "Diagram Tools",       "📈"),
        new QuickAction("inspect_etl",      "Inspect ETL",         "🔬"),
    };

    /// <summary>The default set shown until the user customizes.</summary>
    public static readonly IReadOnlyList<string> Defaults = new[] { "open_file", "open_folder", "open_rules", "cached" };

    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
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

    /// <summary>Raised after the selection changes (add/remove/reorder), so the welcome page and the
    /// top "Quick" menu stay in sync.</summary>
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
