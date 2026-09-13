using System;
using FindPluginCore.Searching.Query;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

/// <summary>
/// How the viewer's pivots compose in the search box: Follow / Filter in / Around ADD a clause, and a
/// clause on the same axis replaces the earlier one instead of ANDing with it.
/// </summary>
[TestClass]
public class QueryEditorTests
{
    private static QueryNode P(string text)
    {
        Assert.IsTrue(LogQuery.TryParse(text, out var n, out var err), $"parse: {err}");
        return n;
    }

    private static string[] Fields(params string[] f) => f;

    [TestMethod]
    public void Serialize_RoundTrips_ThroughTheParser()
    {
        foreach (var q in new[]
        {
            "level == \"Error\"",
            "(activityid == \"A\" OR relatedactivityid == \"A\") AND processid == \"4\"",
            "NOT message ~ \"debug\" AND \"timeout\"",
            "message =~ \"^err\\\\s+\\\\d+$\"",
            "message == \"say \\\"hi\\\"\"",
        })
        {
            var once = QueryEditor.Serialize(P(q));
            var twice = QueryEditor.Serialize(P(once));
            Assert.AreEqual(once, twice, $"serialize should be stable for: {q}");
            Assert.IsTrue(P(once).Evaluate(_ => "") == P(q).Evaluate(_ => ""), "same meaning");
        }
    }

    [TestMethod]
    public void FollowProcess_ThenProvider_KeepsBoth()
    {
        var text = QueryEditor.AddClause("", P("processid == \"4120\""), c => QueryEditor.PinsAnyOf(c, Fields("processid", "threadid")));
        text = QueryEditor.AddClause(text, P("provider == \"Kernel\""), c => QueryEditor.PinsAnyOf(c, Fields("provider")));
        Assert.AreEqual("processid == \"4120\" AND provider == \"Kernel\"", text);
    }

    [TestMethod]
    public void FollowProcess_ThenAnotherProcess_ReplacesTheProcess_KeepsTheRest()
    {
        var text = "provider == \"Kernel\" AND processid == \"4120\"";
        text = QueryEditor.AddClause(text, P("processid == \"9\""), c => QueryEditor.PinsAnyOf(c, Fields("processid", "threadid")));
        Assert.AreEqual("provider == \"Kernel\" AND processid == \"9\"", text);
    }

    [TestMethod]
    public void FollowThread_ThenProcess_DropsTheThreadPair()
    {
        var text = QueryEditor.AddClause("", P("processid == \"4\" AND threadid == \"7\""), c => QueryEditor.PinsAnyOf(c, Fields("processid", "threadid")));
        text = QueryEditor.AddClause(text, P("processid == \"5\""), c => QueryEditor.PinsAnyOf(c, Fields("processid", "threadid")));
        Assert.AreEqual("processid == \"5\"", text, "a different process cannot keep the old thread");
    }

    [TestMethod]
    public void FollowActivity_ReplacesAnEarlierActivity_EvenAsAnOrGroup()
    {
        var text = QueryEditor.AddClause("", P("activityid == \"A\" OR relatedactivityid == \"A\""), c => QueryEditor.PinsAnyOf(c, Fields("activityid", "relatedactivityid")));
        text = QueryEditor.AddClause(text, P("activityid == \"B\" OR relatedactivityid == \"B\""), c => QueryEditor.PinsAnyOf(c, Fields("activityid", "relatedactivityid")));
        Assert.AreEqual("(activityid == \"B\" OR relatedactivityid == \"B\")", text);
    }

    [TestMethod]
    public void FilterIn_Eq_ReplacesSameField_ButContainsAndNot_Accumulate()
    {
        var text = QueryEditor.AddClause("", P("provider == \"A\""), c => QueryEditor.PinsAnyOf(c, Fields("provider")));
        text = QueryEditor.AddClause(text, P("provider == \"B\""), c => QueryEditor.PinsAnyOf(c, Fields("provider")));
        Assert.AreEqual("provider == \"B\"", text, "filter in on the same field replaces");

        text = QueryEditor.AddClause(text, P("message ~ \"x\""), _ => false);
        text = QueryEditor.AddClause(text, P("message ~ \"y\""), _ => false);
        text = QueryEditor.AddClause(text, P("message != \"z\""), _ => false);
        Assert.AreEqual("provider == \"B\" AND message ~ \"x\" AND message ~ \"y\" AND message != \"z\"", text, "narrowing forms accumulate");

        text = QueryEditor.AddClause(text, P("message ~ \"x\""), _ => false);
        Assert.AreEqual("provider == \"B\" AND message ~ \"x\" AND message ~ \"y\" AND message != \"z\"", text, "an identical clause is not added twice");
    }

    [TestMethod]
    public void PlainTextSearch_IsKeptAsABareTerm()
    {
        var text = QueryEditor.AddClause("timeout", P("processid == \"4\""), c => QueryEditor.PinsAnyOf(c, Fields("processid")));
        Assert.AreEqual("\"timeout\" AND processid == \"4\"", text);
        var node = P(text);
        Assert.IsTrue(node.Evaluate(n => n == "*" ? "a timeout happened" : n == "processid" ? "4" : ""));
        Assert.IsFalse(node.Evaluate(n => n == "*" ? "all good" : n == "processid" ? "4" : ""));
    }

    [TestMethod]
    public void Around_ReplacesAnEarlierWindow_KeepsTheFollow()
    {
        var t = new DateTime(2026, 1, 1, 12, 0, 0);
        var text = QueryEditor.AddClause("processid == \"4\"", LogQuery.AroundTime(t, TimeSpan.FromSeconds(10)), QueryEditor.IsTimeRange);
        Assert.AreEqual("processid == \"4\" AND time ~ \"2026-01-01 12:00:00.000\" ±10s", text, "written back in the sugar form");
        var again = QueryEditor.AddClause(text, LogQuery.AroundTime(t.AddMinutes(5), TimeSpan.FromSeconds(1)), QueryEditor.IsTimeRange);
        Assert.AreEqual("processid == \"4\" AND time ~ \"2026-01-01 12:05:00.000\" ±1s", again, "only the new window remains");
        // And a hand-typed window round-trips through the editor unchanged.
        var typed = QueryEditor.AddClause("time ~ \"2026-01-01 12:00:00\" ±2s", P("provider == \"K\""), _ => false);
        Assert.AreEqual("time ~ \"2026-01-01 12:00:00.000\" ±2s AND provider == \"K\"", typed);
    }

    [TestMethod]
    public void IsTimeRange_RecognisesTheSugarForm_AndNothingElse()
    {
        Assert.IsTrue(QueryEditor.IsTimeRange(P("time ~ \"2026-01-01 12:00:00\" ±2s")));
        Assert.IsTrue(QueryEditor.IsTimeRange(P("time >= \"2026-01-01\"")));
        Assert.IsFalse(QueryEditor.IsTimeRange(P("processid == \"4\"")));
        Assert.IsFalse(QueryEditor.IsTimeRange(P("time >= \"2026-01-01\" AND processid == \"4\"")), "mixed clause is not a pure window");
    }
}
