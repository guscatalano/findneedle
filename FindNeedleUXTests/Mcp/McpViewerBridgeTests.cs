using System.Threading.Tasks;
using FindNeedleUX.Services.Mcp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Mcp;

/// <summary>
/// Tests the bridge contract: viewer ops are delegated to the registered controller, and throw a
/// clear <see cref="McpNoViewerException"/> when none is registered. Uses the singleton bridge, so
/// these don't run in parallel and always unregister in cleanup.
/// </summary>
[TestClass]
[TestCategory("Mcp")]
[DoNotParallelize]
public class McpViewerBridgeTests
{
    private FakeViewerController _fake;

    [TestInitialize]
    public void Setup()
    {
        _fake = new FakeViewerController();
        McpViewerBridge.Instance.UiDispatcher = null; // run inline (no UI thread in tests)
    }

    [TestCleanup]
    public void Cleanup() => McpViewerBridge.Instance.UnregisterViewer(_fake);

    [TestMethod]
    public async Task ViewerOp_WithNoViewer_ThrowsNoViewer()
    {
        McpViewerBridge.Instance.UnregisterViewer(_fake); // ensure none registered
        await Assert.ThrowsExceptionAsync<McpNoViewerException>(() => McpViewerBridge.Instance.GetViewAsync());
    }

    [TestMethod]
    public async Task ViewerOp_DelegatesToRegisteredController()
    {
        McpViewerBridge.Instance.RegisterViewer(_fake);

        var view = await McpViewerBridge.Instance.GetViewAsync();

        Assert.AreEqual(1, _fake.GetViewCalls, "call reached the controller");
        Assert.AreEqual("hello", view.Search);
        Assert.AreEqual(7, view.TotalFiltered);
    }

    [TestMethod]
    public async Task TagAndRecord_PassArgsThrough()
    {
        McpViewerBridge.Instance.RegisterViewer(_fake);

        await McpViewerBridge.Instance.TagRowAsync(55, "Important", "look at this");
        var rec = await McpViewerBridge.Instance.GetRecordAsync(123);

        Assert.AreEqual(55, _fake.LastTagId);
        Assert.AreEqual("Important", _fake.LastTag);
        Assert.AreEqual("look at this", _fake.LastTagText);
        Assert.AreEqual(123, _fake.LastRecordId);
        Assert.AreEqual(123, rec.RowId);
    }

    [TestMethod]
    public async Task Status_ReportsHasViewer_BothWays()
    {
        McpViewerBridge.Instance.UnregisterViewer(_fake);
        var before = await McpViewerBridge.Instance.GetStatusAsync();
        Assert.IsFalse(before.HasViewer, "no viewer registered yet");
        Assert.IsNull(before.Total, "no counts without a viewer");

        McpViewerBridge.Instance.RegisterViewer(_fake);
        var after = await McpViewerBridge.Instance.GetStatusAsync();
        Assert.IsTrue(after.HasViewer);
        Assert.AreEqual(9, after.Total, "counts come from the viewer when one is open");
    }

    [TestMethod]
    public async Task WaitForViewer_TimesOutThenSucceeds()
    {
        McpViewerBridge.Instance.UnregisterViewer(_fake);
        Assert.IsFalse(await McpViewerBridge.Instance.WaitForViewerAsync(200), "no viewer → returns false after the timeout");

        McpViewerBridge.Instance.RegisterViewer(_fake);
        Assert.IsTrue(await McpViewerBridge.Instance.WaitForViewerAsync(200), "viewer present → returns true immediately");
    }

    [TestMethod]
    public void Unregister_OtherController_DoesNotClearCurrent()
    {
        McpViewerBridge.Instance.RegisterViewer(_fake);
        McpViewerBridge.Instance.UnregisterViewer(new FakeViewerController()); // a different instance
        Assert.IsTrue(McpViewerBridge.Instance.HasViewer, "registering controller stays active");
    }
}
