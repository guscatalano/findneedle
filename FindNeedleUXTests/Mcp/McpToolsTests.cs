using System.Linq;
using FindNeedleUX.Services.Mcp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Mcp;

/// <summary>
/// Sanity checks on the MCP tool catalog: names are unique and non-empty, each tool has a
/// description, an input schema, and a handler, and the expected core tools are present.
/// </summary>
[TestClass]
[TestCategory("Mcp")]
public class McpToolsTests
{
    [TestMethod]
    public void Catalog_IsWellFormed()
    {
        var all = McpTools.All;
        Assert.IsTrue(all.Count >= 15, "expected the full tool set");

        foreach (var t in all)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(t.Name), "tool has a name");
            Assert.IsFalse(string.IsNullOrWhiteSpace(t.Description), $"{t.Name} has a description");
            Assert.IsNotNull(t.InputSchema, $"{t.Name} has an input schema");
            Assert.IsNotNull(t.Invoke, $"{t.Name} has a handler");
        }

        var names = all.Select(t => t.Name).ToList();
        Assert.AreEqual(names.Count, names.Distinct().Count(), "tool names are unique");
    }

    [TestMethod]
    public void Catalog_ContainsExpectedTools()
    {
        var names = McpTools.All.Select(t => t.Name).ToHashSet();
        foreach (var expected in new[]
        {
            "list_locations", "run_search", "status", "wait_for_viewer",
            "get_view", "get_page", "get_record",
            "summary", "histogram", "search", "set_filter", "clear_filters",
            "set_sort", "goto_page", "set_page_size", "select_row", "tag_row",
            "clear_tag", "set_details_mode", "export",
        })
        {
            Assert.IsTrue(names.Contains(expected), $"catalog should expose '{expected}'");
        }
    }
}
