using System;
using System.Text.RegularExpressions;
using FindNeedlePluginLib;
using FindNeedleUX;
using FindNeedleUX.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Session quick rules used to be all-or-nothing and unnamed: the only way to stop one was to delete
/// every rule, and nothing anywhere said what a rule was doing. These pin the two things the filter
/// pane's "Quick rules" section needs — a readable label, and an on/off that keeps the rule.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
public class ViewerQuickRuleTests
{
    [TestCleanup]
    public void Cleanup() => ViewerQuickRulesStore.Clear(); // the store is session-global

    private static LogLine Row(string message) => new(new Stub(message), 0);

    private static ViewerQuickRule Strip(string pattern) => new()
    {
        Pattern = new Regex(pattern), Strip = true, ColumnLabel = "Message",
    };

    private static ViewerQuickRule Extract(string pattern, string field, string column) => new()
    {
        Pattern = new Regex(pattern), CaptureGroup = "v", TargetField = field, Strip = true, ColumnLabel = column,
    };

    [TestMethod]
    public void NewRule_IsEnabled()
    {
        Assert.IsTrue(Strip("x").Enabled);
    }

    [TestMethod]
    public void Label_SaysWhatTheRuleDoes()
    {
        Assert.AreEqual("strip msg ~ heartbeat", Strip("heartbeat").Label);
        Assert.AreEqual(@"TaskName ← PID=(?<v>\d+)", Extract(@"PID=(?<v>\d+)", "TaskName", "TaskName").Label);
    }

    [TestMethod]
    public void DisabledRule_IsNotApplied_ButIsStillInTheList()
    {
        var rule = Strip("heartbeat ");
        rule.Enabled = false;
        ViewerQuickRulesStore.Add(rule);

        var line = Row("heartbeat tick 5");
        ViewerQuickRulesStore.Apply(line);

        Assert.AreEqual("heartbeat tick 5", line.Message, "a disabled rule must not reshape the row");
        Assert.AreEqual(1, ViewerQuickRulesStore.Rules.Count, "…but it stays in the list so it can be switched back on");
    }

    [TestMethod]
    public void EnabledRule_IsApplied()
    {
        ViewerQuickRulesStore.Add(Strip("heartbeat "));
        var line = Row("heartbeat tick 5");
        ViewerQuickRulesStore.Apply(line);
        Assert.AreEqual("tick 5", line.Message);
    }

    [TestMethod]
    public void Remove_DropsOneRule_NotAllOfThem()
    {
        var keep = Strip("alpha ");
        var drop = Strip("beta ");
        ViewerQuickRulesStore.Add(keep);
        ViewerQuickRulesStore.Add(drop);

        ViewerQuickRulesStore.Remove(drop);

        Assert.AreEqual(1, ViewerQuickRulesStore.Rules.Count);
        Assert.AreSame(keep, ViewerQuickRulesStore.Rules[0]);
    }

    private sealed class Stub : ISearchResult
    {
        private readonly string _m;
        public Stub(string m) { _m = m; }
        public DateTime GetLogTime() => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public string GetMachineName() => "M";
        public void WriteToConsole() { }
        public Level GetLevel() => Level.Info;
        public string GetUsername() => "u";
        public string GetTaskName() => "t";
        public string GetOpCode() => "";
        public string GetSource() => "prov";
        public string GetSearchableData() => _m;
        public string GetMessage() => _m;
        public string GetResultSource() => "rs";
    }
}
