using FindNeedleUX.Pages.NativeResultViewer;
using FindNeedleUX.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Pure text/mapping logic behind the result viewer's toolbar redesign (F4 / F10):
/// the "Rule filter" caption that says WHY it is off, the single progress banner's composed text
/// (streaming and/or index build — never two indicators), and the segmented controls' index mapping.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
public class ViewerToolbarTextTests
{
    // ----- Rule filter caption -----

    [TestMethod]
    public void RuleFilterLabel_NoRuleFiles_SaysWhy()
    {
        Assert.AreEqual("Rule filter (no rule files loaded)", NativeResultsPageViewModel.RuleFilterLabel(0, streaming: false));
        Assert.AreEqual("Rule filter (no rule files loaded)", NativeResultsPageViewModel.RuleFilterLabel(0, streaming: true),
            "no files trumps still-loading: the user needs rule files first");
        Assert.AreEqual("Rule filter (no rule files loaded)", NativeResultsPageViewModel.RuleFilterLabel(-1, streaming: false));
    }

    [TestMethod]
    public void RuleFilterLabel_StillStreaming_SaysAvailableAfterLoading()
    {
        Assert.AreEqual("Rule filter (available after loading)", NativeResultsPageViewModel.RuleFilterLabel(2, streaming: true));
    }

    [TestMethod]
    public void RuleFilterLabel_Enabled_ShowsFileCount()
    {
        Assert.AreEqual("Rule filter · 1 file", NativeResultsPageViewModel.RuleFilterLabel(1, streaming: false));
        Assert.AreEqual("Rule filter · 3 files", NativeResultsPageViewModel.RuleFilterLabel(3, streaming: false));
    }

    // ----- Progress banner text -----

    private const string Streaming = "Loading logs — 1,240,118 rows so far and rising. You can search and scroll now; results keep filling in.";

    [TestMethod]
    public void Banner_StreamingOnly_IsTheStreamingText()
    {
        Assert.AreEqual(Streaming, NativeResultsPageViewModel.ComposeProgressBanner(true, Streaming, false, ""));
        Assert.AreEqual(Streaming, NativeResultsPageViewModel.ComposeProgressBanner(true, Streaming, false, "Building search index… 1 / 2 (50%)"),
            "index text is ignored while IsIndexing is false");
    }

    [TestMethod]
    public void Banner_StreamingAndIndexing_AppendsSecondaryPhrase_WithPercent()
    {
        var text = NativeResultsPageViewModel.ComposeProgressBanner(true, Streaming, true, "Building search index… 2,000 / 5,000 (40%)");
        Assert.AreEqual(Streaming + " · index building in background (40%)", text);
    }

    [TestMethod]
    public void Banner_StreamingAndIndexing_NoPercentYet_AppendsPlainPhrase()
    {
        var text = NativeResultsPageViewModel.ComposeProgressBanner(true, Streaming, true, "Building search index… starting…");
        Assert.AreEqual(Streaming + " · index building in background", text);
        Assert.AreEqual(Streaming + " · index building in background", NativeResultsPageViewModel.ComposeProgressBanner(true, Streaming, true, null));
    }

    [TestMethod]
    public void Banner_IndexingOnly_ShowsIndexStatusAndWhyItMatters()
    {
        var text = NativeResultsPageViewModel.ComposeProgressBanner(false, Streaming, true, "Building search index… 2,000 / 5,000 (40%)");
        Assert.AreEqual("Building search index… 2,000 / 5,000 (40%) — text search uses a slower scan until it finishes.", text);
        StringAssert.StartsWith(NativeResultsPageViewModel.ComposeProgressBanner(false, Streaming, true, ""), "Building search index…");
    }

    [TestMethod]
    public void Banner_NothingRunning_IsEmpty()
    {
        Assert.AreEqual("", NativeResultsPageViewModel.ComposeProgressBanner(false, Streaming, false, "anything"));
    }

    [TestMethod]
    public void Banner_StreamingWithBlankText_FallsBackToLoading()
    {
        Assert.AreEqual("Loading logs…", NativeResultsPageViewModel.ComposeProgressBanner(true, "  ", false, ""));
    }

    [TestMethod]
    public void Banner_ViewModel_VisibleWhenStreamingOrIndexing()
    {
        var vm = new NativeResultsPageViewModel();
        Assert.IsFalse(vm.IsProgressBannerVisible);
        vm.IsIndexing = true;
        Assert.IsTrue(vm.IsProgressBannerVisible, "index build alone shows the banner (no separate toolbar indicator)");
        vm.IsIndexing = false;
        vm.IsStreaming = true;
        Assert.IsTrue(vm.IsProgressBannerVisible);
        vm.IsStreaming = false;
        Assert.IsFalse(vm.IsProgressBannerVisible);
    }

    [TestMethod]
    public void Banner_ViewModel_RaisesPropertyChangedForComposedText()
    {
        var vm = new NativeResultsPageViewModel();
        var raised = new System.Collections.Generic.List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.IsIndexing = true;
        vm.IndexStatusText = "Building search index… 1 / 4 (25%)";
        CollectionAssert.Contains(raised, nameof(NativeResultsPageViewModel.IsProgressBannerVisible));
        CollectionAssert.Contains(raised, nameof(NativeResultsPageViewModel.ProgressBannerText));
        StringAssert.Contains(vm.ProgressBannerText, "(25%)");
    }

    // ----- Segmented controls -----

    [TestMethod]
    public void FiltersSegment_HideWinsOverDock_ElseFollowsDock()
    {
        Assert.AreEqual(NativeResultsPageViewModel.FiltersSegLeft, NativeResultsPageViewModel.FiltersSegmentIndexFor(FilterDock.Left, expanded: true));
        Assert.AreEqual(NativeResultsPageViewModel.FiltersSegTop, NativeResultsPageViewModel.FiltersSegmentIndexFor(FilterDock.Top, expanded: true));
        Assert.AreEqual(NativeResultsPageViewModel.FiltersSegHide, NativeResultsPageViewModel.FiltersSegmentIndexFor(FilterDock.Left, expanded: false));
        Assert.AreEqual(NativeResultsPageViewModel.FiltersSegHide, NativeResultsPageViewModel.FiltersSegmentIndexFor(FilterDock.Top, expanded: false));
    }

    [TestMethod]
    public void DetailsSegment_RoundTripsEveryMode()
    {
        foreach (var mode in new[] { DetailsMode.Inrow, DetailsMode.BottomPanel, DetailsMode.Popup })
            Assert.AreEqual(mode, NativeResultsPageViewModel.DetailsModeForSegment(NativeResultsPageViewModel.DetailsSegmentIndexFor(mode)));
        Assert.AreEqual(0, NativeResultsPageViewModel.DetailsSegmentIndexFor(DetailsMode.Inrow), "In row is the first segment (and the default)");
        Assert.AreEqual(DetailsMode.Inrow, NativeResultsPageViewModel.DetailsModeForSegment(-1), "an unknown index falls back to In row");
    }

    [TestMethod]
    public void ToolbarIds_IncludeTheNewFirstLevelControls()
    {
        var ids = (System.Collections.ICollection)ResultsViewerSettings.ToolbarButtonIds;
        CollectionAssert.Contains(ids, "FilterPlacement");
        CollectionAssert.Contains(ids, "DetailsMode");
        CollectionAssert.Contains(ids, "RuleFilter");
        // The old hide/show button and View ▾ menu are gone; a saved hide for them must not carry over.
        CollectionAssert.DoesNotContain(ids, "Filters");
        CollectionAssert.DoesNotContain(ids, "View");
    }
}
