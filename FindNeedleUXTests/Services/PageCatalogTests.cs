using System.Linq;
using FindNeedleUX.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Services;

/// <summary>
/// <see cref="PageCatalog"/> is the one string table behind the menu labels, the breadcrumb, the page
/// headings, and the command palette. These tests pin its invariants: every navigable page is registered
/// with a section that is a real menu title, and no two pages share a title (a duplicate title would make
/// the breadcrumb ambiguous and the palette show two identical rows).
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
public class PageCatalogTests
{
    private const string Pages = "FindNeedleUX.Pages.";

    // Every page the shell can navigate contentFrame to (MainWindow.ExecuteMenuActionAsync / RunQuickAction /
    // the Rules hub tabs). Adding a page without registering it here AND in PageCatalog fails the test.
    private static readonly string[] NavigablePages =
    {
        "WelcomePage", "CachedSearchesPage", "LogFinderPage",
        "SearchLocationsPage", "RulesPage", "SearchRulesPage", "AutoAddRulesPage", "ReformatRulesPage",
        "SearchProcessorsPage", "ConnectionsPage",
        "RunSearchPage", "NativeResultViewer.NativeResultsPage", "ProcessorOutputPage", "SearchStatisticsPage",
        "WppSymbolResolutionPage", "DiagramToolsPage", "PluginsPage", "PluginConfigPage",
        "SystemInfoPage", "LogsPage", "PerformanceBenchmarkPage",
        "AboutPage", "ResultsViewerSettingsPage",
    };

    [TestMethod]
    public void EveryNavigablePage_HasASectionAndATitle()
    {
        foreach (var page in NavigablePages)
        {
            var info = PageCatalog.Find(Pages + page);
            Assert.IsNotNull(info, $"{page} is not registered in PageCatalog");
            Assert.IsFalse(string.IsNullOrWhiteSpace(info.Section), $"{page} has no section");
            Assert.IsFalse(string.IsNullOrWhiteSpace(info.Title), $"{page} has no title");
        }
    }

    [TestMethod]
    public void EverySection_IsAMenuTitle()
    {
        foreach (var (type, info) in PageCatalog.All.Select(kv => (kv.Key, kv.Value)))
            Assert.IsTrue(PageCatalog.Sections.Contains(info.Section),
                $"{type} is filed under '{info.Section}', which is not a menu section");
    }

    [TestMethod]
    public void Titles_AreUnique()
    {
        var dupes = PageCatalog.Titles
            .GroupBy(t => t, System.StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.AreEqual(0, dupes.Count, "Duplicate page titles: " + string.Join(", ", dupes));
    }

    [TestMethod]
    public void Titles_UseTheAgreedVocabulary_NoEmoji_NoStaleNames()
    {
        // The terminology table: one word per concept. These are the renames the redesign made.
        Assert.AreEqual("Sources", PageCatalog.Find(Pages + "SearchLocationsPage").Title);
        Assert.AreEqual("Recent searches", PageCatalog.Find(Pages + "CachedSearchesPage").Title);
        Assert.AreEqual("Known logs", PageCatalog.Find(Pages + "LogFinderPage").Title);
        Assert.AreEqual("Outputs", PageCatalog.Find(Pages + "ProcessorOutputPage").Title);
        Assert.AreEqual("Search timing", PageCatalog.Find(Pages + "SearchStatisticsPage").Title);
        Assert.AreEqual("App log", PageCatalog.Find(Pages + "LogsPage").Title);
        Assert.AreEqual("Run search", PageCatalog.Find(Pages + "RunSearchPage").Title);
        Assert.AreEqual("System check", PageCatalog.Find(Pages + "SystemInfoPage").Title);
        Assert.AreEqual("Settings", PageCatalog.Find(Pages + "ResultsViewerSettingsPage").Title);

        foreach (var title in PageCatalog.Titles)
        {
            Assert.IsTrue(title.All(c => c < 0x2190 || c == '▸'), $"'{title}' contains a symbol/emoji");
            Assert.IsFalse(title.Contains("Location"), $"'{title}' says Location; the word is Source");
            Assert.IsFalse(title.Contains("Cache"), $"'{title}' says Cache; the word is Recent");
        }
    }

    [TestMethod]
    public void Breadcrumb_IsSectionAndTitle_ExceptWhenTheyMatch()
    {
        Assert.AreEqual("Workspace ▸ Sources", PageCatalog.Breadcrumb(PageCatalog.Find(Pages + "SearchLocationsPage")));
        Assert.AreEqual("Run ▸ Results", PageCatalog.Breadcrumb(PageCatalog.Find(Pages + "NativeResultViewer.NativeResultsPage")));
        Assert.AreEqual("Diagnostics ▸ App log", PageCatalog.Breadcrumb(PageCatalog.Find(Pages + "LogsPage")));
        Assert.AreEqual("Home", PageCatalog.Breadcrumb(PageCatalog.Find(Pages + "WelcomePage")));
        Assert.AreEqual("Settings", PageCatalog.Breadcrumb(PageCatalog.Find(Pages + "ResultsViewerSettingsPage")));
        Assert.AreEqual("", PageCatalog.Breadcrumb(null));
    }

    [TestMethod]
    public void OpenWithRules_IsAnAction_NotAPage()
    {
        // F14: the old QuickLogWithRulesPage was a third search flow. "Open with rules…" now runs through the
        // same open path as every other open, so it must not be registered as a navigable page.
        Assert.IsNull(PageCatalog.Find(Pages + "QuickLogWithRulesPage"));
    }

    [TestMethod]
    public void Find_UnknownPage_ReturnsNull()
    {
        Assert.IsNull(PageCatalog.Find("FindNeedleUX.Pages.NoSuchPage"));
        Assert.IsNull(PageCatalog.Find((string)null));
    }
}
