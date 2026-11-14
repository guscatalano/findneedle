using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FindNeedleUX.Services.Mcp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUX.UnitTests.Mcp;

/// <summary>The stdio bridge's session logic with the HTTP side and the launcher replaced by fakes.</summary>
[TestClass]
public class McpStdioBridgeTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public readonly List<string> Bodies = new();
        public Func<string, HttpResponseMessage> Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"ok\":true}}") };
        public bool Throw;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Bodies.Add(body);
            if (Throw) throw new HttpRequestException("connection refused");
            return Respond(body);
        }
    }

    /// <summary>Counts launches, because "did this start the app?" is the thing most worth asserting.</summary>
    private sealed class Launcher
    {
        public int Calls;
        public bool AppUp = true;
        public Task<bool> EnsureAsync() { Calls++; return Task.FromResult(AppUp); }
    }

    private static (McpStdioBridge.Session session, FakeHandler http, Launcher launcher) Make(bool appUp = true)
    {
        var handler = new FakeHandler();
        var launcher = new Launcher { AppUp = appUp };
        var s = new McpStdioBridge.Session(new HttpClient(handler), "http://127.0.0.1:1/")
        {
            EnsureAppRunning = launcher.EnsureAsync,
        };
        return (s, handler, launcher);
    }

    private const string ToolCall = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"status\"}}";

    [TestMethod]
    public async Task Request_IsForwardedVerbatim_AndTheReplyComesBackAsOneLine()
    {
        var (s, http, _) = Make();
        var reply = await s.HandleAsync(ToolCall);
        Assert.AreEqual(1, http.Bodies.Count);
        StringAssert.Contains(http.Bodies[0], "\"tools/call\"");
        Assert.IsNotNull(reply);
        Assert.IsFalse(reply!.Contains('\n'), "stdio framing: one line per message");
        using var doc = JsonDocument.Parse(reply);
        Assert.AreEqual(1, doc.RootElement.GetProperty("id").GetInt32());
    }

    [TestMethod]
    public async Task Notification_GetsNoReply_AndStartsNothing()
    {
        var (s, http, launcher) = Make();
        var reply = await s.HandleAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
        Assert.IsNull(reply, "nothing is written back for a notification");
        Assert.AreEqual(0, launcher.Calls, "a notification must never start the app");
        // It is no longer forwarded either, and nothing is lost by that: the server's own handler for
        // notifications/initialized and notifications/cancelled returns null and does nothing with
        // them. Cancelling a search goes through the cancel_search TOOL, which is a tools/call.
        Assert.AreEqual(0, http.Bodies.Count);
    }

    [TestMethod]
    public async Task AppNotRunning_AnswersARequestWithAJsonRpcError_NotADeadConnection()
    {
        var (s, http, _) = Make(appUp: false);
        var reply = await s.HandleAsync("{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"tools/call\",\"params\":{\"name\":\"status\"}}");
        Assert.AreEqual(0, http.Bodies.Count, "nothing is forwarded when the app is down");
        using var doc = JsonDocument.Parse(reply!);
        Assert.AreEqual(7, doc.RootElement.GetProperty("id").GetInt32(), "the error carries the request id");
        var err = doc.RootElement.GetProperty("error");
        Assert.AreEqual(-32000, err.GetProperty("code").GetInt32());
        StringAssert.Contains(err.GetProperty("message").GetString(), "not running");
    }

    [TestMethod]
    public async Task AppNotRunning_DropsANotificationSilently()
    {
        var (s, _, _) = Make(appUp: false);
        Assert.IsNull(await s.HandleAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"));
    }

    [TestMethod]
    public async Task AppDiesMidSession_ErrorNamesIt()
    {
        var (s, http, _) = Make();
        http.Throw = true;
        var reply = await s.HandleAsync("{\"jsonrpc\":\"2.0\",\"id\":\"abc\",\"method\":\"tools/call\",\"params\":{\"name\":\"status\"}}");
        using var doc = JsonDocument.Parse(reply!);
        Assert.AreEqual("abc", doc.RootElement.GetProperty("id").GetString(), "string ids are echoed as strings");
        StringAssert.Contains(doc.RootElement.GetProperty("error").GetProperty("message").GetString(), "stopped answering");
    }

    [TestMethod]
    public async Task GarbageLine_IsAParseError()
    {
        var (s, http, _) = Make();
        var reply = await s.HandleAsync("this is not json");
        Assert.AreEqual(0, http.Bodies.Count);
        using var doc = JsonDocument.Parse(reply!);
        Assert.AreEqual(-32700, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    // ---- the handshake must not open a window ----
    //
    // A FindNeedle window appeared every time any MCP client connected, in any directory, because the
    // bridge made sure the app was running before forwarding ANY line - and every client opens with
    // initialize + tools/list. Diagnosed from the process tree: claude.exe -> FindNeedleUX --mcp-stdio
    // -> FindNeedleUX (GUI), three seconds apart.

    [TestMethod]
    public async Task TheOpeningHandshake_StartsNothing()
    {
        var (s, http, launcher) = Make();
        foreach (var line in new[]
        {
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2024-11-05\"}}",
            "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}",
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}",
        })
            await s.HandleAsync(line);

        Assert.AreEqual(0, launcher.Calls, "connecting a client must not start the app - this is the bug");
        Assert.AreEqual(0, http.Bodies.Count, "and nothing needed the app's HTTP endpoint either");
    }

    [TestMethod]
    public async Task Initialize_IsAnsweredHere_AndEchoesTheClientsProtocolVersion()
    {
        var (s, _, launcher) = Make();
        var reply = await s.HandleAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\"}}");
        Assert.AreEqual(0, launcher.Calls);
        using var doc = JsonDocument.Parse(reply!);
        var result = doc.RootElement.GetProperty("result");
        Assert.AreEqual("2025-06-18", result.GetProperty("protocolVersion").GetString(), "the client's version is echoed");
        Assert.AreEqual("findneedle", result.GetProperty("serverInfo").GetProperty("name").GetString());
    }

    [TestMethod]
    public async Task ToolsList_IsAnsweredHere_WithTheRealCatalog()
    {
        var (s, _, launcher) = Make();
        var reply = await s.HandleAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}");
        Assert.AreEqual(0, launcher.Calls, "listing tools must not start the app");
        using var doc = JsonDocument.Parse(reply!);
        var tools = doc.RootElement.GetProperty("result").GetProperty("tools");
        Assert.IsTrue(tools.GetArrayLength() > 0, "the catalog is static data, so the bridge can serve it");
        // Same code path the HTTP server uses, so the two cannot disagree about the catalog.
        Assert.IsTrue(tools.EnumerateArray().Any(t => t.GetProperty("name").GetString() == "status"),
            "a known tool should be in the list the bridge serves");
    }

    [TestMethod]
    public async Task Ping_IsAnsweredHere()
    {
        var (s, _, launcher) = Make();
        var reply = await s.HandleAsync("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"ping\"}");
        Assert.AreEqual(0, launcher.Calls);
        using var doc = JsonDocument.Parse(reply!);
        Assert.IsTrue(doc.RootElement.TryGetProperty("result", out _), "ping gets an empty result, not an error");
    }

    [TestMethod]
    public async Task AnUnsupportedMethod_IsRefusedHere_WithoutStartingTheApp()
    {
        // Some clients probe resources/list or prompts/list. The app answers "method not found"
        // anyway, so starting it to say so would be the same window for no result.
        var (s, http, launcher) = Make();
        var reply = await s.HandleAsync("{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"resources/list\"}");
        Assert.AreEqual(0, launcher.Calls);
        Assert.AreEqual(0, http.Bodies.Count);
        using var doc = JsonDocument.Parse(reply!);
        Assert.AreEqual(-32601, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [TestMethod]
    public async Task AToolCall_DoesStartTheApp()
    {
        // The other half of the contract: the app is still started when something actually needs it.
        var (s, http, launcher) = Make();
        await s.HandleAsync(ToolCall);
        Assert.AreEqual(1, launcher.Calls, "a tool call needs the viewer, so it must start it");
        Assert.AreEqual(1, http.Bodies.Count);
    }

    [TestMethod]
    public void Endpoint_ComesFromTheSettingsPort()
    {
        StringAssert.StartsWith(McpStdioBridge.EndpointFromSettings(), "http://127.0.0.1:");
    }
}
