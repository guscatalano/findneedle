using System.Linq;
using FindNeedleUX.Services.Mcp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Tests for <see cref="McpActivityLog"/> — the "who's connected / last commands" ring buffer behind the
/// MCP status button. [DoNotParallelize] because it mutates a static buffer.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
[DoNotParallelize]
public class McpActivityLogTests
{
    [TestInitialize]
    public void Setup() => McpActivityLog.ClearForTests();

    [TestCleanup]
    public void Cleanup() => McpActivityLog.ClearForTests();

    [TestMethod]
    public void Record_TracksRecentCommands_NewestFirst()
    {
        McpActivityLog.Record("claude", "tools/call", "search", "{}");
        McpActivityLog.Record("claude", "tools/call", "get_page", "{}");
        var recent = McpActivityLog.RecentCommands(10);
        Assert.AreEqual(2, recent.Count);
        Assert.AreEqual("get_page", recent[0].Tool, "newest command first");
        Assert.AreEqual("search", recent[1].Tool);
    }

    [TestMethod]
    public void RecentCommands_IsBounded()
    {
        for (int i = 0; i < 150; i++) McpActivityLog.Record("c", "ping", null, null);
        // Capacity is 100; asking for 200 returns at most the buffer size.
        Assert.IsTrue(McpActivityLog.RecentCommands(200).Count <= 100);
    }

    [TestMethod]
    public void KnownClients_RollsUpPerClient()
    {
        McpActivityLog.Record("claude", "tools/call", "summary");
        McpActivityLog.Record("claude", "tools/call", "search");
        McpActivityLog.Record("other-agent", "initialize");

        var clients = McpActivityLog.KnownClients();
        Assert.AreEqual(2, clients.Count);
        var claude = clients.First(c => c.Name == "claude");
        Assert.AreEqual(2, claude.Commands);
    }

    [TestMethod]
    public void ActiveClients_RecentlySeen_AreActive()
    {
        McpActivityLog.Record("claude", "ping");
        Assert.IsTrue(McpActivityLog.ActiveClients().Any(c => c.Name == "claude"));
    }

    [TestMethod]
    public void Record_NullClient_BecomesUnknown()
    {
        McpActivityLog.Record(null, "ping");
        Assert.IsTrue(McpActivityLog.KnownClients().Any(c => c.Name == "unknown"));
    }
}
