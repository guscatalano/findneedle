using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FindNeedleUX.Services.Mcp;

/// <summary>
/// The stdio face of the MCP server: what an MCP client spawns.
///
/// MCP clients treat a server as a process they own, so pointing one at the app's HTTP port fails
/// every time the app is not running. Instead the client runs <c>FindNeedleUX.exe --mcp-stdio</c>.
/// This process never shows a window: it reads newline-delimited JSON-RPC from stdin, makes sure the
/// real app is up (launching it if not - single-instancing means a launch while it runs just hands off
/// and exits), forwards each message to the app's loopback HTTP endpoint, and writes the reply to
/// stdout. If the app cannot be reached, requests get a JSON-RPC error that says so, rather than the
/// client seeing a dead connection.
///
/// Framing follows the MCP stdio transport: one JSON object per line, UTF-8, nothing else on stdout.
/// </summary>
public static class McpStdioBridge
{
    public const string Flag = "--mcp-stdio";
    private const int LaunchTimeoutMs = 90_000;

    public static int Run(string[] args)
    {
        using var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) }; // a run_search can legitimately take a while
        var bridge = new Session(http, EndpointFromSettings());
        Log($"stdio bridge up; endpoint {bridge.Endpoint}");

        string? line;
        while ((line = stdin.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var reply = bridge.HandleAsync(line).GetAwaiter().GetResult();
            if (reply != null)
            {
                lock (stdout) stdout.WriteLine(reply);
            }
        }
        Log("stdin closed; bridge exiting");
        return 0;
    }

    /// <summary>The app's MCP endpoint per the persisted settings (port), or the default.</summary>
    public static string EndpointFromSettings()
    {
        int port;
        try { port = ResultsViewerSettings.McpServerPort; } catch { port = ResultsViewerSettings.DefaultMcpServerPort; }
        return $"http://127.0.0.1:{port}/";
    }

    private static void Log(string message)
    {
        try { FindNeedlePluginLib.Logger.Instance.Log($"[mcp-stdio] {message}"); } catch { /* logging is best-effort */ }
    }

    /// <summary>One bridge session. Pure enough to unit-test: the HTTP side and the launcher are seams.</summary>
    public sealed class Session
    {
        private readonly HttpClient _http;
        public string Endpoint { get; }

        /// <summary>Starts the app (default: the same exe without the flag). Replaceable for tests.</summary>
        public Func<Task<bool>> EnsureAppRunning { get; set; }

        public Session(HttpClient http, string endpoint)
        {
            _http = http;
            Endpoint = endpoint;
            EnsureAppRunning = DefaultEnsureAppRunningAsync;
        }

        /// <summary>Handle one inbound line: forward it, or answer with an error if the app is unreachable.
        /// Returns the line to write back, or null for a notification that produced nothing.</summary>
        public async Task<string?> HandleAsync(string line)
        {
            JsonElement? id = null;
            bool isRequest;
            try
            {
                using var doc = JsonDocument.Parse(line);
                isRequest = doc.RootElement.TryGetProperty("id", out var idEl);
                if (isRequest) id = idEl.Clone();
            }
            catch (JsonException)
            {
                return ErrorReply(null, -32700, "Parse error: the line was not valid JSON");
            }

            if (!await EnsureAppRunning().ConfigureAwait(false))
                return isRequest
                    ? ErrorReply(id, -32000, "FindNeedle is not running and could not be started. Start it (Settings ▸ Result viewer ▸ MCP server must be enabled) and try again.")
                    : null;

            try
            {
                using var content = new StringContent(line, Encoding.UTF8, "application/json");
                using var resp = await _http.PostAsync(Endpoint, content).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!isRequest) return null; // notifications get no reply on stdio
                if (string.IsNullOrWhiteSpace(body))
                    return ErrorReply(id, -32000, $"FindNeedle answered HTTP {(int)resp.StatusCode} with no body");
                return body.Trim();
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                return isRequest ? ErrorReply(id, -32000, $"FindNeedle stopped answering: {ex.Message}") : null;
            }
        }

        /// <summary>A JSON-RPC error object as a single line.</summary>
        public static string ErrorReply(JsonElement? id, int code, string message)
        {
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms))
            {
                w.WriteStartObject();
                w.WriteString("jsonrpc", "2.0");
                if (id.HasValue) { w.WritePropertyName("id"); id.Value.WriteTo(w); } else w.WriteNull("id");
                w.WritePropertyName("error");
                w.WriteStartObject();
                w.WriteNumber("code", code);
                w.WriteString("message", message);
                w.WriteEndObject();
                w.WriteEndObject();
            }
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        /// <summary>True when the app's MCP endpoint answers a ping.</summary>
        public async Task<bool> IsUpAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(2000);
                using var content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":0,\"method\":\"ping\"}", Encoding.UTF8, "application/json");
                using var resp = await _http.PostAsync(Endpoint, content, cts.Token).ConfigureAwait(false);
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        private bool _launched;

        /// <summary>Ping; if nothing answers, launch the app once and wait for the port. The launch enables the
        /// MCP server in the settings file first, so a fresh install answers without a trip to Settings.</summary>
        private async Task<bool> DefaultEnsureAppRunningAsync()
        {
            if (await IsUpAsync().ConfigureAwait(false)) return true;
            if (_launched) return await WaitForPortAsync(15_000).ConfigureAwait(false); // launched earlier; still coming up?
            _launched = true;

            try
            {
                if (!ResultsViewerSettings.McpServerEnabled) ResultsViewerSettings.McpServerEnabled = true;
            }
            catch (Exception ex) { Log($"could not enable the MCP server setting: {ex.Message}"); }

            var exe = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) { Log("cannot locate FindNeedleUX.exe to launch"); return false; }
            try
            {
                // No arguments: a normal launch. If the app is already running, single-instancing hands this
                // activation to it and this child exits - which is fine, the port will answer either way.
                var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) ?? "" };
                psi.Environment.Remove("FINDNEEDLE_NO_SINGLE_INSTANCE");
                Process.Start(psi);
                Log("launched FindNeedleUX for the MCP client");
            }
            catch (Exception ex) { Log($"launch failed: {ex.Message}"); return false; }

            return await WaitForPortAsync(LaunchTimeoutMs).ConfigureAwait(false);
        }

        private async Task<bool> WaitForPortAsync(int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (await IsUpAsync().ConfigureAwait(false)) return true;
                await Task.Delay(500).ConfigureAwait(false);
            }
            Log($"the MCP endpoint {Endpoint} did not answer within {timeoutMs / 1000}s");
            return false;
        }
    }
}
