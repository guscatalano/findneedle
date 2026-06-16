using System;
using System.IO;
using FindNeedleUX.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Tests for <see cref="ResultsViewerSettings"/>: the pure validation/normalization helpers
/// (TESTING_PLAN.md U-B8 + the details-panel clamp) and the file round-trip (U-B7). The round-trip
/// tests use the internal storage seam to redirect persistence to a temp file, so they never touch
/// the dev's real viewer-settings.json. The class is [DoNotParallelize] because it mutates the
/// static singleton's redirected path/cache.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
[DoNotParallelize]
public class ResultsViewerSettingsTests
{
    [TestMethod]
    public void ClampDetailsPanelHeight_BelowMin_ClampsToMin()
    {
        Assert.AreEqual(ResultsViewerSettings.MinDetailsPanelHeight,
            ResultsViewerSettings.ClampDetailsPanelHeight(ResultsViewerSettings.MinDetailsPanelHeight - 50));
    }

    [TestMethod]
    public void ClampDetailsPanelHeight_AboveMax_ClampsToMax()
    {
        Assert.AreEqual(ResultsViewerSettings.MaxDetailsPanelHeight,
            ResultsViewerSettings.ClampDetailsPanelHeight(ResultsViewerSettings.MaxDetailsPanelHeight + 1000));
    }

    [TestMethod]
    public void ClampDetailsPanelHeight_InRange_Preserved()
    {
        Assert.AreEqual(240d, ResultsViewerSettings.ClampDetailsPanelHeight(240d));
    }

    // ── U-B7: write → reload-from-disk → values persisted ──────────────────────

    [TestMethod]
    public void RoundTrip_WriteThenReload_PersistsValues()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"viewer-settings-{Guid.NewGuid():N}.json");
        try
        {
            ResultsViewerSettings.SetStorageLocationForTests(tmp);

            ResultsViewerSettings.ThemeName = "Dark";
            ResultsViewerSettings.PageSize = 250;
            ResultsViewerSettings.SetColumnVisibility("Source", true);

            Assert.IsTrue(File.Exists(tmp), "Save should have written the redirected file");

            // Drop the in-memory cache so the next access re-reads the file we just wrote.
            ResultsViewerSettings.ReloadFromDiskForTests();

            Assert.AreEqual("Dark", ResultsViewerSettings.ThemeName);
            Assert.AreEqual(250, ResultsViewerSettings.PageSize);
            Assert.IsTrue(ResultsViewerSettings.ColumnVisibility["Source"]);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            ResultsViewerSettings.ResetStorageForTests();
        }
    }

}
