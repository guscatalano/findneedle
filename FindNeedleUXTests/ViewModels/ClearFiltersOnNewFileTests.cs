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
/// Tests for <see cref="NativeResultsPageViewModel.ClearFiltersNoReload"/> — the reset the viewer runs
/// when a new file is opened so its rows aren't hidden behind the previous file's filters. It must wipe
/// every filter field without firing a search (the caller reloads), and a query afterward must see all rows.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
public class ClearFiltersOnNewFileTests
{
    private static NativeResultsPageViewModel NewVmWith(IList<LogLine> rows)
    {
        var vm = new NativeResultsPageViewModel();
        vm.SetSourceForTests(new InMemoryPagedSource(rows));
        return vm;
    }

    private static List<LogLine> Rows(int n, int needleEvery, string token)
    {
        var list = new List<LogLine>(n);
        for (int i = 0; i < n; i++)
            list.Add(new LogLine(new R(i % needleEvery == 0 ? $"{token} row {i}" : $"plain row {i}"), i));
        return list;
    }

    [TestMethod]
    public void ClearFiltersNoReload_ResetsEveryFilterField()
    {
        var vm = NewVmWith(Rows(10, needleEvery: 1, token: "needle"));

        // Set every filter to a non-default value (setters apply against the in-memory source).
        vm.SearchText = "needle";
        vm.ProviderFilter = "prov";
        vm.TaskNameFilter = "task";
        vm.MessageFilter = "msg";
        vm.SourceFilter = "src";
        vm.LevelFilter = "Error";
        vm.FromDate = new DateTime(2026, 1, 1);
        vm.ToDate = new DateTime(2026, 2, 1);

        vm.ClearFiltersNoReload();

        Assert.AreEqual("", vm.SearchText);
        Assert.AreEqual("", vm.ProviderFilter);
        Assert.AreEqual("", vm.TaskNameFilter);
        Assert.AreEqual("", vm.MessageFilter);
        Assert.AreEqual("", vm.SourceFilter);
        Assert.AreEqual("", vm.LevelFilter);
        Assert.IsNull(vm.FromDate);
        Assert.IsNull(vm.ToDate);
    }

    [TestMethod]
    public async Task ClearFiltersNoReload_ThenReapply_ReturnsAllRows()
    {
        var vm = NewVmWith(Rows(30, needleEvery: 3, token: "needle")); // 10 of 30 match "needle"

        Assert.IsTrue(vm.SetSearchTextDeferred("needle"));
        await vm.ApplyFiltersAsync(CancellationToken.None);
        Assert.AreEqual(10, vm.TotalFilteredCount, "precondition: filtered down to the matching rows");

        // Clearing for a new file must not leave the old term in effect when results are re-queried.
        vm.ClearFiltersNoReload();
        await vm.ApplyFiltersAsync(CancellationToken.None);

        Assert.AreEqual(30, vm.TotalFilteredCount, "after clearing, every row is visible again");
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
