using System;
using FindNeedleRuleDSL;
using FindNeedlePluginLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleRuleDSLTests;

/// <summary>
/// Tests for the per-field logic behind RuleDSL output rules: <see cref="OutputRuleProcessor.GetFieldValue"/>
/// (field-name → row value, including aliases + default) and <see cref="OutputRuleProcessor.EscapeCsvField"/>
/// (RFC-4180 escaping). The file-writing orchestration is exercised by the integration tests.
/// </summary>
[TestClass]
public class OutputRuleProcessorFieldTests
{
    [DataTestMethod]
    [DataRow("level", "Error")]
    [DataRow("source", "Auth")]
    [DataRow("message", "boom")]
    [DataRow("msg", "boom")]
    [DataRow("task", "Logon")]
    [DataRow("taskname", "Logon")]
    [DataRow("user", "alice")]
    [DataRow("searchable", "boom searchable")]
    public void GetFieldValue_MapsFieldNames(string field, string expected)
        => Assert.AreEqual(expected, OutputRuleProcessor.GetFieldValue(new R(), field));

    [TestMethod]
    public void GetFieldValue_TimeIsFormatted()
        => Assert.AreEqual("2026-03-15 09:30:00", OutputRuleProcessor.GetFieldValue(new R(), "time"));

    [TestMethod]
    public void GetFieldValue_UnknownField_DefaultsToSearchableData()
        => Assert.AreEqual("boom searchable", OutputRuleProcessor.GetFieldValue(new R(), "no_such_field"));

    [TestMethod]
    public void EscapeCsvField_QuotesWhenNeeded()
    {
        Assert.AreEqual("plain", OutputRuleProcessor.EscapeCsvField("plain"));
        Assert.AreEqual("\"a,b\"", OutputRuleProcessor.EscapeCsvField("a,b"));
        Assert.AreEqual("\"say \"\"hi\"\"\"", OutputRuleProcessor.EscapeCsvField("say \"hi\""));
        Assert.AreEqual("\"l1\nl2\"", OutputRuleProcessor.EscapeCsvField("l1\nl2"));
        Assert.AreEqual("", OutputRuleProcessor.EscapeCsvField(""));
        Assert.AreEqual("", OutputRuleProcessor.EscapeCsvField(null));
    }

    private sealed class R : ISearchResult
    {
        public DateTime GetLogTime() => new(2026, 3, 15, 9, 30, 0, DateTimeKind.Utc);
        public string GetMachineName() => "M";
        public void WriteToConsole() { }
        public Level GetLevel() => Level.Error;
        public string GetUsername() => "alice";
        public string GetTaskName() => "Logon";
        public string GetOpCode() => "";
        public string GetSource() => "Auth";
        public string GetSearchableData() => "boom searchable";
        public string GetMessage() => "boom";
        public string GetResultSource() => @"C:\logs\a.log";
    }
}
