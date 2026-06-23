using System.Linq;
using FindPluginCore.Searching.RuleDSL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

[TestClass]
public class FieldExtractionPreviewTests
{
    private const string Rules = @"{
      ""schemaVersion"": ""2.0"",
      ""sections"": [{
        ""name"": ""t"", ""purpose"": ""enrichment"", ""providers"": [""*""],
        ""rules"": [
          { ""name"": ""pid"", ""field"": ""message"", ""match"": ""PID=\\d+"", ""enabled"": true,
            ""action"": { ""type"": ""extract"", ""pattern"": ""PID=(?<pid>\\d+)"", ""set"": { ""ProcessId"": ""{pid}"" }, ""strip"": true } }
        ]
      }]
    }";

    [TestMethod]
    public void Preview_ExtractsFieldAndStripsMessage()
    {
        var rows = FieldExtractionPreview.Run(Rules, new[] { "hello PID=1234 world" });
        Assert.AreEqual(1, rows.Count);
        var r = rows[0];
        Assert.AreEqual("1234", r.Fields["ProcessId"], "the named capture should land in the ProcessId column");
        Assert.AreEqual("hello world", r.After, "the matched 'PID=1234' should be stripped from the message");
        Assert.AreEqual("hello PID=1234 world", r.Before);
    }

    [TestMethod]
    public void Preview_NonMatchingLineIsUnchanged()
    {
        var rows = FieldExtractionPreview.Run(Rules, new[] { "nothing to extract here" });
        Assert.AreEqual(0, rows[0].Fields.Count, "no rule matched → no columns set");
        Assert.AreEqual("nothing to extract here", rows[0].After);
    }
}
