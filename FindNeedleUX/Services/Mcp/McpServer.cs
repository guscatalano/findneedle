using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FindNeedleUX.Services.Mcp;

/// <summary>
/// Minimal Model Context Protocol server over HTTP, bound to loopback only. Implements the
/// "Streamable HTTP" transport's request/response path (POST JSON-RPC → JSON result); it does not
/// push server-initiated messages, so a GET for SSE is answered 405. Uses the BCL
/// <see cref="HttpListener"/> so no ASP.NET Core / SDK dependency is pulled into the packaged app.
///
/// Supported JSON-RPC methods: initialize, notifications/initialized, ping, tools/list, tools/call.
/// Tool calls dispatch through <see cref="McpTools"/> to <see cref="McpViewerBridge"/>.
///
/// Security: binds <c>http://127.0.0.1:{port}/</c> and additionally rejects any request whose remote
/// endpoint isn't loopback. No auth token (localhost trust boundary, per design).
/// </summary>
public sealed class McpServer : IDisposable
{
    private const string ProtocolVersion = "2024-11-05";

    private HttpListener _listener;
    private CancellationTokenSource _cts;
    private readonly object _sync = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public bool IsRunning { get; private set; }
    public int Port { get; private set; }

    /// <summary>Optional sink for diagnostics (start/stop/errors). Set by the host.</summary>
    public Action<string> Log { get; set; }

