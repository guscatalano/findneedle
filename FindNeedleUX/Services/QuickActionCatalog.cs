using System;
using System.Collections.Generic;
using System.Linq;

namespace FindNeedleUX.Services;

/// <summary>One customizable welcome-page quick action: a stable id, a display label, and an emoji.
/// The id maps to <see cref="MainWindow.RunQuickAction"/>.</summary>
public sealed record QuickAction(string Id, string Label, string Emoji);

/// <summary>
/// The catalog of available welcome-page quick actions plus the user's chosen subset/order, persisted
/// to <c>LocalSettings</c> so the welcome page can be customized.
/// </summary>
public static class QuickActionCatalog
{
    /// <summary>Every action a user can pin to the welcome page.</summary>
    public static readonly IReadOnlyList<QuickAction> All = new[]
    {
        new QuickAction("open_file",        "Open Log File",       "📁"),
        new QuickAction("open_folder",      "Open Folder",         "📂"),
        new QuickAction("open_rules",       "Open Log with Rules", "📝"),
        new QuickAction("cached",           "Cached Searches",     "🕑"),
        new QuickAction("locations",        "Configure Locations", "📍"),
        new QuickAction("rules_config",     "Configure Rules",     "⚙️"),
        new QuickAction("auto_rules",       "Auto-add Rules",      "✨"),
        new QuickAction("run_search",       "Run Search",          "▶️"),
        new QuickAction("results",          "View Results",        "📊"),
        new QuickAction("processor_output", "Processor Output",    "🖼️"),
        new QuickAction("diagram",          "Diagram Tools",       "📈"),
        new QuickAction("inspect_etl",      "Inspect ETL",         "🔬"),
    };

    private static readonly string[] Defaults = { "open_file", "open_folder", "open_rules" };
    private const string SettingKey = "WelcomeQuickActions";

    public static QuickAction Find(string id) => All.FirstOrDefault(a => a.Id == id);

    /// <summary>The user's chosen action ids, in order. Falls back to the defaults; drops any ids no
    /// longer in the catalog.</summary>
    public static List<string> GetSelectedIds()
    {
        string raw = null;
        try { raw = global::Windows.Storage.ApplicationData.Current.LocalSettings.Values[SettingKey] as string; } catch { }
        var ids = string.IsNullOrWhiteSpace(raw)
            ? Defaults.ToList()
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        ids = ids.Where(id => All.Any(a => a.Id == id)).Distinct().ToList();
        return ids.Count > 0 ? ids : Defaults.ToList();
    }

    public static void SetSelectedIds(IEnumerable<string> ids)
    {
        var clean = ids.Where(id => All.Any(a => a.Id == id)).Distinct();
        try { global::Windows.Storage.ApplicationData.Current.LocalSettings.Values[SettingKey] = string.Join(",", clean); } catch { }
    }
}
