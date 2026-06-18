using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FindNeedleUX.Services.Mcp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Mcp;

/// <summary>
/// End-to-end tests of the loopback MCP server: real HttpListener on a free port, JSON-RPC over
/// HttpClient. Validates initialize / tools/list / tools/call wiring and the no-viewer error path.
/// </summary>
[TestClass]
[TestCategory("Mcp")]
[DoNotParallelize]
public class McpServerTests
{
    private McpServer _server;
    private HttpClient _http;
    private FakeViewerController _fake;
    private string _url;

    [TestInitialize]
    public void Setup()
    {
        var port = FreePort();
        _server = new McpServer();
        _server.Start(port);
        _url = $"http://127.0.0.1:{port}/";
        _http = new HttpClient();
        _fake = new FakeViewerController();
        McpViewerBridge.Instance.UiDispatcher = null; // run inline
    }

    [TestCleanup]
    public void Cleanup()
    {
        McpViewerBridge.Instance.UnregisterViewer(_fake);
        _http?.Dispose();
        _server?.Dispose();
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    private async Task<JsonElement> RpcAsync(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        using var resp = await _http.PostAsync(_url, new StringContent(json, Encoding.UTF8, "application/json"));
        var body = await resp.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    [TestMethod]
    public async Task Initialize_ReturnsServerInfo()
    {
        var r = await RpcAsync(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2024-11-05" } });
        var result = r.GetProperty("result");
        Assert.AreEqual("2024-11-05", result.GetProperty("protocolVersion").GetString());
        Assert.AreEqual("findneedle", result.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.IsTrue(result.GetProperty("capabilities").TryGetProperty("tools", out _), "advertises tools capability");
    }

    [TestMethod]
    public async Task ToolsList_IncludesCoreTools()
    {
        var r = await RpcAsync(new { jsonrpc = "2.0", id = 2, method = "tools/list" });
        var tools = r.GetProperty("result").GetProperty("tools");
        bool hasGetView = false, hasExport = false;
        foreach (var t in tools.EnumerateArray())
        {
            var name = t.GetProperty("name").GetString();
            Assert.IsTrue(t.TryGetProperty("inputSchema", out _), $"{name} exposes an inputSchema");
            if (name == "get_view") hasGetView = true;
            if (name == "export") hasExport = true;
        }
        Assert.IsTrue(hasGetView && hasExport, "tools/list includes get_view and export");
    }

    [TestMethod]
    public async Task ToolsCall_GetView_DelegatesToViewer()
    {
        McpViewerBridge.Instance.RegisterViewer(_fake);

        var r = await RpcAsync(new { jsonrpc = "2.0", id = 3, method = "tools/call", @params = new { name = "get_view", arguments = new { } } });
        var result = r.GetProperty("result");

        Assert.IsFalse(result.GetProperty("isError").GetBoolean(), "successful call is not an error");
        var text = result.GetProperty("content")[0].GetProperty("text").GetString();
        Assert.IsTrue(text.Contains("hello"), "tool result carries the controller's data: " + text);
        Assert.AreEqual(1, _fake.GetViewCalls);
    }

    [TestMethod]
    public async Task ToolsCall_ViewerToolWithNoViewer_ReportsErrorContent()
    {
        McpViewerBridge.Instance.UnregisterViewer(_fake); // no viewer registered

        var r = await RpcAsync(new { jsonrpc = "2.0", id = 4, method = "tools/call", @params = new { name = "get_view", arguments = new { } } });
        var result = r.GetProperty("result");

        Assert.IsTrue(result.GetProperty("isError").GetBoolean(), "no-viewer surfaces as an error result");
        var text = result.GetProperty("content")[0].GetProperty("text").GetString();
        Assert.IsTrue(text.ToLowerInvariant().Contains("no result viewer") || text.ToLowerInvariant().Contains("no active"),
            "error explains there's no viewer: " + text);
    }

    [TestMethod]
    public async Task UnknownMethod_ReturnsJsonRpcError()
    {
        var r = await RpcAsync(new { jsonrpc = "2.0", id = 5, method = "does/not/exist" });
        Assert.IsTrue(r.TryGetProperty("error", out var err), "unknown method returns a JSON-RPC error");
        Assert.AreEqual(-32601, err.GetProperty("code").GetInt32());
    }
}
