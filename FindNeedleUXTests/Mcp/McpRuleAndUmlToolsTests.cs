using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FindNeedleUX.Services.Mcp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Mcp;

/// <summary>
/// Covers the rule-authoring helpers (rule_schema, rule_examples), the UML-tool status/install tools,
/// and the validate_rule schema hint. These don't need an open viewer.
/// </summary>
[TestClass]
[TestCategory("Mcp")]
[DoNotParallelize]
public class McpRuleAndUmlToolsTests
{
    [TestMethod]
    public void Catalog_ContainsRuleAndUmlTools()
    {
        var names = McpTools.All.Select(t => t.Name).ToHashSet();
        foreach (var e in new[] { "rule_schema", "rule_examples", "uml_tools", "install_uml_tool" })
            Assert.IsTrue(names.Contains(e), $"catalog should expose '{e}'");
    }

    [TestMethod]
    public async Task RuleSchema_HasEnumerationsAndValidExamples()
    {
        var el = JsonSerializer.SerializeToElement(await McpViewerBridge.Instance.RuleSchemaAsync());

        var purposes = el.GetProperty("purposes").EnumerateArray().Select(x => x.GetString()).ToList();
        CollectionAssert.AreEquivalent(new[] { "filter", "enrichment", "output" }, purposes);
        Assert.IsTrue(el.GetProperty("matchFields").GetArrayLength() > 0);

        var examples = el.GetProperty("examples");
        Assert.IsTrue(examples.GetArrayLength() >= 3, "should ship >= 3 examples");

        // Every advertised example must actually pass validate_rule.
        foreach (var ex in examples.EnumerateArray())
        {
            var json = ex.GetProperty("json").GetString();
            var v = JsonSerializer.SerializeToElement(await McpViewerBridge.Instance.ValidateRuleAsync(json));
            Assert.IsTrue(v.GetProperty("valid").GetBoolean(),
                $"example '{ex.GetProperty("title").GetString()}' must validate; got {v}");
        }
    }

    [TestMethod]
    public async Task RuleExamples_ListsShippedAndFetchesByName()
    {
        var el = JsonSerializer.SerializeToElement(await McpViewerBridge.Instance.RuleExamplesAsync(null));
        var examples = el.GetProperty("examples");
        if (examples.GetArrayLength() == 0)
        {
            Assert.Inconclusive("CommonRules not deployed to the test output — list is empty here.");
            return;
        }
        var first = examples.EnumerateArray().First().GetProperty("name").GetString();
        var fetched = JsonSerializer.SerializeToElement(await McpViewerBridge.Instance.RuleExamplesAsync(first));
        Assert.IsTrue(fetched.GetProperty("content").GetString().Length > 0, "named fetch returns the JSON content");
    }

    [TestMethod]
    public async Task UmlTools_ReportsBothToolsWithFields()
    {
        var el = JsonSerializer.SerializeToElement(await McpViewerBridge.Instance.UmlToolsAsync());
        foreach (var t in new[] { "mermaid", "plantuml" })
        {
            var node = el.GetProperty(t);
            Assert.IsTrue(node.TryGetProperty("installed", out _), $"{t}.installed");
            Assert.IsTrue(node.TryGetProperty("imageSupported", out _), $"{t}.imageSupported");
        }
    }

    [TestMethod]
    public async Task ValidateRule_Invalid_HintsAtSchema()
    {
        var el = JsonSerializer.SerializeToElement(await McpViewerBridge.Instance.ValidateRuleAsync("{ not json"));
        Assert.IsFalse(el.GetProperty("valid").GetBoolean());
        Assert.IsTrue((el.GetProperty("hint").GetString() ?? "").Contains("rule_schema"));
    }
}
