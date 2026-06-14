using FindNeedleUX.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Tests for the pure validation/normalization helpers on <see cref="ResultsViewerSettings"/>
/// (TESTING_PLAN.md U-B8 + the details-panel clamp). These exercise the value rules without
/// touching the static file-backed state, so they don't mutate the dev's real viewer-settings.json.
/// (U-B7 full file round-trip is deferred — it needs a storage-path seam on the static singleton.)
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
public class ResultsViewerSettingsTests
{
    // U-B8: WebViewerServerSideThreshold rejects non-positive values.
    [DataTestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(-10000)]
    public void NormalizeThreshold_NonPositive_FallsBackToDefault(int bad)
    {
        Assert.AreEqual(ResultsViewerSettings.DefaultWebViewerServerSideThreshold,
            ResultsViewerSettings.NormalizeThreshold(bad));
    }

    [DataTestMethod]
    [DataRow(1)]
    [DataRow(5000)]
    [DataRow(250000)]
    public void NormalizeThreshold_Positive_Preserved(int good)
    {
        Assert.AreEqual(good, ResultsViewerSettings.NormalizeThreshold(good));
    }

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
}
