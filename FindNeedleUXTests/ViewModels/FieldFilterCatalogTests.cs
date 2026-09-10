using System;
using System.Collections.Generic;
using System.Linq;
using FindNeedlePluginLib;
using FindNeedleUX;
using FindNeedleUX.Pages.NativeResultViewer;
using FindNeedleUX.Services.PagedLogSource;
using FindPluginCore.Searching.Query;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// The filter pane's field list is dynamic: the four built-ins can be removed, and any other column
/// the ENGINE can filter can be added. "Can filter" is the load-bearing claim — an added field has to
/// resolve to a real query field or its box would silently do nothing — so these check the catalog
/// against LogQuery and then drive an added filter end to end through the view model.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
public class FieldFilterCatalogTests
{
    [TestMethod]
    public void EveryOfferedField_IsAFieldTheQueryEngineKnows()
    {
        foreach (var kv in FilterFieldCatalog.All)
            Assert.IsTrue(LogQuery.IsField(kv.Key),
                $"'{kv.Key}' is offered in the Add field menu but LogQuery can't filter it — its box would do nothing");
    }

    [TestMethod]
    public void TheFourBuiltInsAreOffered_AndFlaggedAsBuiltIn()
    {
        foreach (var f in new[] { "provider", "taskname", "message", "source" })
        {
            Assert.IsTrue(FilterFieldCatalog.IsKnown(f), f);
            Assert.IsTrue(FilterFieldCatalog.IsBuiltIn(f), f);
        }
        Assert.IsFalse(FilterFieldCatalog.IsBuiltIn("processid"),
            "ProcessId has no FilterSpec slot — it goes through the query path");
    }

    [TestMethod]
    public void TimeAndLevel_AreNotFieldRows_TheyHaveTheirOwnSections()
    {
        Assert.IsFalse(FilterFieldCatalog.IsKnown("time"));
        Assert.IsFalse(FilterFieldCatalog.IsKnown("level"));
    }

    [TestMethod]
    public void DisplayNames_AreTheColumnLabels()
    {
        Assert.AreEqual("ProcessId", FilterFieldCatalog.DisplayOf("processid"));
        Assert.AreEqual("TaskName", FilterFieldCatalog.DisplayOf("taskname"));
        Assert.AreEqual("banana", FilterFieldCatalog.DisplayOf("banana")); // unknown falls back to itself
    }

    [TestMethod]
    public void AddedFieldFilter_ActuallyNarrowsTheResults()
    {
        var rows = new List<R>();
        for (int i = 0; i < 12; i++) rows.Add(new R("row " + i, pid: (i % 3 == 0) ? "1234" : "77"));
        var vm = new NativeResultsPageViewModel();
        vm.SetSourceForTests(new InMemoryPagedSource(rows.Select((r, i) => new LogLine(r, i)).ToList()));
        vm.ApplyFiltersSync();
        Assert.AreEqual(12, vm.TotalFilteredCount);

        vm.SetExtraFieldFilter("processid", "1234");
        vm.ApplyFiltersSync(); // the UI path is debounced; force it for the assertion

        Assert.AreEqual(4, vm.TotalFilteredCount, "an added ProcessId filter must narrow like the built-in fields do");
        Assert.IsTrue(vm.GetRows(0, 100).All(r => r.ProcessId == "1234"));
    }

    [TestMethod]
    public void RemovingAnAddedFieldFilter_RestoresEverything()
    {
        var rows = new List<R>();
        for (int i = 0; i < 12; i++) rows.Add(new R("row " + i, pid: (i % 3 == 0) ? "1234" : "77"));
        var vm = new NativeResultsPageViewModel();
        vm.SetSourceForTests(new InMemoryPagedSource(rows.Select((r, i) => new LogLine(r, i)).ToList()));

        vm.SetExtraFieldFilter("processid", "1234");
        vm.ApplyFiltersSync();
        Assert.AreEqual(1, vm.ExtraFieldFilters.Count);
        Assert.IsTrue(vm.HasActiveFilter());

        vm.SetExtraFieldFilter("processid", "");   // what the row's X does
        vm.ApplyFiltersSync();

        Assert.AreEqual(0, vm.ExtraFieldFilters.Count);
        Assert.IsFalse(vm.HasActiveFilter());
        Assert.AreEqual(12, vm.TotalFilteredCount);
    }

    [TestMethod]
    public void AddedFields_AndTheSearchBox_AreAnded_NotReplaced()
    {
        // Both ride on FilterSpec.Query, so the risk is one clobbering the other.
        var rows = new List<R>
        {
            new("alpha", pid: "1234"),
            new("alpha", pid: "77"),
            new("beta",  pid: "1234"),
        };
        var vm = new NativeResultsPageViewModel();
        vm.SetSourceForTests(new InMemoryPagedSource(rows.Select((r, i) => new LogLine(r, i)).ToList()));

        vm.SetExtraFieldFilter("processid", "1234");
        vm.SetFiltersBulk("msg ~ alpha", null, null, null, null, null, null, null);

        Assert.AreEqual(1, vm.TotalFilteredCount, "the structured query and the added field filter must AND");
    }

    [TestMethod]
    public void ClearFilters_DropsAddedFields()
    {
        var vm = new NativeResultsPageViewModel();
        vm.SetSourceForTests(new InMemoryPagedSource(new List<LogLine>()));
        vm.SetExtraFieldFilter("channel", "Operational");
        Assert.AreEqual(1, vm.ExtraFieldFilters.Count);

        vm.ClearFilters();

        Assert.AreEqual(0, vm.ExtraFieldFilters.Count);
    }

    private sealed class R : ISearchResult
    {
        private readonly string _m;
        private readonly string _pid;
        public R(string m, string pid = "0") { _m = m; _pid = pid; }
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
        public string GetProcessId() => _pid;
    }
}