    public void Start(int port)
    {
        lock (_sync)
        {
            if (IsRunning) return;
            var listener = new HttpListener();
            // 127.0.0.1 (not localhost/+) so a non-admin user can bind without a urlacl reservation.
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            _listener = listener;
            _cts = new CancellationTokenSource();
            Port = port;
            IsRunning = true;
            _ = AcceptLoopAsync(listener, _cts.Token);
            Log?.Invoke($"MCP server listening on http://127.0.0.1:{port}/");
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (!IsRunning) return;
            IsRunning = false;
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }
            _listener = null;
            Log?.Invoke("MCP server stopped.");
        }
    }

    public void Dispose() => Stop();

    private async Task AcceptLoopAsync(HttpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync().ConfigureAwait(false); }
            catch { break; } // listener stopped/disposed
            _ = HandleContextAsync(ctx);
        }
    }

    private async Task HandleContextAsync(HttpListenerContext ctx)
    {
        try
        {
            // Loopback-only guard.
            if (!IPAddress.IsLoopback(ctx.Request.RemoteEndPoint?.Address ?? IPAddress.None))
            {
                ctx.Response.StatusCode = 403;
                ctx.Response.Close();
                return;
            }

            if (ctx.Request.HttpMethod == "GET")
            {
                // No server-initiated SSE stream in this minimal transport.
                ctx.Response.StatusCode = 405;
                ctx.Response.Close();
                return;
            }
            if (ctx.Request.HttpMethod != "POST")
            {
                ctx.Response.StatusCode = 405;
                ctx.Response.Close();
                return;
            }

            string body;
            using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8))
                body = await reader.ReadToEndAsync().ConfigureAwait(false);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            // Batch (array) or single message.
            if (root.ValueKind == JsonValueKind.Array)
            {
                var responses = new System.Collections.Generic.List<object>();
                foreach (var msg in root.EnumerateArray())
                {
                    var r = await HandleMessageAsync(msg).ConfigureAwait(false);
                    if (r != null) responses.Add(r);
                }
                if (responses.Count == 0) { ctx.Response.StatusCode = 202; ctx.Response.Close(); return; }
                await WriteJsonAsync(ctx, responses).ConfigureAwait(false);
            }
            else
            {
                var r = await HandleMessageAsync(root).ConfigureAwait(false);
                if (r == null) { ctx.Response.StatusCode = 202; ctx.Response.Close(); return; }
                await WriteJsonAsync(ctx, r).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            try
            {
                ctx.Response.StatusCode = 200;
                await WriteJsonAsync(ctx, Error(null, -32700, "Parse/handler error: " + ex.Message)).ConfigureAwait(false);
            }
            catch { try { ctx.Response.Abort(); } catch { } }
        }
    }

    /// <summary>Handle one JSON-RPC message. Returns the response object, or null for a notification.</summary>
    private async Task<object> HandleMessageAsync(JsonElement msg)
    {
        JsonElement idEl = default;
        bool hasId = msg.TryGetProperty("id", out idEl);
        object id = hasId ? JsonElementToId(idEl) : null;
        string method = msg.TryGetProperty("method", out var mEl) ? mEl.GetString() : null;

        if (string.IsNullOrEmpty(method))
            return hasId ? Error(id, -32600, "Invalid request: missing method") : null;

        // Notifications (no id) get no response.
        switch (method)
        {
            case "notifications/initialized":
            case "notifications/cancelled":
                return null;

            case "initialize":
            {
                string clientProto = ProtocolVersion;
                if (msg.TryGetProperty("params", out var p) && p.TryGetProperty("protocolVersion", out var pv)
                    && pv.ValueKind == JsonValueKind.String)
                    clientProto = pv.GetString();
                return Result(id, new
                {
                    protocolVersion = clientProto,
                    capabilities = new { tools = new { listChanged = false } },
                    serverInfo = new { name = "findneedle", version = "0.0.1" },
                });
            }

            case "ping":
                return Result(id, new { });

            case "tools/list":
            {
                var tools = new System.Collections.Generic.List<object>();
                foreach (var t in McpTools.All)
                    tools.Add(new { name = t.Name, description = t.Description, inputSchema = t.InputSchema });
                return Result(id, new { tools });
            }

            case "tools/call":
                return await HandleToolCallAsync(id, msg).ConfigureAwait(false);

            default:
                return hasId ? Error(id, -32601, $"Method not found: {method}") : null;
        }
    }

    private async Task<object> HandleToolCallAsync(object id, JsonElement msg)
    {
        if (!msg.TryGetProperty("params", out var p) || !p.TryGetProperty("name", out var nameEl))
            return Error(id, -32602, "tools/call requires params.name");

        var name = nameEl.GetString();
        McpTools.ToolDef tool = null;
        foreach (var t in McpTools.All) if (t.Name == name) { tool = t; break; }
        if (tool == null) return Error(id, -32602, $"Unknown tool: {name}");

        // arguments is optional; default to an empty object.
        JsonElement args;
        if (p.TryGetProperty("arguments", out var aEl) && aEl.ValueKind == JsonValueKind.Object)
            args = aEl.Clone();
        else
            args = JsonDocument.Parse("{}").RootElement.Clone();

        try
        {
            var result = await tool.Invoke(args).ConfigureAwait(false);
            var text = JsonSerializer.Serialize(result, JsonOpts);
            return Result(id, new { content = new[] { new { type = "text", text } }, isError = false });
        }
        catch (McpNoViewerException ex)
        {
            return Result(id, new { content = new[] { new { type = "text", text = ex.Message } }, isError = true });
        }
        catch (Exception ex)
        {
            return Result(id, new { content = new[] { new { type = "text", text = $"{name} failed: {ex.Message}" } }, isError = true });
        }
    }

    // ----- JSON-RPC envelope helpers -----
    private static object Result(object id, object result) => new { jsonrpc = "2.0", id, result };
    private static object Error(object id, int code, string message) => new { jsonrpc = "2.0", id, error = new { code, message } };

    private static object JsonElementToId(JsonElement idEl) => idEl.ValueKind switch
    {
        JsonValueKind.String => idEl.GetString(),
        JsonValueKind.Number => idEl.TryGetInt64(out var l) ? l : idEl.GetDouble(),
        _ => null,
    };

    private static async Task WriteJsonAsync(HttpListenerContext ctx, object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = "application/json";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        ctx.Response.Close();
    }
}
