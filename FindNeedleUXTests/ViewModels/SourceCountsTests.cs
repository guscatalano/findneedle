using System;
using System.Collections.Generic;
using FindNeedleUX;
using FindNeedleUX.Pages.NativeResultViewer;
using FindNeedleUX.Services.PagedLogSource;
using FindNeedlePluginLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Tests for the cheap distinct-Source counts that back the Sources dialog's by-type toggles
/// (replacing the old O(rows) facet scan). "Source" here is the viewer's Source column = each row's
/// GetResultSource (file/origin).
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
public class SourceCountsTests
{
    [TestMethod]
    public void InMemory_GetSourceCounts_TalliesByResultSource()
    {
        var rows = new List<LogLine>
        {
            new(new R(@"C:\logs\a.log"), 0),
            new(new R(@"C:\logs\a.log"), 1),
            new(new R(@"C:\logs\b.etl"), 2),
        };
        var counts = new InMemoryPagedSource(rows).GetSourceCounts();
        Assert.AreEqual(2, counts.Count);
        Assert.AreEqual(2, counts[@"C:\logs\a.log"]);
        Assert.AreEqual(1, counts[@"C:\logs\b.etl"]);
    }

    [TestMethod]
    public void ViewModel_GetSourceCounts_DelegatesToSource()
    {
        var vm = new NativeResultsPageViewModel();
        vm.SetSourceForTests(new InMemoryPagedSource(new List<LogLine> { new(new R(@"C:\x.log"), 0) }));
        Assert.AreEqual(1, vm.GetSourceCounts()[@"C:\x.log"]);
    }

    private sealed class R : ISearchResult
    {
        private readonly string _rs;
        public R(string resultSource) { _rs = resultSource; }
        public DateTime GetLogTime() => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public string GetMachineName() => "M";
        public void WriteToConsole() { }
        public Level GetLevel() => Level.Info;
        public string GetUsername() => "u";
        public string GetTaskName() => "t";
        public string GetOpCode() => "";
        public string GetSource() => "prov";       // viewer Provider — NOT what GetSourceCounts groups by
        public string GetSearchableData() => "d";
        public string GetMessage() => "m";
        public string GetResultSource() => _rs;     // viewer Source — this is what's grouped
    }
}
