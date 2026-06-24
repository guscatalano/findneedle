using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FindNeedleUX.Services;
using FindNeedleUX.Services.Mcp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Mcp;

/// <summary>
/// Covers the tools added on top of the base catalog: save/open_workspace, list_tags, filter_by_tag,
/// get_context, get/set_setting. Delegation is checked through the singleton bridge + a fake viewer;
/// settings round-trip uses the settings test seam; workspace round-trips through MiddleLayerService.
/// </summary>
[TestClass]
[TestCategory("Mcp")]
[DoNotParallelize]
public class McpNewToolsTests
{
    private FakeViewerController _fake;

    [TestInitialize]
    public void Setup()
    {
        _fake = new FakeViewerController();
        McpViewerBridge.Instance.UiDispatcher = null; // run inline (no UI thread in tests)
        McpViewerBridge.Instance.RegisterViewer(_fake);
    }

    [TestCleanup]
    public void Cleanup() => McpViewerBridge.Instance.UnregisterViewer(_fake);

    [TestMethod]
    public void Catalog_ContainsNewTools()
    {
        var names = McpTools.All.Select(t => t.Name).ToHashSet();
        foreach (var e in new[]
        {
            "save_workspace", "open_workspace", "list_tags", "filter_by_tag",
            "get_context", "get_setting", "set_setting",
        })
            Assert.IsTrue(names.Contains(e), $"catalog should expose '{e}'");
    }

    [TestMethod]
    public async Task ListTags_DelegatesToViewer()
    {
        var tags = await McpViewerBridge.Instance.GetTagCountsAsync();
        Assert.AreEqual(1, tags.Count);
        Assert.AreEqual("Important", tags[0].Tag);
        Assert.AreEqual(2, tags[0].Count);
    }

    [TestMethod]
    public async Task FilterByTag_PassesTagThrough()
    {
        var rows = await McpViewerBridge.Instance.GetTaggedRowsAsync("Question");
        Assert.AreEqual("Question", _fake.LastTaggedFilter);
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(100, rows[0].RowId);
    }

    [TestMethod]
    public async Task GetContext_PassesArgsThrough()
    {
        var ctx = await McpViewerBridge.Instance.GetContextAsync(77, 3, 9);
        Assert.AreEqual(77, _fake.LastContextRowId);
        Assert.AreEqual(3, _fake.LastContextBefore);
        Assert.AreEqual(9, _fake.LastContextAfter);
        Assert.IsTrue(ctx.Found);
        Assert.AreEqual(77, ctx.RowId);
    }

    [TestMethod]
    public async Task Setting_SetThenGet_RoundTrips()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "fn_mcp_settings_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            ResultsViewerSettings.SetStorageLocationForTests(tmp);

            await McpViewerBridge.Instance.SetSettingAsync("ThemeName", "Vivid");
            await McpViewerBridge.Instance.SetSettingAsync("PageSize", "250");

            // The set reached the real setting.
            Assert.AreEqual("Vivid", ResultsViewerSettings.ThemeName);
            Assert.AreEqual(250, ResultsViewerSettings.PageSize);

            // get_setting reports the value (serialize the anonymous result to avoid cross-assembly dynamic).
            var themeJson = JsonSerializer.Serialize(await McpViewerBridge.Instance.GetSettingAsync("ThemeName"));
            StringAssert.Contains(themeJson, "Vivid");
        }
        finally
        {
            ResultsViewerSettings.ResetStorageForTests();
            try { File.Delete(tmp); } catch { }
        }
    }

    [TestMethod]
    public async Task SetSetting_UnknownKey_Throws()
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(
            () => McpViewerBridge.Instance.SetSettingAsync("Bogus", "x"));
    }

    [TestMethod]
    public async Task Workspace_SaveThenOpen_RestoresLocation()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fn_mcp_ws_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.log"), "[2026-01-01 00:00:00] INFO hello");
        var wsPath = Path.Combine(Path.GetTempPath(), "fn_mcp_ws_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            MiddleLayerService.NewWorkspace();
            MiddleLayerService.AddFolderLocation(dir);
            Assert.AreEqual(1, MiddleLayerService.Locations.Count, "location added");

            await McpViewerBridge.Instance.SaveWorkspaceAsync(wsPath);
            Assert.IsTrue(File.Exists(wsPath), "workspace .json written (Save was a no-op before the fix)");

            MiddleLayerService.NewWorkspace();
            Assert.AreEqual(0, MiddleLayerService.Locations.Count, "New cleared the workspace");

            await McpViewerBridge.Instance.OpenWorkspaceAsync(wsPath);
            Assert.AreEqual(1, MiddleLayerService.Locations.Count, "location restored from the saved workspace");
        }
        finally
        {
            MiddleLayerService.NewWorkspace();
            try { File.Delete(wsPath); } catch { }
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
