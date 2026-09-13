using System;
using System.Collections.Generic;
using System.IO;
using FindNeedlePluginLib;
using FindPluginCore.Implementations.Storage;
using FindPluginCore.Searching.Query;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

/// <summary>
/// The power-user additions to the query language: regex (=~), tag as a field, data.&lt;key&gt; payload
/// fields, rawlevel, and the time neighbourhood sugar (time ~ X ±w). Each is checked on the in-memory
/// predicate AND through SQLite, since the two compile from one AST and must agree.
/// </summary>
[TestClass]
public class LogQueryPowerTests
{
    private static Func<string, string> Row(Dictionary<string, string> f)
    {
        var any = string.Join(" ", f.Values);
        return name => name == "*" ? any : (f.TryGetValue(name, out var v) ? v : "");
    }

    private static QueryNode Parse(string q)
    {
        Assert.IsTrue(LogQuery.TryParse(q, out var node, out var err), $"parse failed: {err}");
        return node;
    }

    [TestCleanup]
    public void Cleanup() { LogQuery.TagSnapshot = null; LogQuery.DefaultDate = null; }

    // ---------- regex ----------

    [TestMethod]
    public void Regex_MatchesCaseInsensitively_InMemory()
    {
        var node = Parse("msg =~ \"^err(or)?\\s+\\d+$\"");
        Assert.IsTrue(node.Evaluate(Row(new() { ["message"] = "Error 42" })));
        Assert.IsTrue(node.Evaluate(Row(new() { ["message"] = "ERR 7" })));
        Assert.IsFalse(node.Evaluate(Row(new() { ["message"] = "error forty" })));
    }

    [TestMethod]
    public void Regex_BadPattern_IsAParseError_NotAnEmptyResult()
    {
        Assert.IsFalse(LogQuery.TryParse("msg =~ \"(unclosed\"", out _, out var err));
        StringAssert.Contains(err, "regex");
    }

    // ---------- tag ----------

    [TestMethod]
    public void Tag_EqMatchesName_ContainsSearchesNote_UntaggedRowsCompareAsEmpty()
    {
        LogQuery.TagSnapshot = () => new Dictionary<long, (string, string)>
        {
            [7] = ("Important", "handle leak here"),
            [9] = ("Note", ""),
        };
        Func<string, string> R(long id) => Row(new() { ["rowid"] = id.ToString(), ["message"] = "x" });

        Assert.IsTrue(Parse("tag == Important").Evaluate(R(7)));
        Assert.IsFalse(Parse("tag == Important").Evaluate(R(9)));
        Assert.IsFalse(Parse("tag == Important").Evaluate(R(1)), "untagged row");
        Assert.IsTrue(Parse("tag ~ leak").Evaluate(R(7)), "~ searches the note");
        Assert.IsTrue(Parse("tag != Important").Evaluate(R(1)), "untagged rows are 'not Important'");
        Assert.IsTrue(Parse("NOT tag == Important").Evaluate(R(9)));
    }

    // ---------- data.<key> ----------

    [TestMethod]
    public void DataField_ReadsThePayloadKey_InMemory()
    {
        var node = Parse("data.ProcessId == 4 AND data.Status ~ ok");
        var row = Row(new() { ["data.ProcessId"] = "4", ["data.Status"] = "OK", ["message"] = "m" });
        Assert.IsTrue(node.Evaluate(row));
        Assert.IsFalse(node.Evaluate(Row(new() { ["data.ProcessId"] = "5", ["data.Status"] = "OK" })));
    }

    [TestMethod]
    public void DataField_RejectsUnsafeKeys()
    {
        Assert.IsFalse(LogQuery.TryParse("data.x' == 1", out _, out _), "a quote can't reach the SQL path");
        Assert.IsTrue(LogQuery.TryParse("data.Event_Id-2 == 1", out _, out _));
    }

    // ---------- time neighbourhood ----------

    [TestMethod]
    public void TimeAround_WithWindow_IsAnInclusiveRange()
    {
        var node = Parse("time ~ \"2026-01-15 12:34:56\" ±2s");
        Func<string, string> At(string t) => Row(new() { ["time"] = t });
        Assert.IsTrue(node.Evaluate(At("2026-01-15T12:34:54.000")), "lower edge");
        Assert.IsTrue(node.Evaluate(At("2026-01-15T12:34:58.000")), "upper edge");
        Assert.IsTrue(node.Evaluate(At("2026-01-15T12:34:56.500")));
        Assert.IsFalse(node.Evaluate(At("2026-01-15T12:34:53.999")));
        Assert.IsFalse(node.Evaluate(At("2026-01-15T12:34:58.001")));
    }

    [TestMethod]
    public void TimeAround_WithoutWindow_IsThatWholeSecond()
    {
        var node = Parse("time ~ \"2026-01-15 12:34:56\"");
        Func<string, string> At(string t) => Row(new() { ["time"] = t });
        Assert.IsTrue(node.Evaluate(At("2026-01-15T12:34:56.000")));
        Assert.IsTrue(node.Evaluate(At("2026-01-15T12:34:56.999")));
        Assert.IsFalse(node.Evaluate(At("2026-01-15T12:34:57.000")));
        Assert.IsFalse(node.Evaluate(At("2026-01-15T12:34:55.999")));
    }

