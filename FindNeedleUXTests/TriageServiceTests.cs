using System;
using System.IO;
using System.Linq;
using FindNeedlePluginLib;
using FindNeedleRuleDSL;
using FindNeedleUX.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUX.UnitTests;

/// <summary>
/// Tests the triage service's rule generation: a provider selection becomes a `scope` rules.json that the
/// engine compiles back into a DecodeScope which keeps exactly the chosen providers. (The provider scan and
/// the panel UI need a real file / the running app; this covers the testable logic.)
/// </summary>
[TestClass]
public class TriageServiceTests
{
    [TestMethod]
    public void WriteScopeRuleFile_Include_ProducesScopeThatKeepsOnlySelected()
    {
        string path = TriageService.WriteScopeRuleFile(new[] { "Microsoft-Windows-DotNETRuntime", "Microsoft-Windows-RPC" });
        try
        {
            Assert.IsTrue(File.Exists(path), "a scope rules.json should be written");
            var set = System.Text.Json.JsonSerializer.Deserialize<UnifiedRuleSet>(File.ReadAllText(path));
            var scopeSections = set!.Sections.Where(s => string.Equals(s.Purpose, "scope", StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.AreEqual(0, ScopeRuleParser.Validate(scopeSections).Count, "generated scope must validate");
            var scope = ScopeRuleParser.Build(scopeSections);
            Assert.IsNotNull(scope);
            Assert.IsTrue(scope!.Keep("Microsoft-Windows-DotNETRuntime", DateTime.UtcNow, -1));
            Assert.IsTrue(scope.Keep("Microsoft-Windows-RPC", DateTime.UtcNow, -1));
            Assert.IsFalse(scope.Keep("Windows Kernel", DateTime.UtcNow, -1), "unselected provider dropped");
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [TestMethod]
    public void WriteScopeRuleFile_Exclude_ProducesScopeThatDropsSelected()
    {
        string path = TriageService.WriteScopeRuleFile(new[] { "Windows Kernel", "MSNT_SystemTrace" }, exclude: true);
        try
        {
            var set = System.Text.Json.JsonSerializer.Deserialize<UnifiedRuleSet>(File.ReadAllText(path));
            var scope = ScopeRuleParser.Build(set!.Sections.Where(s => s.Purpose == "scope"));
            Assert.IsNotNull(scope);
            Assert.IsFalse(scope!.Keep("Windows Kernel", DateTime.UtcNow, -1), "excluded provider dropped");
            Assert.IsTrue(scope.Keep("Microsoft-Windows-DotNETRuntime", DateTime.UtcNow, -1), "others kept");
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [TestMethod]
    public void ShouldOfferTriage_FalseForMissingOrSmall()
    {
        Assert.IsFalse(TriageService.ShouldOfferTriage(null));
        Assert.IsFalse(TriageService.ShouldOfferTriage(@"C:\does\not\exist.etl"));
    }
}
