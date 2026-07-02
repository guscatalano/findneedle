using System;
using System.Collections.Generic;
using System.Linq;
using FindNeedlePluginLib;
using FindNeedleUX;
using FindNeedleUX.Services.PagedLogSource;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Exercises the result viewer's filter semantics end-to-end through <see cref="InMemoryPagedSource"/>
/// (the default backend): substring fields, exact Level, time range, the multi-select OR-sets (and their
/// precedence over the substring field), the structured query, and combinations (AND across fields).
/// </summary>
[TestClass]
public class FilterSpecTests
{
    // 6 rows. Reminder of the LogLine mapping: GetSource()->Provider, GetResultSource()->Source.
    //  #  Provider  Task  Source  Level      Message                  Time
    //  0  Alpha     T1    src-a   Error      "database needle failed" 09:00
    //  1  Alpha     T2    src-b   Warning    "cache miss"             09:05
    //  2  Beta      T1    src-a   Error      "disk full"              09:10
    //  3  Beta      T3    src-c   Info       "all ok"                 10:00
    //  4  Gamma     T1    src-a   Verbose    "trace needle here"      11:00
    //  5  Gamma     T2    src-b   Info       "hello world"            12:00
    private static InMemoryPagedSource Sample()
    {
        var baseT = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rows = new[]
        {
            new Row("Alpha", "T1", "src-a", Level.Error,   "database needle failed", baseT.AddHours(9)),
            new Row("Alpha", "T2", "src-b", Level.Warning, "cache miss",             baseT.AddHours(9).AddMinutes(5)),
            new Row("Beta",  "T1", "src-a", Level.Error,   "disk full",              baseT.AddHours(9).AddMinutes(10)),
            new Row("Beta",  "T3", "src-c", Level.Info,    "all ok",                 baseT.AddHours(10)),
            new Row("Gamma", "T1", "src-a", Level.Verbose, "trace needle here",      baseT.AddHours(11)),
            new Row("Gamma", "T2", "src-b", Level.Info,    "hello world",            baseT.AddHours(12)),
        };
        return new InMemoryPagedSource(rows.Select((r, i) => new LogLine(r, i)).ToList());
    }

    private static FilterSpec F => FilterSpec.Empty;
    private static int Count(InMemoryPagedSource s, FilterSpec f) => s.GetFilteredCount(f);

    [TestMethod]
    public void EmptyFilter_ReturnsAll()
        => Assert.AreEqual(6, Count(Sample(), FilterSpec.Empty));

    // ---- substring fields (case-insensitive) ----
    [TestMethod]
    public void Provider_Substring_CaseInsensitive()
    {
        Assert.AreEqual(2, Count(Sample(), F with { Provider = "Alpha" }));
        Assert.AreEqual(2, Count(Sample(), F with { Provider = "alph" }), "partial + case-insensitive");
        Assert.AreEqual(0, Count(Sample(), F with { Provider = "Delta" }));
    }

    [TestMethod]
    public void TaskName_Substring()
        => Assert.AreEqual(3, Count(Sample(), F with { TaskName = "T1" }));

    [TestMethod]
    public void Message_Substring_CaseInsensitive()
    {
        Assert.AreEqual(2, Count(Sample(), F with { Message = "needle" }));
        Assert.AreEqual(2, Count(Sample(), F with { Message = "NEEDLE" }));
    }

    [TestMethod]
    public void Source_Substring()
        => Assert.AreEqual(3, Count(Sample(), F with { Source = "src-a" }));

    // ---- level (exact, case-insensitive) ----
    [TestMethod]
    public void Level_ExactMatch()
    {
        Assert.AreEqual(2, Count(Sample(), F with { Level = "Error" }));
        Assert.AreEqual(2, Count(Sample(), F with { Level = "error" }), "case-insensitive");
        Assert.AreEqual(2, Count(Sample(), F with { Level = "Info" }));
        Assert.AreEqual(0, Count(Sample(), F with { Level = "Err" }), "level is exact, not substring");
    }

    // ---- multi-select level OR-set (Error + Warning together) ----
    [TestMethod]
    public void LevelSet_IsOrSet_CaseInsensitive_AndTakesPrecedence()
    {
        // 2 Error + 1 Warning = 3.
        Assert.AreEqual(3, Count(Sample(), F with { LevelSet = new[] { "Error", "Warning" } }));
        Assert.AreEqual(2, Count(Sample(), F with { LevelSet = new[] { "error" } }), "case-insensitive");
        Assert.AreEqual(4, Count(Sample(), F with { LevelSet = new[] { "Error", "Info" } }), "2 Error + 2 Info");
        // The set takes precedence over the single Level field.
        Assert.AreEqual(2, Count(Sample(), F with { Level = "Info", LevelSet = new[] { "Error" } }),
            "LevelSet wins over Level → the 2 Error rows, not Info");
    }

