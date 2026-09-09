using System;
using System.Collections.Generic;
using System.Linq;

namespace FindNeedleUX.Services;

/// <summary>Where a page lives in the shell: the menu it belongs to and the one label it is known by.</summary>
public sealed record PageInfo(string Section, string Title);

/// <summary>
/// THE string table for the shell. One entry per navigable page: the menu section it belongs to and the
/// single user-facing title for it. The menu items, the "you are here" breadcrumb, each page's heading,
/// and the Ctrl+K command palette all read from here, so the same word shows up in all of them (a page
/// is never "Cached Searches" in one place and "Recent searches" in another).
///
/// Keyed by the page type's full name (not <see cref="Type"/>) so the table is plain data that unit
/// tests can read without loading WinUI. <see cref="Find(Type)"/> is the convenience for the app.
/// </summary>
public static class PageCatalog
{
    // Menu sections, in menu-bar order. "Home" (Welcome) and "Settings" are reachable outside the menu
    // bar but still need a breadcrumb section.
    public const string Open = "Open";
    public const string Workspace = "Workspace";
    public const string Run = "Run";
    public const string Tools = "Tools";
    public const string Diagnostics = "Diagnostics";
    public const string Help = "Help";
    public const string Settings = "Settings";
    public const string Home = "Home";

    public static readonly IReadOnlyList<string> Sections =
        new[] { Open, Workspace, Run, Tools, Diagnostics, Help, Settings, Home };

    private const string P = "FindNeedleUX.Pages.";

    private static readonly IReadOnlyDictionary<string, PageInfo> Table = new Dictionary<string, PageInfo>(StringComparer.Ordinal)
    {
        [P + "WelcomePage"]                          = new(Home, "Home"),

        // Open — get a log in front of you. ("Open log file…", "Open folder…" and "Open with rules…" are
        // actions, not pages — they run through MainWindow.OpenIntoWorkspaceAsync.)
        [P + "CachedSearchesPage"]                   = new(Open, "Recent searches"),
        [P + "LogFinderPage"]                        = new(Open, "Known logs"),

        // Workspace — what the search is made of.
        [P + "SearchLocationsPage"]                  = new(Workspace, "Sources"),
        [P + "RulesPage"]                            = new(Workspace, "Rules"),           // hub; the tab names the page
        [P + "SearchRulesPage"]                      = new(Workspace, "Rule files"),
        [P + "AutoAddRulesPage"]                     = new(Workspace, "Auto rules"),
        [P + "ReformatRulesPage"]                    = new(Workspace, "Field extraction"),
        [P + "SearchProcessorsPage"]                 = new(Workspace, "Active rules"),
        [P + "ConnectionsPage"]                      = new(Workspace, "Connections"),

        // Run — run it and look at what came out.
        [P + "RunSearchPage"]                        = new(Run, "Run search"),
        [P + "NativeResultViewer.NativeResultsPage"] = new(Run, "Results"),
        [P + "ProcessorOutputPage"]                  = new(Run, "Outputs"),
        [P + "SearchStatisticsPage"]                 = new(Run, "Search timing"),

        // Tools — standalone utilities.
        [P + "WppSymbolResolutionPage"]              = new(Tools, "Symbols (WPP)"),
        [P + "DiagramToolsPage"]                     = new(Tools, "Diagram tools"),
        [P + "PluginsPage"]                          = new(Tools, "Plugins"),
        [P + "PluginConfigPage"]                     = new(Tools, "Plugin configuration"),

        // Diagnostics — the app looking at itself.
        [P + "SystemInfoPage"]                       = new(Diagnostics, "System check"),
        [P + "LogsPage"]                             = new(Diagnostics, "App log"),
        [P + "PerformanceBenchmarkPage"]             = new(Diagnostics, "Performance benchmark"),

        [P + "AboutPage"]                            = new(Help, "About"),
        [P + "ResultsViewerSettingsPage"]            = new(Settings, "Settings"),
    };

    /// <summary>Every registered page, keyed by the page type's full name.</summary>
    public static IReadOnlyDictionary<string, PageInfo> All => Table;

    public static PageInfo Find(Type pageType) => pageType == null ? null : Find(pageType.FullName);

    public static PageInfo Find(string pageTypeFullName)
        => pageTypeFullName != null && Table.TryGetValue(pageTypeFullName, out var info) ? info : null;

    /// <summary>The page's one label — for headings and menu items. Falls back to the type name so an
    /// unregistered page is visibly wrong rather than blank.</summary>
    public static string TitleOf(Type pageType) => Find(pageType)?.Title ?? pageType?.Name ?? "";

    /// <summary>"Workspace ▸ Sources"; a page whose title is its section (Home, Settings) is just the title.</summary>
    public static string Breadcrumb(PageInfo info)
    {
        if (info == null) return "";
        return info.Section == info.Title ? info.Title : $"{info.Section} ▸ {info.Title}";
    }

    /// <summary>Titles across the whole table, for the "every title is unique" invariant.</summary>
    public static IEnumerable<string> Titles => Table.Values.Select(v => v.Title);
}
