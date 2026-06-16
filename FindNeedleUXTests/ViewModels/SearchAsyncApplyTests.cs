using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FindNeedleUX;
using FindNeedleUX.Pages.NativeResultViewer;
using FindNeedleUX.Services.PagedLogSource;
using FindNeedlePluginLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Tests for <see cref="NativeResultsPageViewModel.ApplyFiltersAsync"/> — the off-UI-thread search
/// that keeps a slow query from freezing the window. Verifies it filters correctly and that a
/// cancelled (superseded) search does not clobber the visible results with stale data.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
public class SearchAsyncApplyTests
{
    private static NativeResultsPageViewModel NewVmWith(IList<LogLine> rows)
    {
        var vm = new NativeResultsPageViewModel();
        vm.SetSourceForTests(new InMemoryPagedSource(rows));
        return vm;
    }

    /// <summary>10 rows; every 'needleEvery'-th row's text contains the token.</summary>
    private static List<LogLine> Rows(int n, int needleEvery, string token)
    {
        var list = new List<LogLine>(n);
        for (int i = 0; i < n; i++)
            list.Add(new LogLine(new R(i % needleEvery == 0 ? $"{token} row {i}" : $"plain row {i}"), i));
        return list;
    }

    [TestMethod]
    public async Task ApplyFiltersAsync_FiltersToMatchingRows()
    {
        var vm = NewVmWith(Rows(30, needleEvery: 3, token: "needle")); // 10 of 30 match

        Assert.IsTrue(vm.SetSearchTextDeferred("needle"));
        await vm.ApplyFiltersAsync(CancellationToken.None);

        Assert.AreEqual(10, vm.TotalFilteredCount, "count should reflect only matching rows");
        Assert.AreEqual(10, vm.Results.Count, "first page should hold the matches");
        Assert.IsTrue(vm.Results.All(r => r.SearchableData.Contains("needle")), "every shown row matches");
    }

    [TestMethod]
    public async Task ApplyFiltersAsync_EmptyTerm_ReturnsAllRows()
    {
        var vm = NewVmWith(Rows(20, needleEvery: 2, token: "needle"));

        // page size default is 100, so all 20 fit on page 1
        vm.SetSearchTextDeferred("");
        await vm.ApplyFiltersAsync(CancellationToken.None);

        Assert.AreEqual(20, vm.TotalFilteredCount);
        Assert.AreEqual(20, vm.Results.Count);
    }

    [TestMethod]
    public async Task ApplyFiltersAsync_Cancelled_DoesNotPublishStaleResults()
    {
        var vm = NewVmWith(Rows(30, needleEvery: 3, token: "needle"));

        // First, a real search lands 10 "needle" rows.
        vm.SetSearchTextDeferred("needle");
        await vm.ApplyFiltersAsync(CancellationToken.None);
        Assert.AreEqual(10, vm.Results.Count);

        // Now a superseded search (token already cancelled): it must NOT replace the visible rows.
        vm.SetSearchTextDeferred("plain");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await vm.ApplyFiltersAsync(cts.Token);

        Assert.AreEqual(10, vm.Results.Count, "stale/cancelled search must not clobber results");
        Assert.IsTrue(vm.Results.All(r => r.SearchableData.Contains("needle")), "still showing the prior results");
    }

    [TestMethod]
    public void SetSearchTextDeferred_Unchanged_ReturnsFalse()
    {
        var vm = NewVmWith(Rows(5, needleEvery: 1, token: "needle"));
        Assert.IsTrue(vm.SetSearchTextDeferred("abc"), "first set changes the term");
        Assert.IsFalse(vm.SetSearchTextDeferred("abc"), "re-setting the same term is a no-op");
        Assert.IsTrue(vm.SetSearchTextDeferred("abcd"), "a different term changes again");
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