    // ---- time range (inclusive both ends) ----
    [TestMethod]
    public void TimeRange_InclusiveBounds()
    {
        var baseT = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.AreEqual(5, Count(Sample(), F with { FromTime = baseT.AddHours(9).AddMinutes(5) }), ">= 09:05");
        Assert.AreEqual(4, Count(Sample(), F with { ToTime = baseT.AddHours(10) }), "<= 10:00");
        Assert.AreEqual(4, Count(Sample(),
            F with { FromTime = baseT.AddHours(9).AddMinutes(5), ToTime = baseT.AddHours(11) }), "09:05..11:00");
    }

    // ---- multi-select OR-sets (exact, case-insensitive) ----
    [TestMethod]
    public void ProviderSet_IsOrSet_CaseInsensitive()
    {
        Assert.AreEqual(4, Count(Sample(), F with { ProviderSet = new[] { "Alpha", "Gamma" } }));
        Assert.AreEqual(2, Count(Sample(), F with { ProviderSet = new[] { "alpha" } }), "case-insensitive exact");
        Assert.AreEqual(0, Count(Sample(), F with { ProviderSet = new[] { "Alp" } }), "set is exact, not substring");
    }

    [TestMethod]
    public void TaskNameSet_And_SourceSet()
    {
        Assert.AreEqual(4, Count(Sample(), F with { TaskNameSet = new[] { "T1", "T3" } }));
        Assert.AreEqual(3, Count(Sample(), F with { SourceSet = new[] { "src-a" } }));
    }

    [TestMethod]
    public void ProviderSet_TakesPrecedenceOverSubstring()
    {
        // The set wins; the conflicting substring (Provider="Beta") is ignored.
        var f = F with { Provider = "Beta", ProviderSet = new[] { "Alpha" } };
        Assert.AreEqual(2, Count(Sample(), f));
    }

    // ---- combinations: AND across fields ----
    [TestMethod]
    public void Combination_AndsAcrossFields()
    {
        Assert.AreEqual(1, Count(Sample(), F with { Provider = "Alpha", Level = "Error" }), "row 0 only");
        var baseT = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.AreEqual(1, Count(Sample(), F with { Level = "Error", FromTime = baseT.AddHours(9).AddMinutes(5) }),
            "Error after 09:05 = row 2");
    }

    // ---- structured query (search-box language) ----
    [TestMethod]
    public void StructuredQuery_OrAndNot()
    {
        Assert.AreEqual(3, Count(Sample(), F with { Query = Parse("level == \"Error\" OR level == \"Warning\"") }));
        Assert.AreEqual(1, Count(Sample(), F with { Query = Parse("provider == \"Alpha\" AND message ~ \"needle\"") }));
        Assert.AreEqual(4, Count(Sample(), F with { Query = Parse("NOT level == \"Info\"") }), "6 - 2 Info");
        Assert.AreEqual(4, Count(Sample(), F with { Query = Parse("provider != \"Beta\"") }), "6 - 2 Beta");
    }

    private static FindPluginCore.Searching.Query.QueryNode Parse(string q)
    {
        Assert.IsTrue(FindPluginCore.Searching.Query.LogQuery.TryParse(q, out var node, out var error),
            $"should parse '{q}': {error}");
        return node;
    }

    private sealed class Row : ISearchResult
    {
        private readonly string _provider, _task, _source, _msg;
        private readonly Level _level;
        private readonly DateTime _time;
        public Row(string provider, string task, string source, Level level, string msg, DateTime time)
        { _provider = provider; _task = task; _source = source; _level = level; _msg = msg; _time = time; }
        public DateTime GetLogTime() => _time;
        public string GetMachineName() => "M";
        public void WriteToConsole() { }
        public Level GetLevel() => _level;
        public string GetUsername() => "u";
        public string GetTaskName() => _task;
        public string GetOpCode() => "";
        public string GetSource() => _provider;       // -> LogLine.Provider
        public string GetSearchableData() => _msg;
        public string GetMessage() => _msg;
        public string GetResultSource() => _source;    // -> LogLine.Source
    }
}
