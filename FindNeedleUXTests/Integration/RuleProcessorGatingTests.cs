using System;
using System.IO;
using FindNeedleUX.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Integration;

/// <summary>
/// Tests <see cref="MiddleLayerService.RuleFileHasProcessableSections"/>: only rules with a
/// filter/enrichment (non-output) section need a Step3 processor — which is what forces the full result
/// list to consolidate. An all-output ruleset (e.g. a UML diagram) must NOT, so plain viewing stays lazy.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class RuleProcessorGatingTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"FN_gate_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string Write(string name, string json)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, json);
        return p;
    }

    [TestMethod]
    public void OutputOnlyRule_DoesNotNeedAProcessor()
    {
        var p = Write("uml.rules.json", """
        { "sections": [ { "name": "D", "purpose": "output", "providers": ["*"],
            "rules": [ { "name": "u", "match": "x", "action": { "type": "uml" } } ] } ] }
        """);
        Assert.IsFalse(MiddleLayerService.RuleFileHasProcessableSections(p));
    }

    [TestMethod]
    public void FilterRule_NeedsAProcessor()
    {
        var p = Write("filter.rules.json", """
        { "sections": [ { "name": "F", "purpose": "filter", "providers": ["*"],
            "rules": [ { "name": "x", "match": "secret", "action": { "type": "exclude" } } ] } ] }
        """);
        Assert.IsTrue(MiddleLayerService.RuleFileHasProcessableSections(p));
    }

    [TestMethod]
    public void MixedRule_NeedsAProcessor()
    {
        var p = Write("mixed.rules.json", """
        { "sections": [
            { "name": "F", "purpose": "filter", "providers": ["*"], "rules": [] },
            { "name": "D", "purpose": "output", "providers": ["*"], "rules": [] } ] }
        """);
        Assert.IsTrue(MiddleLayerService.RuleFileHasProcessableSections(p));
    }

    [TestMethod]
    public void UnspecifiedPurpose_IsTreatedAsProcessable()
    {
        var p = Write("nopurpose.rules.json", """
        { "sections": [ { "name": "S", "providers": ["*"], "rules": [] } ] }
        """);
        Assert.IsTrue(MiddleLayerService.RuleFileHasProcessableSections(p));
    }

    [TestMethod]
    public void ShippedDismRule_IsOutputOnly()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        string dism = null;
        while (dir != null)
        {
            var c = Path.Combine(dir.FullName, "FindNeedleUX", "CommonRules", "dism-interaction.rules.json");
            if (File.Exists(c)) { dism = c; break; }
            dir = dir.Parent;
        }
        Assert.IsNotNull(dism, "shipped DISM emitter must exist");
        Assert.IsFalse(MiddleLayerService.RuleFileHasProcessableSections(dism),
            "the DISM emitter is output-only → must not force consolidation as a processor");
    }
}
