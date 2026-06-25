using System;
using System.IO;
using FindNeedleUX.Services;
using FindPluginCore.Searching;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// "Fresh install" defaults for <see cref="ResultsViewerSettings"/>: with no saved settings file, the
/// first-run values must be sane — especially that OS/agent integrations are OFF until opted in, the
/// core columns are visible while long/correlation columns are hidden, and every clamp range is valid.
/// Uses the storage seam to point at a non-existent temp file so every getter returns its default,
/// and never writes (which would create the file). [DoNotParallelize] — mutates the static singleton.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
[DoNotParallelize]
public class ResultsViewerDefaultsTests
{
    [TestInitialize]
    public void FreshInstall()
    {
        // A path that does not exist → Load() yields defaults; we only ever read below.
        ResultsViewerSettings.SetStorageLocationForTests(
            Path.Combine(Path.GetTempPath(), $"viewer-settings-fresh-{Guid.NewGuid():N}.json"));
        ResultsViewerSettings.ReloadFromDiskForTests();
    }

    [TestCleanup]
    public void Cleanup() => ResultsViewerSettings.ResetStorageForTests();

    [TestMethod]
    public void Defaults_CorePreferencesAreSane()
    {
        Assert.AreEqual("Subtle", ResultsViewerSettings.ThemeName);
        Assert.AreEqual("yyyy-MM-dd HH:mm:ss", ResultsViewerSettings.TimeFormat);
        Assert.AreEqual(100, ResultsViewerSettings.PageSize);
        Assert.IsTrue(ResultsViewerSettings.StreamWhileLoading, "progressive loading on by default");
        Assert.AreEqual(DragDropMode.Prompt, ResultsViewerSettings.DragDropMode);
        Assert.AreEqual(CacheReuseMode.Prompt, ResultsViewerSettings.CacheReuseMode);
        Assert.AreEqual(IndexingMode.Background, ResultsViewerSettings.IndexingMode);
        Assert.AreEqual(SearchSubmitMode.Auto, ResultsViewerSettings.SearchSubmitMode);
        Assert.AreEqual(FilterDock.Top, ResultsViewerSettings.FilterDock);
        Assert.AreEqual("System", ResultsViewerSettings.TitleBarColorMode);
        Assert.IsTrue(ResultsViewerSettings.ShowStatusBar);
        Assert.AreEqual(8765, ResultsViewerSettings.McpServerPort);
        Assert.AreEqual(
            FindPluginCore.GlobalConfiguration.GlobalSettings.NativeResultViewerKey,
            ResultsViewerSettings.DefaultResultViewer);
    }

    [TestMethod]
    public void Defaults_IntegrationsAreOffUntilOptedIn()
    {
        // These touch the OS / expose data, so a fresh install must leave them off.
        Assert.IsFalse(ResultsViewerSettings.McpServerEnabled, "MCP server must be off on a fresh install");
        Assert.IsFalse(ResultsViewerSettings.FileOpenWithEnabled, "Open-with integration must be opt-in");
        Assert.IsFalse(ResultsViewerSettings.FileContextMenuEnabled, "Context-menu integration must be opt-in");
        Assert.IsFalse(ResultsViewerSettings.EnrichmentEnabled, "Enrichment (scan-time cost) must be opt-in");
    }

    [TestMethod]
    public void Defaults_ColumnVisibility_CoreShown_LongAndCorrelationHidden()
    {
        var cols = ResultsViewerSettings.ColumnVisibility;
        foreach (var c in new[] { "Index", "Time", "Provider", "TaskName", "Message", "Level" })
            Assert.IsTrue(cols[c], $"{c} should be visible by default");
        Assert.IsFalse(cols["Source"], "Source (long full path) hidden by default");
        foreach (var c in new[] { "ProcessId", "ThreadId", "EventId", "Raw Row" })
            Assert.IsFalse(cols[c], $"{c} should be hidden by default (opt-in)");
    }

    [TestMethod]
    public void Defaults_ToolbarButtons_AllVisible()
    {
        var tb = ResultsViewerSettings.ToolbarButtonVisibility;
        foreach (var id in ResultsViewerSettings.ToolbarButtonIds)
            Assert.IsTrue(tb[id], $"toolbar button {id} should be visible by default");
    }

    [TestMethod]
    public void Defaults_ClampRanges_AreValid_AndDefaultsSitInside()
    {
        void Check(string name, double min, double def, double max, double actual)
        {
            Assert.IsTrue(min < max, $"{name}: Min({min}) must be < Max({max})");
            Assert.IsTrue(def >= min && def <= max, $"{name}: Default({def}) must be within [{min},{max}]");
            Assert.AreEqual(def, actual, 0.0001, $"{name}: fresh-install value should be the default");
        }
        Check("DetailsPanelHeight", ResultsViewerSettings.MinDetailsPanelHeight,
            ResultsViewerSettings.DefaultDetailsPanelHeight, ResultsViewerSettings.MaxDetailsPanelHeight,
            ResultsViewerSettings.DetailsPanelHeight);
        Check("ScrollBarSize", ResultsViewerSettings.MinScrollBarSize,
            ResultsViewerSettings.DefaultScrollBarSize, ResultsViewerSettings.MaxScrollBarSize,
            ResultsViewerSettings.ScrollBarSize);
        Check("RowFontSize", ResultsViewerSettings.MinRowFontSize,
            ResultsViewerSettings.DefaultRowFontSize, ResultsViewerSettings.MaxRowFontSize,
            ResultsViewerSettings.RowFontSize);
        Check("RowHeightRatio", ResultsViewerSettings.MinRowHeightRatio,
            ResultsViewerSettings.DefaultRowHeightRatio, ResultsViewerSettings.MaxRowHeightRatio,
            ResultsViewerSettings.RowHeightRatio);
        Check("FilterPaneWidth", ResultsViewerSettings.MinFilterPaneWidth,
            ResultsViewerSettings.DefaultFilterPaneWidth, ResultsViewerSettings.MaxFilterPaneWidth,
            ResultsViewerSettings.FilterPaneWidth);
    }
}
