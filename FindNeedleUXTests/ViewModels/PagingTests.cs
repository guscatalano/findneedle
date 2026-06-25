using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FindNeedleUX;
using FindNeedleUX.Pages.NativeResultViewer;
using FindNeedleUX.Services.PagedLogSource;
using FindNeedlePluginLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Tests for the result viewer's pagination math (<see cref="NativeResultsPageViewModel"/>): TotalPages,
/// page-range text, clamping of CurrentPage, and First/Prev/Next/Last navigation — all pure given a
/// known filtered count, exercised through the in-memory paged-source test seam.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
public class PagingTests
{
    private static async Task<NativeResultsPageViewModel> VmWith(int rowCount, int pageSize)
    {
        var rows = new List<LogLine>(rowCount);
        for (int i = 0; i < rowCount; i++) rows.Add(new LogLine(new R($"row {i}"), i));
        var vm = new NativeResultsPageViewModel();
        vm.SetSourceForTests(new InMemoryPagedSource(rows));
        vm.PageSize = pageSize;
        await vm.ApplyFiltersAsync(CancellationToken.None); // populates TotalFilteredCount
        return vm;
    }

    [TestMethod]
    public async Task TotalPages_CeilingDividesByPageSize()
    {
        Assert.AreEqual(3, (await VmWith(30, 10)).TotalPages);
        Assert.AreEqual(4, (await VmWith(31, 10)).TotalPages);   // remainder rounds up
        Assert.AreEqual(1, (await VmWith(1, 10)).TotalPages);
        Assert.AreEqual(0, (await VmWith(0, 10)).TotalPages);    // empty set
    }

    [TestMethod]
    public async Task PageRangeText_TracksCurrentPage()
    {
        var vm = await VmWith(30, 10);
        Assert.AreEqual("1–10", vm.PageRangeText);
        vm.NextPage();
        Assert.AreEqual("11–20", vm.PageRangeText);
        vm.LastPage();
        Assert.AreEqual("21–30", vm.PageRangeText);
    }

    [TestMethod]
    public async Task PageRangeText_LastPagePartial_ClampsToCount()
    {
        var vm = await VmWith(25, 10);
        vm.LastPage();
        Assert.AreEqual(3, vm.CurrentPage);
        Assert.AreEqual("21–25", vm.PageRangeText); // not 21–30
    }

    [TestMethod]
    public async Task Navigation_ClampsAtBothEnds()
    {
        var vm = await VmWith(30, 10); // 3 pages
        vm.PrevPage();
        Assert.AreEqual(1, vm.CurrentPage, "can't go before page 1");
        vm.GoToPage(0);
        Assert.AreEqual(1, vm.CurrentPage, "page 0 clamps to 1");
        vm.GoToPage(999);
        Assert.AreEqual(3, vm.CurrentPage, "beyond the last page clamps to last");
        vm.NextPage();
        Assert.AreEqual(3, vm.CurrentPage, "can't go past the last page");
        vm.FirstPage();
        Assert.AreEqual(1, vm.CurrentPage);
    }

    [TestMethod]
    public async Task EmptySet_StaysOnPageOne_WithZeroRange()
    {
        var vm = await VmWith(0, 10);
        Assert.AreEqual("0", vm.PageRangeText);
        vm.LastPage();
        Assert.AreEqual(1, vm.CurrentPage);
    }

    private sealed class R : ISearchResult
    {
        private readonly string _m;
        public R(string m) { _m = m; }
        public DateTime GetLogTime() => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public string GetMachineName() => "M";
        public void WriteToConsole() { }
        public Level GetLevel() => Level.Info;
        public string GetUsername() => "u";
        public string GetTaskName() => "t";
        public string GetOpCode() => "";
        public string GetSource() => "s";
        public string GetSearchableData() => _m;
        public string GetMessage() => _m;
        public string GetResultSource() => "rs";
    }
}
