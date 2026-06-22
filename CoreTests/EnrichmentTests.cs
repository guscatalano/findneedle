using System;
using System.Collections.Generic;
using System.Text.Json;
using findneedle.RuleDSL;
using FindNeedlePluginLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

/// <summary>
/// RuleDSL field-extraction enrichment: the "extract" action fills EvaluationResult.Fields from named
/// regex groups + literals, and EnrichedSearchResult surfaces those as overridden row fields.
/// </summary>
[TestClass]
public class EnrichmentTests
{
    private sealed class StubResult : ISearchResult
    {
        public string Text = "";
        public DateTime GetLogTime() => new(2026, 6, 20, 9, 33, 36);
        public string GetMachineName() => "";
        public void WriteToConsole() { }
        public Level GetLevel() => Level.Info;
        public string GetUsername() => "";
        public string GetTaskName() => "";
        public string GetOpCode() => "";
        public string GetSource() => "dism.log";
        public string GetSearchableData() => Text;
        public string GetMessage() => Text;
        public string GetResultSource() => @"C:\WINDOWS\Logs\DISM\dism.log";
    }

    // Two rules: set Provider literal on any DISM line, and PID/TID captures.
    private const string DismSectionJson = @"{
      ""rules"": [
        { ""name"":""prov"", ""field"":""message"", ""match"":""\\bDISM\\b"",
          ""action"": { ""type"":""extract"", ""pattern"":""\\bDISM\\b"", ""set"": { ""Source"":""DISM"" } } },
        { ""name"":""pidtid"", ""field"":""message"", ""match"":""PID=\\d+\\s+TID=\\d+"",
          ""action"": { ""type"":""extract"", ""pattern"":""PID=(?<pid>\\d+)\\s+TID=(?<tid>\\d+)"",
                        ""set"": { ""ProcessId"":""{pid}"", ""ThreadId"":""{tid}"" } } }
      ] }";

    [TestMethod]
    public void Extract_FillsFields_FromGroupsAndLiterals()
    {
        var engine = new RuleEvaluationEngine();
        using var doc = JsonDocument.Parse(DismSectionJson);
        var r = new StubResult { Text = "2026-06-20 09:33:36, Info  DISM  PID=24288 TID=15484 LoadLocalImageSession" };

        var eval = engine.EvaluateRules(r, doc.RootElement);

        Assert.AreEqual("24288", eval.Fields["ProcessId"]);
        Assert.AreEqual("15484", eval.Fields["ThreadId"]);
        Assert.AreEqual("DISM", eval.Fields["Source"]);
    }

    [TestMethod]
    public void Extract_NoMatch_ProducesNoFields()
    {
        var engine = new RuleEvaluationEngine();
        using var doc = JsonDocument.Parse(DismSectionJson);
        var r = new StubResult { Text = "an ordinary line with no dism marker or pid" };

        var eval = engine.EvaluateRules(r, doc.RootElement);

        Assert.AreEqual(0, eval.Fields.Count);
    }

    [TestMethod]
    public void EnrichedSearchResult_OverridesSetFields_DelegatesRest()
    {
        ISearchResult baseR = new StubResult { Text = "the message" };
        var dec = new EnrichedSearchResult(baseR, new Dictionary<string, string>
        {
            ["ProcessId"] = "24288",
            ["ThreadId"] = "15484",
            ["Source"] = "DISM",
        });

        // Overridden:
        Assert.AreEqual("24288", dec.GetProcessId());
        Assert.AreEqual("15484", dec.GetThreadId());
        Assert.AreEqual("DISM", dec.GetSource());
        // Delegated (not in the override map):
        Assert.AreEqual("the message", dec.GetMessage());
        Assert.AreEqual(baseR.GetResultSource(), dec.GetResultSource());
        Assert.AreEqual(Level.Info, dec.GetLevel());
        Assert.AreEqual("", dec.GetOpCode());
    }
}
