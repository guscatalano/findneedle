using System;
using System.Text.RegularExpressions;
using FindNeedleUX;
using FindNeedleUX.Services;
using FindNeedlePluginLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Services;

/// <summary>
/// Tests for <see cref="ViewerQuickRule"/> — the right-click "pull a value into a column / strip it
/// from the message" rule applied at display time. Covers the pure <c>Evaluate</c> (match / capture /
/// strip) and <c>Apply</c> (field routing onto a <see cref="LogLine"/>).
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
public class ViewerQuickRuleTests
{
    private static Regex Rx(string p) => new(p, RegexOptions.IgnoreCase);

    [TestInitialize]
    public void Init() => ViewerQuickRulesStore.Clear(); // LogLine ctor applies the static store

    [TestMethod]
    public void Evaluate_NoMatch_ReturnsOriginalMessage()
    {
        var r = new ViewerQuickRule { Pattern = Rx(@"PID=(?<v>\d+)"), TargetField = "ProcessId" };
        var (matched, captured, after) = r.Evaluate("nothing here");
        Assert.IsFalse(matched);
        Assert.IsNull(captured);
        Assert.AreEqual("nothing here", after);
    }

    [TestMethod]
    public void Evaluate_CapturesNamedGroup()
    {
        var r = new ViewerQuickRule { Pattern = Rx(@"PID=(?<v>\d+)"), TargetField = "ProcessId" };
        var (matched, captured, _) = r.Evaluate("svc PID=1234 started");
        Assert.IsTrue(matched);
        Assert.AreEqual("1234", captured);
    }

    [TestMethod]
    public void Evaluate_Strip_RemovesMatch_AndCollapsesWhitespace()
    {
        var r = new ViewerQuickRule { Pattern = Rx(@"PID=\d+"), Strip = true }; // no TargetField → capture null
        var (matched, captured, after) = r.Evaluate("svc  PID=1234  started");
        Assert.IsTrue(matched);
        Assert.IsNull(captured);
        Assert.AreEqual("svc started", after);
    }

    [TestMethod]
    public void Evaluate_EmptyMessage_NoMatch()
    {
        var r = new ViewerQuickRule { Pattern = Rx("x"), TargetField = "ProcessId" };
        var (matched, _, after) = r.Evaluate("");
        Assert.IsFalse(matched);
        Assert.AreEqual("", after);
    }

    [TestMethod]
    public void Apply_SetsTargetField_FromCapture()
    {
        var line = new LogLine(new R("worker PID=4242 ok"), 0);
        new ViewerQuickRule { Pattern = Rx(@"PID=(?<v>\d+)"), TargetField = "ProcessId" }.Apply(line);
        Assert.AreEqual("4242", line.ProcessId);
    }

    [TestMethod]
    public void Apply_SourceTarget_FeedsProviderColumn()
    {
        var line = new LogLine(new R("svc=Auth hello"), 0);
        new ViewerQuickRule { Pattern = Rx(@"svc=(?<v>\w+)"), TargetField = "Source" }.Apply(line);
        Assert.AreEqual("Auth", line.Provider); // the "Source" target feeds the Provider column
    }

    [TestMethod]
    public void Apply_Strip_RewritesMessage()
    {
        var line = new LogLine(new R("noise PID=7 done"), 0);
        new ViewerQuickRule { Pattern = Rx(@"PID=\d+"), Strip = true }.Apply(line);
        Assert.AreEqual("noise done", line.Message);
    }

    // ----- ViewerQuickRulesStore (the session-only list that applies rules to each row) -----

    [TestMethod]
    public void Store_AddAndClear_TrackRules()
    {
        Assert.IsFalse(ViewerQuickRulesStore.Any, "Init clears the store");
        ViewerQuickRulesStore.Add(new ViewerQuickRule { Pattern = Rx("x"), TargetField = "ProcessId" });
        Assert.IsTrue(ViewerQuickRulesStore.Any);
        Assert.AreEqual(1, ViewerQuickRulesStore.Rules.Count);
        ViewerQuickRulesStore.Clear();
        Assert.IsFalse(ViewerQuickRulesStore.Any);
    }

    [TestMethod]
    public void Store_Add_IgnoresNull()
    {
        ViewerQuickRulesStore.Add(null);
        Assert.IsFalse(ViewerQuickRulesStore.Any);
    }

    [TestMethod]
    public void Store_Apply_RunsEveryRuleOnTheRow()
    {
        // Build the row while the store is empty (Init cleared it), then register rules and apply.
        var line = new LogLine(new R("worker PID=11 TID=22 ok"), 0);
        ViewerQuickRulesStore.Add(new ViewerQuickRule { Pattern = Rx(@"PID=(?<v>\d+)"), TargetField = "ProcessId" });
        ViewerQuickRulesStore.Add(new ViewerQuickRule { Pattern = Rx(@"TID=(?<v>\d+)"), TargetField = "ThreadId" });
        ViewerQuickRulesStore.Apply(line);
        Assert.AreEqual("11", line.ProcessId);
        Assert.AreEqual("22", line.ThreadId);
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
        public string GetTaskName() => "";
        public string GetOpCode() => "";
        public string GetSource() => "";
        public string GetSearchableData() => _m;
        public string GetMessage() => _m;
        public string GetResultSource() => "rs";
    }
}
