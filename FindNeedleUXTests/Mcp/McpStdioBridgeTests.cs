using System;
using System.Collections.Generic;
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

    private static (McpStdioBridge.Session session, FakeHandler http) Make(bool appUp = true)
    {
        var handler = new FakeHandler();
        var s = new McpStdioBridge.Session(new HttpClient(handler), "http://127.0.0.1:1/")
        {
            EnsureAppRunning = () => Task.FromResult(appUp),
        };
        return (s, handler);
    }

    [TestMethod]
    public async Task Request_IsForwardedVerbatim_AndTheReplyComesBackAsOneLine()
    {
        var (s, http) = Make();
        var reply = await s.HandleAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}");
        Assert.AreEqual(1, http.Bodies.Count);
        StringAssert.Contains(http.Bodies[0], "\"tools/list\"");
        Assert.IsNotNull(reply);
        Assert.IsFalse(reply!.Contains('\n'), "stdio framing: one line per message");
        using var doc = JsonDocument.Parse(reply);
        Assert.AreEqual(1, doc.RootElement.GetProperty("id").GetInt32());
    }

    [TestMethod]
    public async Task Notification_IsForwarded_ButGetsNoReply()
    {
        var (s, http) = Make();
        var reply = await s.HandleAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
        Assert.AreEqual(1, http.Bodies.Count, "the app still hears the notification");
        Assert.IsNull(reply, "nothing is written back for a notification");
    }

    [TestMethod]
    public async Task AppNotRunning_AnswersARequestWithAJsonRpcError_NotADeadConnection()
    {
        var (s, http) = Make(appUp: false);
        var reply = await s.HandleAsync("{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"initialize\"}");
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
        var (s, _) = Make(appUp: false);
        Assert.IsNull(await s.HandleAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"));
    }

    [TestMethod]
    public async Task AppDiesMidSession_ErrorNamesIt()
    {
        var (s, http) = Make();
        http.Throw = true;
        var reply = await s.HandleAsync("{\"jsonrpc\":\"2.0\",\"id\":\"abc\",\"method\":\"ping\"}");
        using var doc = JsonDocument.Parse(reply!);
        Assert.AreEqual("abc", doc.RootElement.GetProperty("id").GetString(), "string ids are echoed as strings");
        StringAssert.Contains(doc.RootElement.GetProperty("error").GetProperty("message").GetString(), "stopped answering");
    }

    [TestMethod]
    public async Task GarbageLine_IsAParseError()
    {
        var (s, http) = Make();
        var reply = await s.HandleAsync("this is not json");
        Assert.AreEqual(0, http.Bodies.Count);
        using var doc = JsonDocument.Parse(reply!);
        Assert.AreEqual(-32700, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [TestMethod]
    public void Endpoint_ComesFromTheSettingsPort()
    {
        StringAssert.StartsWith(McpStdioBridge.EndpointFromSettings(), "http://127.0.0.1:");
    }
}
