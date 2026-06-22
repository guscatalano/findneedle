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
                        ""set"": { ""ProcessId"":""{pid}"", ""ThreadId"":""{tid}"" }, ""strip"": true } }
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

        // strip:true on the pid/tid rule records the matched span (the "PID=… TID=…" text to remove).
        Assert.AreEqual(1, eval.MessageStrips.Count);
        var (start, len) = eval.MessageStrips[0];
        Assert.AreEqual("PID=24288 TID=15484", r.Text.Substring(start, len));
    }

    [TestMethod]
    public void Extract_StripOnly_RecordsSpan_NoFields()
    {
        const string json = @"{ ""rules"": [
          { ""name"":""lvl"", ""field"":""message"", ""match"":""\\bDISM\\b"",
            ""action"": { ""type"":""extract"", ""pattern"":"",\\s+(?<lvl>Info|Warning|Error)\\b"", ""strip"": true } } ] }";
        var engine = new RuleEvaluationEngine();
        using var doc = JsonDocument.Parse(json);
        var r = new StubResult { Text = "2026-06-20 09:33:36, Info  DISM  payload" };

        var eval = engine.EvaluateRules(r, doc.RootElement);

        Assert.AreEqual(0, eval.Fields.Count, "strip-only sets no fields");
        Assert.AreEqual(1, eval.MessageStrips.Count, "strip-only still records the matched span");
        var (start, len) = eval.MessageStrips[0];
        Assert.AreEqual(", Info", r.Text.Substring(start, len));
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
            ["Message"] = "rewritten clean line",
        });

        // Overridden:
        Assert.AreEqual("24288", dec.GetProcessId());
        Assert.AreEqual("15484", dec.GetThreadId());
        Assert.AreEqual("DISM", dec.GetSource());
        Assert.AreEqual("rewritten clean line", dec.GetMessage()); // Message can be rewritten
        // SearchableData stays the original even when Message is rewritten (search still matches raw):
        Assert.AreEqual("the message", dec.GetSearchableData());
        Assert.AreEqual(baseR.GetResultSource(), dec.GetResultSource());
        Assert.AreEqual(Level.Info, dec.GetLevel());
        Assert.AreEqual("", dec.GetOpCode());
    }
}
