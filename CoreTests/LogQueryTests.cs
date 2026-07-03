using System;
using System.Collections.Generic;
using FindPluginCore.Searching.Query;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

[TestClass]
[TestCategory("Query")]
public class LogQueryTests
{
    // A fake row: field name -> value, plus "*" = all searchable text.
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

    [TestMethod]
    public void Eq_And_Ne_Combine_WithAnd()
    {
        var node = Parse("msg != \"this\" AND taskname == \"that\"");
        var match = Row(new() { ["message"] = "other", ["taskname"] = "that" });
        var noMatch = Row(new() { ["message"] = "this", ["taskname"] = "that" });
        Assert.IsTrue(node.Evaluate(match));
        Assert.IsFalse(node.Evaluate(noMatch), "msg == 'this' should fail the != predicate");
    }

    [TestMethod]
    public void ActivityId_Field_And_Aliases_Match()
    {
        const string guid = "12345678-1234-1234-1234-1234567890ab";
        foreach (var q in new[] { $"activityid == \"{guid}\"", $"aid == \"{guid}\"", $"activity == \"{guid}\"" })
        {
            var node = Parse(q);
            Assert.IsTrue(node.Evaluate(Row(new() { ["activityid"] = guid })), $"should match: {q}");
            Assert.IsFalse(node.Evaluate(Row(new() { ["activityid"] = "00000000-0000-0000-0000-000000000000" })),
                $"should not match the zero GUID: {q}");
        }
        Assert.IsTrue(LogQuery.IsField("activityid"));
        Assert.IsTrue(LogQuery.IsField("aid"));
    }

    [TestMethod]
    public void Contains_And_NotContains()
    {
        var node = Parse("provider ~ Kernel AND msg !~ debug");
        Assert.IsTrue(node.Evaluate(Row(new() { ["provider"] = "Microsoft-Windows-Kernel-Power", ["message"] = "boot ok" })));
        Assert.IsFalse(node.Evaluate(Row(new() { ["provider"] = "Kernel", ["message"] = "debug spew" })));
        Assert.IsFalse(node.Evaluate(Row(new() { ["provider"] = "Audio", ["message"] = "x" })));
    }

    [TestMethod]
    public void Or_And_Parens_And_Not()
    {
        var node = Parse("(level == Error OR level == Warning) AND NOT msg ~ ignore");
        Assert.IsTrue(node.Evaluate(Row(new() { ["level"] = "Error", ["message"] = "real problem" })));
        Assert.IsTrue(node.Evaluate(Row(new() { ["level"] = "Warning", ["message"] = "watch out" })));
        Assert.IsFalse(node.Evaluate(Row(new() { ["level"] = "Error", ["message"] = "please ignore" })));
        Assert.IsFalse(node.Evaluate(Row(new() { ["level"] = "Info", ["message"] = "fine" })));
    }

    [TestMethod]
    public void BareTerm_Is_AnyContains()
    {
        var node = Parse("level == Error AND timeout");
        Assert.IsTrue(node.Evaluate(Row(new() { ["level"] = "Error", ["message"] = "connection timeout" })));
        Assert.IsFalse(node.Evaluate(Row(new() { ["level"] = "Error", ["message"] = "all good" })));
    }

    [TestMethod]
    public void CaseInsensitive_Fields_Values_Aliases()
    {
        var node = Parse("MSG == \"Hello\"");   // alias + case-insensitive value
        Assert.IsTrue(node.Evaluate(Row(new() { ["message"] = "hello" })));
    }

    [TestMethod]
    public void Time_Comparison()
    {
        var node = Parse("time > \"2024-01-15 09:00\"");
        Assert.IsTrue(node.Evaluate(Row(new() { ["time"] = "2024-01-15T09:30:00.0000000" })));
        Assert.IsFalse(node.Evaluate(Row(new() { ["time"] = "2024-01-15T08:30:00.0000000" })));
    }

    [TestMethod]
    public void LooksStructured_Detects_Query_Vs_Plain()
    {
        Assert.IsTrue(LogQuery.LooksStructured("msg == x"));
        Assert.IsTrue(LogQuery.LooksStructured("a AND b"));
        Assert.IsFalse(LogQuery.LooksStructured("access denied"), "plain multi-word search is not a query");
        Assert.IsFalse(LogQuery.LooksStructured("timeout"));
    }

    [TestMethod]
    public void UnknownField_And_BadSyntax_ReportErrors()
    {
        Assert.IsFalse(LogQuery.TryParse("bogus == x", out _, out var e1));
        StringAssert.Contains(e1, "bogus");
        Assert.IsFalse(LogQuery.TryParse("msg == ", out _, out _));
        Assert.IsFalse(LogQuery.TryParse("(msg == x", out _, out _), "missing paren");
    }

    [TestMethod]
    public void ToSql_Produces_Parameterized_Where()
    {
        var node = Parse("msg != \"this\" AND taskname == \"that\"");
        var ctx = new QuerySqlContext();
        var sql = node.AppendSql(ctx);
        StringAssert.Contains(sql, "Message");
        StringAssert.Contains(sql, "TaskName");
        StringAssert.Contains(sql, "AND");
        Assert.AreEqual(2, ctx.Parameters.Count, "two bound parameters");
        // Level maps to its int enum value in SQL.
        var lvl = new QuerySqlContext();
        var lsql = Parse("level == Error").AppendSql(lvl);
        StringAssert.Contains(lsql, "Level =");
        Assert.AreEqual(1, lvl.Parameters[0].Value, "Error → int 1");
    }
}
