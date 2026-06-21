using System;
using System.Collections.Generic;
using System.Linq;
using FindNeedlePluginLib;
using FindNeedleUX;
using FindNeedleUX.Services.Mcp;
using FindNeedleUX.Services.PagedLogSource;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Mcp;

/// <summary>
/// Tests for <see cref="LogAnalysis"/> — the MCP <c>facets</c> and <c>top_patterns</c> aggregations.
/// </summary>
[TestClass]
[TestCategory("Mcp")]
public class LogAnalysisTests
{
    private static InMemoryPagedSource Source(IEnumerable<ISearchResult> rows)
        => new(rows.Select((r, i) => new LogLine(r, i)).ToList());

    [TestMethod]
    public void Facets_GroupsByProvider_MostCommonFirst()
    {
        var rows = new List<R>();
        for (int i = 0; i < 6; i++) rows.Add(new R("m", source: "Alpha"));
        for (int i = 0; i < 3; i++) rows.Add(new R("m", source: "Beta"));
        rows.Add(new R("m", source: "Gamma"));
        using var src = Source(rows);

        var result = LogAnalysis.Facets(src, FilterSpec.Empty, "provider", 10, 500_000);

        Assert.AreEqual(10, result.Total);
        Assert.IsFalse(result.Truncated);
        Assert.AreEqual("Alpha", result.Values[0].Value);
        Assert.AreEqual(6, result.Values[0].Count);
        Assert.AreEqual("Beta", result.Values[1].Value);
        Assert.AreEqual(3, result.Values[1].Count);
    }

    [TestMethod]
    public void Facets_RespectsLimit()
    {
        // source maps to the row's Provider column (GetSource); give each row a distinct one.
        var rows = new List<R>();
        for (int i = 0; i < 10; i++) rows.Add(new R("m", source: "S" + i));
        using var src = Source(rows);
        var result = LogAnalysis.Facets(src, FilterSpec.Empty, "provider", 3, 500_000);
        Assert.AreEqual(3, result.Values.Count);
    }

    [TestMethod]
    public void Facets_UnknownField_Throws()
    {
        using var src = Source(new[] { new R("m") });
        Assert.ThrowsException<ArgumentException>(() => LogAnalysis.Facets(src, FilterSpec.Empty, "bogus", 10, 1000));
    }

    [TestMethod]
    public void Facets_EmptyValue_BecomesNone()
    {
        using var src = Source(new[] { new R("m", source: "") });
        var result = LogAnalysis.Facets(src, FilterSpec.Empty, "provider", 10, 1000);
        Assert.AreEqual("(none)", result.Values[0].Value);
    }

    [TestMethod]
    public void TopPatterns_CollapsesVariableParts()
    {
        // These differ only in numbers/GUIDs/paths — they should collapse to ONE template.
        var rows = new List<R>
        {
            new("Opened file C:\\logs\\a.txt in 12 ms"),
            new("Opened file C:\\logs\\b.txt in 348 ms"),
            new("Opened file D:\\x\\y.log in 7 ms"),
            new("Deleted session 5f1c2d34-1111-2222-3333-444455556666"),
            new("Deleted session 99887766-aaaa-bbbb-cccc-ddddeeeeffff"),
        };
        using var src = Source(rows);

        var result = LogAnalysis.TopPatterns(src, FilterSpec.Empty, 10, 200_000);

        Assert.AreEqual(5, result.Total);
        // Two distinct templates: the "Opened file" one (3) and the "Deleted session" one (2).
        Assert.AreEqual(2, result.Patterns.Count);
        Assert.AreEqual(3, result.Patterns[0].Count, "the file-open template is most common");
        Assert.IsTrue(result.Patterns[0].Template.Contains("{path}"));
        Assert.IsTrue(result.Patterns.Any(p => p.Template.Contains("{guid}")));
    }

    [TestMethod]
    public void Normalize_ReplacesPlaceholders()
    {
        Assert.AreEqual("user {str} did {n} things", LogAnalysis.Normalize("user \"bob\" did 42 things"));
        Assert.AreEqual("addr {hex}", LogAnalysis.Normalize("addr 0xDEADBEEF"));
    }

    private sealed class R : ISearchResult
    {
        private readonly string _m;
        private readonly string _source;
        public R(string m, string source = "prov") { _m = m; _source = source; }
        public DateTime GetLogTime() => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public string GetMachineName() => "M";
        public void WriteToConsole() { }
        public Level GetLevel() => Level.Info;
        public string GetUsername() => "u";
        public string GetTaskName() => "t";
        public string GetOpCode() => "";
        public string GetSource() => _source;
        public string GetSearchableData() => _m;
        public string GetMessage() => _m;
        public string GetResultSource() => "rs";
    }
}
