using Microsoft.VisualStudio.TestTools.UnitTesting;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace FindNeedleUX.UITests
{
    /// <summary>
    /// Tags outlive the session. The app is driven through its own MCP server (the surface an agent
    /// uses): open a log, tag a row, close the app, open the same log again in the same profile - the
    /// tag is back, on the same row, note intact, even though every row was handed a new id by the
    /// rescan. Also covers the other half: clearing a tag forgets it for good.
    /// </summary>
    [TestClass]
    [TestCategory("UITests")]
    [TestCategory("SkipCI")]
    [TestCategory("UiSmoke")] // self-contained (generated data): runs in the CI ui-smoke job
    public class TagPersistenceUITests
    {
        private static int _nextPort = 8871;

        private sealed class Session : IDisposable
        {
            public UIA3Automation Automation;
            public Application App;
            public AutomationElement Window;
            public int Port;
            public readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

            /// <summary>Call one MCP tool and return the JSON payload it produced.</summary>
            public JsonDocument Mcp(string tool, object args = null)
            {
                var body = JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "tools/call",
                    @params = new { name = tool, arguments = args ?? new { } },
                });
                Exception last = null;
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    try
                    {
                        var resp = Http.PostAsync($"http://127.0.0.1:{Port}/", new StringContent(body, Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
                        var json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("error", out var err)) throw new InvalidOperationException($"{tool}: {err}");
                        var result = doc.RootElement.GetProperty("result");
                        if (result.TryGetProperty("isError", out var isErr) && isErr.ValueKind == JsonValueKind.True)
                            throw new InvalidOperationException($"{tool} failed: {result}");
                        return JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString());
                    }
                    catch (HttpRequestException ex) { last = ex; Thread.Sleep(500); } // server still starting
                }
                throw new InvalidOperationException($"MCP server on port {Port} did not answer: {last?.Message}");
            }

            public void Dispose()
            {
                try { App?.Close(); } catch { }
                try { if (App != null && !App.HasExited) App.Kill(); } catch { }
                try { App?.Dispose(); } catch { }
                try { Automation?.Dispose(); } catch { }
                try { Http.Dispose(); } catch { }
                Thread.Sleep(1000);
            }
        }

        /// <summary>Launch an instance on a given settings profile, with the MCP server on a fresh port.</summary>
        private static Session Launch(string settingsPath, string args)
        {
            var s = new Session { Automation = new UIA3Automation(), Port = Interlocked.Increment(ref _nextPort) };
            var psi = new System.Diagnostics.ProcessStartInfo(UiTestHelpers.GetAppExecutablePath(), args) { UseShellExecute = false };
            psi.EnvironmentVariables["FINDNEEDLE_VIEWER_SETTINGS"] = settingsPath;
            File.WriteAllText(settingsPath, $"{{\"McpServerEnabled\":true,\"McpServerPort\":{s.Port}}}");
            s.App = Application.Launch(psi);
            Thread.Sleep(3000);
            s.Window = s.App.GetMainWindow(s.Automation);
            try { s.Window?.Patterns.Window.PatternOrDefault?.SetWindowVisualState(WindowVisualState.Maximized); } catch { }
            Assert.IsNotNull(s.Window, "the app window did not come up.");
            return s;
        }

        private static string WriteLog(int lines)
        {
            var path = Path.Combine(Path.GetTempPath(), $"fn_tagpersist_{Guid.NewGuid():N}.log");
            var start = new DateTime(2026, 1, 1, 0, 0, 0);
            using var sw = new StreamWriter(path, false, Encoding.ASCII);
            for (int i = 0; i < lines; i++)
            {
                sw.Write('['); sw.Write(start.AddSeconds(i).ToString("yyyy-MM-dd HH:mm:ss")); sw.Write("] ");
                sw.Write(i % 25 == 0 ? "ERROR: something failed on line " : "INFO: routine message line ");
                sw.Write(i); sw.Write('\n');
            }
            return path;
        }

        /// <summary>The first row of the loaded page, as (id, message).</summary>
        private static (long id, string message) RowAt(Session s, int index)
        {
            using var page = s.Mcp("get_page", new { offset = 0, limit = 50 });
            var rows = page.RootElement.GetProperty("rows");
            var row = rows[index];
            return (row.GetProperty("rowId").GetInt64(), row.GetProperty("message").GetString());
        }

        /// <summary>Count of one tag category in the viewer right now (0 when absent).</summary>
        private static int TagCount(Session s, string category)
        {
            using var doc = s.Mcp("list_tags");
            foreach (var t in doc.RootElement.GetProperty("tags").EnumerateArray())
                if (string.Equals(t.GetProperty("tag").GetString(), category, StringComparison.OrdinalIgnoreCase))
                    return t.GetProperty("count").GetInt32();
            return 0;
        }

        /// <summary>Launch with the log on the command line - the way Explorer opens one - so the viewer
        /// is up (MCP's viewer tools need one) and the rows have landed.</summary>
        private static Session LaunchWithLog(string settingsPath, string logPath)
        {
            var s = Launch(settingsPath, $"\"{logPath}\" --viewer=native");
            Assert.IsNotNull(UiTestHelpers.WaitForPopulatedGrid(s.Window, 60000), "the log did not load.");
            s.Mcp("wait_for_load", new { timeoutMs = 90000 }).Dispose();
            return s;
        }

        /// <summary>Swap the open viewer over to another log (viewer already up).</summary>
        private static void OpenAnotherLog(Session s, string logPath)
        {
            s.Mcp("clear_workspace").Dispose();
            s.Mcp("add_folder", new { path = logPath }).Dispose();
            s.Mcp("run_search").Dispose();
            s.Mcp("wait_for_viewer", new { timeoutMs = 60000 }).Dispose();
            s.Mcp("wait_for_load", new { timeoutMs = 90000 }).Dispose();
        }

        [TestMethod]
        [Timeout(300000)]
        public void ATaggedRow_IsStillTagged_AfterReopeningTheLog()
        {
            var log = WriteLog(200);
            var settings = Path.Combine(Path.GetTempPath(), $"fn_tagpersist_settings_{Guid.NewGuid():N}.json");
            string taggedMessage;
            try
            {
                using (var s = LaunchWithLog(settings, log))
                {
                    var (id, message) = RowAt(s, 7);
                    taggedMessage = message;
                    using (var tagged = s.Mcp("tag_row", new { id, tag = "Important", text = "the reset lands here" }))
                        Assert.IsTrue(tagged.RootElement.GetProperty("tagged").GetBoolean(), "the row should tag");
                    Assert.AreEqual(1, TagCount(s, "Important"), "the tag is applied in this session");
                }

                // Same profile, same log, a brand new process: the rows are rescanned and re-numbered.
                using (var s = LaunchWithLog(settings, log))
                {
                    Assert.AreEqual(1, TagCount(s, "Important"), "the tag should come back after the rescan");

                    using var rows = s.Mcp("filter_by_tag", new { tag = "Important" });
                    var list = rows.RootElement.GetProperty("rows");
                    Assert.AreEqual(1, list.GetArrayLength(), "exactly the row that was tagged");
                    Assert.AreEqual(taggedMessage, list[0].GetProperty("message").GetString(),
                        "the tag must land back on the same row, not merely on some row");
                }
            }
            finally
            {
                TryDelete(log, settings);
                TryDeleteDir(settings + ".row-tags");
            }
        }

        [TestMethod]
        [Timeout(300000)]
        public void ClearingATag_ForgetsIt_AcrossSessionsToo()
        {
            var log = WriteLog(120);
            var settings = Path.Combine(Path.GetTempPath(), $"fn_tagpersist_settings_{Guid.NewGuid():N}.json");
            try
            {
                using (var s = LaunchWithLog(settings, log))
                {
                    var (id, _) = RowAt(s, 3);
                    s.Mcp("tag_row", new { id, tag = "Question", text = "why here?" }).Dispose();
                    Assert.AreEqual(1, TagCount(s, "Question"));
                    using (var cleared = s.Mcp("clear_tag", new { id }))
                        Assert.IsTrue(cleared.RootElement.GetProperty("cleared").GetBoolean());
                    Assert.AreEqual(0, TagCount(s, "Question"), "gone from this session");
                }

                using (var s = LaunchWithLog(settings, log))
                {
                    Assert.AreEqual(0, TagCount(s, "Question"), "a cleared tag must not come back from the store");
                }
            }
            finally
            {
                TryDelete(log, settings);
                TryDeleteDir(settings + ".row-tags");
            }
        }

        [TestMethod]
        [Timeout(300000)]
        public void TagsBelongToTheirOwnLog_NotToWhateverIsOpen()
        {
            var tagged = WriteLog(100);
            var other = WriteLog(100);
            var settings = Path.Combine(Path.GetTempPath(), $"fn_tagpersist_settings_{Guid.NewGuid():N}.json");
            try
            {
                using (var s = LaunchWithLog(settings, tagged))
                {
                    var (id, _) = RowAt(s, 2);
                    s.Mcp("tag_row", new { id, tag = "Note", text = "keep an eye on this" }).Dispose();
                    Assert.AreEqual(1, TagCount(s, "Note"));

                    // A different log in the same session: its own (empty) set of tags. Before tags were
                    // stored this was a live bug - the session map is keyed by RowId, so the old tags
                    // stayed put and landed on whichever rows of the new log inherited those ids.
                    OpenAnotherLog(s, other);
                    Assert.AreEqual(0, TagCount(s, "Note"), "another log does not inherit these tags");

                    // Back to the tagged one: the tag is there again.
                    OpenAnotherLog(s, tagged);
                    Assert.AreEqual(1, TagCount(s, "Note"), "reopening the tagged log brings its tag back");
                }
            }
            finally
            {
                TryDelete(tagged, other, settings);
                TryDeleteDir(settings + ".row-tags");
            }
        }

        private static void TryDelete(params string[] paths)
        {
            foreach (var p in paths) try { if (p != null && File.Exists(p)) File.Delete(p); } catch { }
        }

        private static void TryDeleteDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }
}