    [TestMethod]
    public void TimeAround_DateLessTime_LandsOnTheDataDay()
    {
        LogQuery.DefaultDate = new DateTime(2026, 3, 9);
        var node = Parse("time ~ 12:34:56 ±500ms");
        Assert.IsTrue(node.Evaluate(Row(new() { ["time"] = "2026-03-09T12:34:56.200" })));
        Assert.IsFalse(node.Evaluate(Row(new() { ["time"] = "2026-03-10T12:34:56.200" })), "a different day");
    }

    [TestMethod]
    public void TimeAround_NotForm_Excludes()
    {
        var node = Parse("time !~ \"2026-01-15 12:34:56\" ±1s");
        Assert.IsFalse(node.Evaluate(Row(new() { ["time"] = "2026-01-15T12:34:56.500" })));
        Assert.IsTrue(node.Evaluate(Row(new() { ["time"] = "2026-01-15T12:35:56.500" })));
    }

    [TestMethod]
    public void TryParseWindow_AcceptsTheCommonSpellings()
    {
        Assert.IsTrue(LogQuery.TryParseWindow("±2s", out var w) && w == TimeSpan.FromSeconds(2));
        Assert.IsTrue(LogQuery.TryParseWindow("500ms", out w) && w == TimeSpan.FromMilliseconds(500));
        Assert.IsTrue(LogQuery.TryParseWindow("+-1m", out w) && w == TimeSpan.FromMinutes(1));
        Assert.IsTrue(LogQuery.TryParseWindow("2h", out w) && w == TimeSpan.FromHours(2));
        Assert.IsFalse(LogQuery.TryParseWindow("soon", out _));
    }

    [TestMethod]
    public void AroundTimeText_RoundTripsThroughTheParser()
    {
        var center = new DateTime(2026, 1, 15, 12, 34, 56, 789);
        var text = LogQuery.AroundTimeText(center, TimeSpan.FromSeconds(10));
        StringAssert.Contains(text, "±10s");
        var node = Parse(text);
        Assert.IsTrue(node.Evaluate(Row(new() { ["time"] = "2026-01-15T12:35:06.789" })), "10 s after, inclusive");
        Assert.IsFalse(node.Evaluate(Row(new() { ["time"] = "2026-01-15T12:35:06.790" })));
    }

    // ---------- the same through SQLite ----------

    private sealed class R : ISearchResult
    {
        private readonly string _m; private readonly DateTime _t; private readonly string _sd; private readonly string _raw;
        public R(string m, DateTime t, string structured = "", string raw = "") { _m = m; _t = t; _sd = structured; _raw = raw; }
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
        public string GetStructuredData() => _sd;
        public string GetRawLevel() => _raw;
    }

    private static QueryNode Q(string s)
    {
        Assert.IsTrue(LogQuery.TryParse(s, out var n, out var e), $"parse: {e}");
        return n;
    }

    [TestMethod]
    [TestCategory("Storage")]
    public void Sqlite_Regex_Data_RawLevel_Tag_TimeAround_AllFilterViaSql()
    {
        var f = Path.Combine(Path.GetTempPath(), "lqpower_" + Guid.NewGuid().ToString("N"));
        var dbPath = FindNeedleCoreUtils.CachedStorage.GetCacheFilePath(f, ".db");
        try
        {
            using var storage = new SqliteStorage(f);
            storage.ClearTables();
            var t0 = new DateTime(2026, 1, 15, 12, 34, 56, 0);
            storage.AddFilteredBatch(new List<ISearchResult>
            {
                new R("Error 42",     t0,                    "{\"ProcessId\":\"4\",\"Status\":\"OK\"}", "2"),
                new R("error forty",  t0.AddSeconds(1),      "{\"ProcessId\":\"5\"}",                 "3"),
                new R("all good",     t0.AddSeconds(30),     "",                                       ""),
                new R("ERR 7",        t0.AddMinutes(5),      "{\"ProcessId\":\"4\"}",                 "2"),
            });

            int Count(string q) => storage.GetFilteredCount(new SqliteStorage.FilterInput { Query = Q(q) });

            Assert.AreEqual(2, Count("msg =~ \"^err(or)?\\s+\\d+$\""), "regex through REGEXP");
            Assert.AreEqual(2, Count("data.ProcessId == 4"), "json_extract on the payload");
            Assert.AreEqual(1, Count("data.ProcessId == 4 AND data.Status ~ ok"));
            Assert.AreEqual(2, Count("rawlevel == 2"));
            Assert.AreEqual(2, Count("time ~ \"2026-01-15 12:34:56\" ±2s"), "t0 and t0+1s");
            Assert.AreEqual(1, Count("time ~ \"2026-01-15 12:34:56\""), "just that second");
            Assert.AreEqual(3, Count("time ~ \"2026-01-15 12:34:56\" ±1m"));

            // Tags: rows are addressed by their stable Id; take them from the store.
            var rows = new List<ISearchResult>();
            storage.GetFilteredResultsInBatches(b => rows.AddRange(b));
            long idOfErr7 = rows.Find(r => r.GetMessage() == "ERR 7").GetRowId();
            LogQuery.TagSnapshot = () => new Dictionary<long, (string, string)> { [idOfErr7] = ("Important", "the one") };
            Assert.AreEqual(1, Count("tag == Important"));
            Assert.AreEqual(3, Count("tag != Important"), "untagged rows count as not Important");
            Assert.AreEqual(1, Count("tag ~ \"the one\""));
            Assert.AreEqual(1, Count("tag == Important AND msg ~ err"));
        }
        finally { try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { } }
    }
}
