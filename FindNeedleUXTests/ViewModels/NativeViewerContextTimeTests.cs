using System;
using System.Collections.Generic;
using FindNeedleUX;
using FindNeedleUX.Pages.NativeResultViewer;
using FindNeedleUX.Services.PagedLogSource;
using FindNeedlePluginLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Tests for the viewer's row-context + time-range queries (used by the details "context" view and the
/// MCP histogram/summary): <see cref="NativeResultsPageViewModel.GetContext"/>,
/// <see cref="NativeResultsPageViewModel.CountInTimeRange"/>, and
/// <see cref="NativeResultsPageViewModel.GetFilteredTimeRange"/>.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
public class NativeViewerContextTimeTests
{
    private static readonly DateTime Base = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static NativeResultsPageViewModel VmWithTimedRows(int n)
    {
        var rows = new List<LogLine>(n);
        for (int i = 0; i < n; i++) rows.Add(new LogLine(new R(Base.AddMinutes(i), $"row {i}"), i));
        var vm = new NativeResultsPageViewModel();
        vm.SetSourceForTests(new InMemoryPagedSource(rows));
        return vm;
    }

    [TestMethod]
    public void GetContext_ReturnsTargetIndexAndSurroundingRows()
    {
        var (index, rows) = VmWithTimedRows(10).GetContext(rowId: 5, before: 2, after: 2);
        Assert.AreEqual(5, index);
        Assert.AreEqual(5, rows.Count);          // 2 before + target + 2 after
        Assert.AreEqual(3, rows[0].RowId);
        Assert.AreEqual(7, rows[rows.Count - 1].RowId);
    }

    [TestMethod]
    public void GetContext_UnknownRowId_ReturnsMinusOne()
    {
        var (index, rows) = VmWithTimedRows(10).GetContext(rowId: 999, before: 1, after: 1);
        Assert.AreEqual(-1, index);
        Assert.AreEqual(0, rows.Count);
    }

    [TestMethod]
    public void GetContext_ClampsBeforeAfterAtEdges()
    {
        var (index, rows) = VmWithTimedRows(10).GetContext(rowId: 0, before: 999, after: 999);
        Assert.AreEqual(0, index);
        Assert.AreEqual(10, rows.Count); // clamped + no negative start → all rows
    }

    [TestMethod]
    public void GetFilteredTimeRange_ReturnsMinAndMax()
    {
        var (min, max) = VmWithTimedRows(10).GetFilteredTimeRange();
        Assert.AreEqual(Base, min.Value.ToUniversalTime());
        Assert.AreEqual(Base.AddMinutes(9), max.Value.ToUniversalTime());
    }

    [TestMethod]
    public void GetFilteredTimeRange_EmptySet_ReturnsNulls()
    {
        var vm = new NativeResultsPageViewModel();
        vm.SetSourceForTests(new InMemoryPagedSource(Array.Empty<LogLine>()));
        var (min, max) = vm.GetFilteredTimeRange();
        Assert.IsNull(min);
        Assert.IsNull(max);
    }

    [TestMethod]
    public void CountInTimeRange_CountsRowsWithinWindow()
    {
        // times Base+0..Base+9 min; window [Base+2, Base+5] inclusive → rows 2,3,4,5
        Assert.AreEqual(4, VmWithTimedRows(10).CountInTimeRange(Base.AddMinutes(2), Base.AddMinutes(5)));
    }

    private sealed class R : ISearchResult
    {
        private readonly DateTime _t; private readonly string _m;
        public R(DateTime t, string m) { _t = t; _m = m; }
        public DateTime GetLogTime() => _t;
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
