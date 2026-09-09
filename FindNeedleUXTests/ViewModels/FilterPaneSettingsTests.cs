using System;
using System.IO;
using FindNeedleUX.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Shown/hidden + dock handling for the result viewer's filter pane (<see cref="ResultsViewerSettings.FiltersExpanded"/>
/// / <see cref="ResultsViewerSettings.FilterDock"/>). The pane is expanded + docked left by default; a
/// collapse is remembered per dock; and a legacy <c>FiltersExpanded:false</c> written by a build that
/// defaulted the filters to a row across the TOP (no dock recorded) must NOT hide the new left pane.
/// Round-trips through a real temp file. [DoNotParallelize] — mutates the static singleton.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
[DoNotParallelize]
public class FilterPaneSettingsTests
{
    private string _path;

    [TestInitialize]
    public void Init()
    {
        _path = Path.Combine(Path.GetTempPath(), $"viewer-settings-filterpane-{Guid.NewGuid():N}.json");
        ResultsViewerSettings.SetStorageLocationForTests(_path);
    }

    [TestCleanup]
    public void Cleanup()
    {
        ResultsViewerSettings.ResetStorageForTests();
        try { File.Delete(_path); } catch { /* best-effort */ }
    }

    private void WriteSettings(string json)
    {
        File.WriteAllText(_path, json);
        ResultsViewerSettings.ReloadFromDiskForTests();
    }

    [TestMethod]
    public void FreshFile_IsLeftAndExpanded()
    {
        Assert.IsFalse(File.Exists(_path));
        Assert.AreEqual(FilterDock.Left, ResultsViewerSettings.FilterDock);
        Assert.IsTrue(ResultsViewerSettings.FiltersExpanded);
    }

    [TestMethod]
    public void LegacyCollapse_NoDockRecorded_IsResetToExpanded()
    {
        // Written by a build that defaulted to the top row and had no FiltersExpandedDock field: the user
        // collapsed the TOP row back then. That must not hide the (new default) left pane.
        WriteSettings("{ \"FiltersExpanded\": false }");
        Assert.AreEqual(FilterDock.Left, ResultsViewerSettings.FilterDock);
        Assert.IsTrue(ResultsViewerSettings.FiltersExpanded, "legacy collapse (no dock recorded) must not hide the left pane");
    }

    [TestMethod]
    public void LegacyCollapse_WithExplicitLeftDock_ButNoDockRecorded_IsResetOnce()
    {
        // Same legacy shape, but the user had explicitly chosen "Filters on left" and collapsed it before
        // the per-dock record existed. Accepted one-time reset (documented on FiltersExpanded): the next
        // deliberate collapse records the dock and sticks.
        WriteSettings("{ \"FiltersExpanded\": false, \"FilterDock\": \"Left\" }");
        Assert.IsTrue(ResultsViewerSettings.FiltersExpanded);

        ResultsViewerSettings.FiltersExpanded = false; // deliberate collapse under Left
        ResultsViewerSettings.ReloadFromDiskForTests();
        Assert.IsFalse(ResultsViewerSettings.FiltersExpanded, "a deliberate collapse under the current dock sticks");
    }

    [TestMethod]
    public void DeliberateCollapse_UnderLeft_IsHonoredAfterReload()
    {
        ResultsViewerSettings.FiltersExpanded = false;
        ResultsViewerSettings.ReloadFromDiskForTests();
        Assert.AreEqual(FilterDock.Left, ResultsViewerSettings.FilterDock);
        Assert.IsFalse(ResultsViewerSettings.FiltersExpanded);

        ResultsViewerSettings.FiltersExpanded = true;
        ResultsViewerSettings.ReloadFromDiskForTests();
        Assert.IsTrue(ResultsViewerSettings.FiltersExpanded);
    }

    [TestMethod]
    public void Collapse_IsPerDock_SwitchingDockShowsThePaneAgain()
    {
        ResultsViewerSettings.FiltersExpanded = false;              // collapsed the LEFT pane
        ResultsViewerSettings.FilterDock = FilterDock.Top;          // moved filters to the top row
        Assert.IsTrue(ResultsViewerSettings.FiltersExpanded, "a new dock comes up expanded");

        ResultsViewerSettings.FiltersExpanded = false;              // collapsed the TOP row
        ResultsViewerSettings.ReloadFromDiskForTests();
        Assert.AreEqual(FilterDock.Top, ResultsViewerSettings.FilterDock);
        Assert.IsFalse(ResultsViewerSettings.FiltersExpanded, "collapse under Top is honored while Top is active");

        ResultsViewerSettings.FilterDock = FilterDock.Left;         // back to the left rail
        Assert.IsTrue(ResultsViewerSettings.FiltersExpanded, "the top-row collapse says nothing about the left pane");
    }

    [TestMethod]
    public void ExplicitTopDock_IsHonored()
    {
        WriteSettings("{ \"FilterDock\": \"Top\" }");
        Assert.AreEqual(FilterDock.Top, ResultsViewerSettings.FilterDock, "an explicit 'Filters on top' choice persists");
        Assert.IsTrue(ResultsViewerSettings.FiltersExpanded);
    }

    [TestMethod]
    public void CorruptDockValue_FallsBackToLeft()
    {
        WriteSettings("{ \"FilterDock\": \"Sideways\", \"FiltersExpanded\": false, \"FiltersExpandedDock\": \"Sideways\" }");
        Assert.AreEqual(FilterDock.Left, ResultsViewerSettings.FilterDock);
        Assert.IsTrue(ResultsViewerSettings.FiltersExpanded, "a collapse recorded under an unknown dock does not apply to Left");
    }
}
