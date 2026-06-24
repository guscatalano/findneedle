using System;
using System.Collections.Generic;
using System.Linq;
using FindNeedlePluginLib;
using FindNeedleUX;
using FindNeedleUX.Pages.NativeResultViewer;
using FindNeedleUX.Services.Mcp;
using FindNeedleUX.Services.PagedLogSource;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Tests for the "known value" filter dropdowns (Provider / TaskName / Source) that match
/// SimpleEventViewer: SEV-style summary labels, value+count display rows, and cross-filter narrowing
/// (a field's values reflect the OTHER active filters).
/// </summary>
[TestClass]
public class KnownFilterTests
{
    // ---- SEV-style summary on the multi-select button ----
    [TestMethod]
    public void Summarize_None_ShowsAll()
        => Assert.AreEqual("Provider: All", KnownFilterLabel.Summarize("Provider", new List<string>()));

    [TestMethod]
    public void Summarize_One_ShowsValue()
        => Assert.AreEqual("Provider: Alpha", KnownFilterLabel.Summarize("Provider", new List<string> { "Alpha" }));

    [TestMethod]
    public void Summarize_Many_ShowsFirstPlusCount()
        => Assert.AreEqual("Source: Alpha +2",
            KnownFilterLabel.Summarize("Source", new List<string> { "Alpha", "Beta", "Gamma" }));

    // ---- dropdown rows carry the count, like SEV's "Name (Count)" ----
    [TestMethod]
    public void FacetItem_Display_ShowsValueAndCount()
    {
        Assert.AreEqual("Alpha  (1,234)", new KnownFacetItem { Value = "Alpha", Count = 1234 }.Display);
        Assert.AreEqual("(All)", new KnownFacetItem { IsAll = true, Count = 9 }.Display);
    }

    // ---- cross-filter narrowing: provider values reflect an active TaskName filter ----
    // (mirrors NativeResultsPageViewModel.GetFacetsExcludingField, which clears the queried field
    //  from the spec but keeps every other active filter before computing facets.)
    [TestMethod]
    public void Facets_AreNarrowedByOtherActiveFilter()
    {
        var rows = new List<Row>
        {
            new("m", provider: "Alpha", task: "T1"),
            new("m", provider: "Alpha", task: "T1"),
            new("m", provider: "Beta",  task: "T2"),
        };
        using var src = new InMemoryPagedSource(rows.Select((r, i) => new LogLine(r, i)).ToList());

        var spec = FilterSpec.Empty with { TaskName = "T1" };
        var result = LogAnalysis.Facets(src, spec, "provider", 10, 500_000);

        Assert.AreEqual(1, result.Values.Count, "only providers present in T1 rows survive the cross-filter");
        Assert.AreEqual("Alpha", result.Values[0].Value);
        Assert.AreEqual(2, result.Values[0].Count);
    }

    private sealed class Row : ISearchResult
    {
        private readonly string _m, _provider, _task;
        public Row(string m, string provider = "prov", string task = "t") { _m = m; _provider = provider; _task = task; }
        public DateTime GetLogTime() => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public string GetMachineName() => "M";
        public void WriteToConsole() { }
        public Level GetLevel() => Level.Info;
        public string GetUsername() => "u";
        public string GetTaskName() => _task;
        public string GetOpCode() => "";
        public string GetSource() => _provider;   // maps to the Provider column
        public string GetSearchableData() => _m;
        public string GetMessage() => _m;
        public string GetResultSource() => "rs";
    }
}
