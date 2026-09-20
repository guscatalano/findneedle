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
    /// Workspace flows on the real app: load a file, add a second, clear, reload; apply a rule file; save
    /// and reopen a workspace; reopen a recent search from Home; inspect the loaded sources. The state
    /// changes are driven through the app's own MCP server (the same surface an AI agent uses - it is a
    /// product feature, not a test hook) and every assertion is made against what the WINDOW shows:
    /// the workspace chip, the breadcrumb, the pager, the grid. Each test runs an isolated instance on
    /// its own MCP port.
    /// </summary>
    [TestClass]
    [TestCategory("UITests")]
    [TestCategory("SkipCI")]
    [TestCategory("UiSmoke")] // self-contained (generated data, small): runs in the CI ui-smoke job
    public class WorkspaceFlowUITests
    {
        public TestContext TestContext { get; set; }

        private static int _nextPort = 8821;

        private sealed class Session : IDisposable
        {
            public UIA3Automation Automation;
            public Application App;
            public AutomationElement Window;
            public string SettingsPath;
            public int Port;
            public readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

            /// <summary>Call one MCP tool and return its text payload (the JSON the tool produced).</summary>
            public string Mcp(string tool, object args = null)
            {
                var body = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = tool, arguments = args ?? new { } } });
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
                        return result.GetProperty("content")[0].GetProperty("text").GetString();
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
                try { if (SettingsPath != null && File.Exists(SettingsPath)) File.Delete(SettingsPath); } catch { }
                Thread.Sleep(800);
            }
        }

        /// <summary>Launch an isolated instance with the MCP server on a fresh port. <paramref name="args"/> is the
        /// raw command line ("" = Home).</summary>
        private static Session Launch(string args)
        {
            var s = new Session { Automation = new UIA3Automation(), Port = Interlocked.Increment(ref _nextPort) };
            var psi = UiTestHelpers.IsolatedLaunch(UiTestHelpers.GetAppExecutablePath(), args, out s.SettingsPath);
            File.WriteAllText(s.SettingsPath, $"{{\"McpServerEnabled\":true,\"McpServerPort\":{s.Port}}}");
            s.App = Application.Launch(psi);
            Thread.Sleep(3000);
            s.Window = s.App.GetMainWindow(s.Automation);
            try { s.Window?.Patterns.Window.PatternOrDefault?.SetWindowVisualState(WindowVisualState.Maximized); } catch { }
            Assert.IsNotNull(s.Window, "the app window did not come up.");
            return s;
        }

        private static string TextOf(AutomationElement e)
        {
            if (e == null) return "";
            try { return e.Properties.Name.ValueOrDefault ?? ""; } catch { return ""; }
        }

        private static string Chip(AutomationElement window)
            => TextOf(UiTestHelpers.FindByIdSkippingGrid(window, "WorkspaceChipSummary", 5000));

        private static string ChipName(AutomationElement window)
            => TextOf(UiTestHelpers.FindByIdSkippingGrid(window, "WorkspaceChipName", 5000));

        private static string Breadcrumb(AutomationElement window)
            => TextOf(UiTestHelpers.FindByIdSkippingGrid(window, "BreadcrumbTitle", 5000));

        private static bool WaitUntil(Func<bool> cond, int timeoutMs)
        {
            var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < deadline) { if (cond()) return true; Thread.Sleep(300); }
            return cond();
        }

        private static AutomationElement FindByName(AutomationElement root, string name, int timeoutMs, ControlType? type = null, bool exact = false)
        {
            var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            do
            {
                AutomationElement found = null;
                UiTestHelpers.WalkSkippingGrid(root, e =>
                {
                    if (type.HasValue && UiTestHelpers.SafeControlType(e) != type.Value) return false;
                    var n = UiTestHelpers.SafeName(e);
                    bool hit = exact ? string.Equals(n, name, StringComparison.OrdinalIgnoreCase) : n.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (hit) { found = e; return true; }
                    return false;
                });
                if (found != null) return found;
                Thread.Sleep(300);
            } while (DateTime.Now < deadline);
            return null;
        }

        private static void Invoke(AutomationElement e)
        {
            Assert.IsNotNull(e, "element to invoke was not found");
            var inv = e.Patterns.Invoke.PatternOrDefault;
            if (inv != null) inv.Invoke(); else e.Click();
        }

        private static void Toggle(AutomationElement e)
        {
            var t = e.Patterns.Toggle.PatternOrDefault;
            if (t != null) t.Toggle(); else Invoke(e);
        }

        /// <summary>The pager's total ("1–100 of 300" → 300), or -1 when the pager isn't up.</summary>
        private static long PagerTotal(AutomationElement window)
        {
            try
            {
                var (_, _, raw) = UiTestHelpers.ReadPager(window);
                // "Page 1 of 2 · 1–100 of 150": the row total is the LAST "of N".
                var ms = System.Text.RegularExpressions.Regex.Matches(raw ?? "", @"of\s+([\d,]+)");
                return ms.Count > 0 ? long.Parse(ms[ms.Count - 1].Groups[1].Value.Replace(",", "")) : -1;
            }
            catch { return -1; }
        }

        private static bool WaitForTotal(AutomationElement window, long expected, int timeoutMs)
            => WaitUntil(() => PagerTotal(window) == expected, timeoutMs);

        /// <summary>A log with <paramref name="lines"/> rows, every <paramref name="errorEvery"/>th an ERROR (0 = none).</summary>
        private static string WriteLog(string prefix, int lines, int errorEvery = 0)
        {
            var path = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}.log");
            var start = new DateTime(2026, 1, 1, 0, 0, 0);
            using var sw = new StreamWriter(path, false, Encoding.ASCII);
            for (int i = 0; i < lines; i++)
            {
                bool err = errorEvery > 0 && i % errorEvery == 0;
                sw.Write('['); sw.Write(start.AddSeconds(i).ToString("yyyy-MM-dd HH:mm:ss")); sw.Write("] ");
                sw.Write(err ? "ERROR: something failed on line " : "INFO: routine message line ");
                sw.Write(i); sw.Write('\n');
            }
            return path;
        }

        private static void TryDelete(params string[] paths)
        {
            foreach (var p in paths) try { if (p != null && File.Exists(p)) File.Delete(p); } catch { }
        }

        // ---- tests ----

        [TestMethod]
        [Timeout(240000)]
        public void LoadFile_AddSecond_ThenClear_ChipAndPagerFollow()
        {
            var a = WriteLog("fn_flow_a", 150);
            var b = WriteLog("fn_flow_b", 250);
            try
            {
                using var s = Launch($"\"{a}\" --viewer=native");
                Assert.IsNotNull(UiTestHelpers.WaitForPopulatedGrid(s.Window, 45000), "the first file did not load.");
                Assert.IsTrue(WaitForTotal(s.Window, 150, 30000), $"expected 150 rows from the first file (pager: {PagerTotal(s.Window)}).");
                StringAssert.Contains(Chip(s.Window), "1 source", "chip after the first file");

                // Add a second source and re-run: both files' rows, and the chip says two.
                s.Mcp("add_folder", new { path = b });
                Assert.IsTrue(WaitUntil(() => Chip(s.Window).Contains("2 sources"), 15000), $"chip after adding a second source: '{Chip(s.Window)}'");
                s.Mcp("run_search");
                s.Mcp("wait_for_load", new { timeoutMs = 60000 });
                Assert.IsTrue(WaitForTotal(s.Window, 400, 60000), $"expected 150 + 250 rows after adding the second file (pager: {PagerTotal(s.Window)}).");

                // Clear: nothing loaded, and the chip says so.
                s.Mcp("clear_workspace");
                Assert.IsTrue(WaitUntil(() => Chip(s.Window).Contains("0 sources"), 15000), $"chip after clearing: '{Chip(s.Window)}'");
                using var status = JsonDocument.Parse(s.Mcp("status"));
                Assert.AreEqual(0, status.RootElement.GetProperty("locations").GetInt32(), "clear_workspace should leave no locations.");

                // And loading again after a clear works (the workspace is genuinely fresh, not wedged).
                s.Mcp("add_folder", new { path = b });
                s.Mcp("run_search");
                s.Mcp("wait_for_load", new { timeoutMs = 60000 });
                Assert.IsTrue(WaitUntil(() => Chip(s.Window).Contains("1 source"), 15000), $"chip after reloading: '{Chip(s.Window)}'");
                Assert.IsTrue(WaitForTotal(s.Window, 250, 60000), $"expected 250 rows from the reloaded file (pager: {PagerTotal(s.Window)}).");
            }
            finally { TryDelete(a, b); }
        }

        [TestMethod]
        [Timeout(240000)]
        public void RuleFile_IncludeFilter_KeepsOnlyMatches_AndChipCountsIt()
        {
            var log = WriteLog("fn_rules", 200, errorEvery: 5); // 40 ERROR lines
            var rules = Path.Combine(Path.GetTempPath(), $"fn_rules_{Guid.NewGuid():N}.rules.json");
            File.WriteAllText(rules, @"{
  ""sections"": [
    { ""name"": ""ErrorsOnly"", ""purpose"": ""filter"", ""providers"": [ ""*"" ],
      ""rules"": [ { ""match"": ""ERROR"", ""action"": { ""type"": ""include"" } } ] }
  ]
}");
            try
            {
                // Open with rules from the command line - the same path as Home's "Open with rules…".
                using var s = Launch($"\"{log}\" --rules=\"{rules}\" --viewer=native");
                Assert.IsNotNull(UiTestHelpers.WaitForPopulatedGrid(s.Window, 45000), "the file did not load with rules.");
                Assert.IsTrue(WaitUntil(() => Chip(s.Window).Contains("1 rule file"), 15000), $"chip should count the rule file: '{Chip(s.Window)}'");
                StringAssert.Contains(s.Mcp("list_rules"), Path.GetFileName(rules), "list_rules should name the applied rule file.");

                // Loading keeps every row; a filter rule is applied on demand by the viewer's "Rule filter"
                // toggle (enabled only once a rule file is loaded), so the user can flip between all rows
                // and the rule's view without a rescan.
                Assert.IsTrue(WaitForTotal(s.Window, 200, 30000), $"all 200 rows load before the rule filter is switched on (pager: {PagerTotal(s.Window)}).");
                var toggle = UiTestHelpers.FindByIdSkippingGrid(s.Window, "RuleFilterToggle", 10000);
                Assert.IsNotNull(toggle, "the Rule filter toggle is missing.");
                Assert.IsTrue(WaitUntil(() => toggle.IsEnabled, 10000), "the Rule filter toggle should be enabled once a rule file is loaded.");
                Toggle(toggle);
                Assert.IsTrue(WaitForTotal(s.Window, 40, 30000),
                    $"with the rule filter on, an include rule on ERROR should keep 40 of 200 rows (pager: {PagerTotal(s.Window)}).");
                Toggle(toggle);
                Assert.IsTrue(WaitForTotal(s.Window, 200, 30000), $"switching the rule filter off should bring all 200 rows back (pager: {PagerTotal(s.Window)}).");

                // Dropping the rule file entirely: the chip goes back to none and the toggle is off limits.
                s.Mcp("set_rules", new { paths = new string[0] });
                s.Mcp("run_search");
                s.Mcp("wait_for_load", new { timeoutMs = 60000 });
                Assert.IsTrue(WaitUntil(() => Chip(s.Window).Contains("0 rule files"), 15000), $"chip after removing rules: '{Chip(s.Window)}'");
                Assert.IsTrue(WaitForTotal(s.Window, 200, 60000), $"without rules all 200 rows remain (pager: {PagerTotal(s.Window)}).");
            }
            finally { TryDelete(log, rules); }
        }

        [TestMethod]
        [Timeout(240000)]
        public void Workspace_SaveClearReopen_RestoresSourcesAndName()
        {
            var log = WriteLog("fn_ws", 120);
            var ws = Path.Combine(Path.GetTempPath(), $"fn_ws_{Guid.NewGuid():N}.json");
            var wsName = Path.GetFileNameWithoutExtension(ws);
            try
            {
                using var s = Launch($"\"{log}\" --viewer=native");
                Assert.IsNotNull(UiTestHelpers.WaitForPopulatedGrid(s.Window, 45000), "the file did not load.");
                StringAssert.Contains(Chip(s.Window), "1 source");

                s.Mcp("save_workspace", new { path = ws });
                Assert.IsTrue(File.Exists(ws), "save_workspace wrote no file.");
                Assert.IsTrue(WaitUntil(() => ChipName(s.Window).Equals(wsName, StringComparison.OrdinalIgnoreCase), 10000),
                    $"after saving, the chip should carry the workspace name (chip: '{ChipName(s.Window)}').");

                s.Mcp("clear_workspace");
                Assert.IsTrue(WaitUntil(() => Chip(s.Window).Contains("0 sources"), 15000), $"chip after clear: '{Chip(s.Window)}'");

                s.Mcp("open_workspace", new { path = ws });
                Assert.IsTrue(WaitUntil(() => Chip(s.Window).Contains("1 source"), 15000), $"chip after reopening the workspace: '{Chip(s.Window)}'");
                Assert.IsTrue(WaitUntil(() => ChipName(s.Window).Equals(wsName, StringComparison.OrdinalIgnoreCase), 10000),
                    $"the reopened workspace should be named after its file (chip: '{ChipName(s.Window)}').");
                s.Mcp("run_search");
                s.Mcp("wait_for_load", new { timeoutMs = 60000 });
                Assert.IsTrue(WaitForTotal(s.Window, 120, 60000), $"the reopened workspace should load its 120 rows (pager: {PagerTotal(s.Window)}).");
            }
            finally { TryDelete(log, ws); }
        }

        [TestMethod]
        [Timeout(240000)]
        public void Home_RecentSearch_ReopensFromCache_AfterClear()
        {
            var log = WriteLog("fn_recent", 130);
            var name = Path.GetFileName(log);
            try
            {
                // Recent searches are the on-disk (SQLite) caches; a 130-row log would stay in memory, so
                // force the SQLite tier the way a large log would get it.
                using var s = Launch($"\"{log}\" --storage=sqlite --viewer=native");
                Assert.IsNotNull(UiTestHelpers.WaitForPopulatedGrid(s.Window, 45000), "the file did not load.");
                s.Mcp("wait_for_load", new { timeoutMs = 60000 });

                // Forget it, go Home: the Recent searches card still offers it.
                s.Mcp("clear_workspace");
                Invoke(UiTestHelpers.FindByIdSkippingGrid(s.Window, "BreadcrumbHome", 5000));
                Assert.IsTrue(WaitUntil(() => Breadcrumb(s.Window) == "Home", 10000), "did not navigate Home.");
                var open = FindByName(s.Window, $"Open {name}", 15000, ControlType.Button, exact: true);
                Assert.IsNotNull(open, $"Home's Recent searches should list '{name}' with an Open button.");

                // Open reopens the cached result without a rescan and lands on Results.
                Invoke(open);
                Assert.IsTrue(WaitUntil(() => Breadcrumb(s.Window).Contains("Results"), 30000), $"Open from Recent did not open the viewer (breadcrumb: '{Breadcrumb(s.Window)}').");
                Assert.IsNotNull(UiTestHelpers.WaitForPopulatedGrid(s.Window, 45000), "the recent search opened an empty viewer.");
                Assert.IsTrue(WaitForTotal(s.Window, 130, 30000), $"the cached result should have its 130 rows (pager: {PagerTotal(s.Window)}).");
                Assert.IsTrue(WaitUntil(() => Chip(s.Window).Contains("1 source"), 15000), $"reopening from Recent should put the source back in the workspace: '{Chip(s.Window)}'");
                Assert.IsNotNull(FindByName(s.Window, "from cache", 10000, ControlType.Text),
                    "the status strip should say the last run came from the cache.");
            }
            finally { TryDelete(log); }
        }

        [TestMethod]
        [Timeout(180000)]
        public void SourcesButton_ListsTheLoadedFiles()
        {
            var a = WriteLog("fn_src_a", 60);
            var b = WriteLog("fn_src_b", 60);
            try
            {
                using var s = Launch($"\"{a}\" --viewer=native");
                Assert.IsNotNull(UiTestHelpers.WaitForPopulatedGrid(s.Window, 45000), "the file did not load.");
                s.Mcp("add_folder", new { path = b });
                s.Mcp("run_search");
                s.Mcp("wait_for_load", new { timeoutMs = 60000 });
                Assert.IsTrue(WaitForTotal(s.Window, 120, 60000), $"both files should be loaded (pager: {PagerTotal(s.Window)}).");

                // The viewer's Sources button opens the "what is loaded" dialog naming both files.
                Invoke(UiTestHelpers.FindByIdSkippingGrid(s.Window, "SourcesButton", 10000));
                Assert.IsNotNull(FindByName(s.Window, Path.GetFileName(a), 10000), $"the Sources dialog does not list '{Path.GetFileName(a)}'.");
                Assert.IsNotNull(FindByName(s.Window, Path.GetFileName(b), 5000), $"the Sources dialog does not list '{Path.GetFileName(b)}'.");

                // With two files loaded the dialog also breaks the rows down per file, so "which of
                // these is most of this search" is answerable without filtering.
                Assert.IsNotNull(FindByName(s.Window, "Rows by file", 10000), "the Sources dialog should break rows down per file.");
            }
            finally { TryDelete(a, b); }
        }

        /// <summary>
        /// Source is hidden by default (long paths, and with one log every row has the same answer),
        /// but the moment a second file is loaded the grid has to say which log a row came from - so
        /// the column shows itself. Asserted through the Columns popover's own checkbox.
        /// </summary>
        [TestMethod]
        [Timeout(240000)]
        public void ASecondSource_ShowsTheSourceColumn()
        {
            var a = WriteLog("fn_srccol_a", 40);
            var b = WriteLog("fn_srccol_b", 40);
            try
            {
                using var s = Launch($"\"{a}\" --viewer=native");
                Assert.IsNotNull(UiTestHelpers.WaitForPopulatedGrid(s.Window, 45000), "the file did not load.");
                Assert.AreEqual(ToggleState.Off, ColumnCheckState(s, "Source"), "one file: Source stays hidden");

                s.Mcp("add_folder", new { path = b });
                s.Mcp("run_search");
                s.Mcp("wait_for_load", new { timeoutMs = 60000 });
                Assert.IsTrue(WaitForTotal(s.Window, 80, 60000), $"both files should be loaded (pager: {PagerTotal(s.Window)}).");
                Assert.IsTrue(WaitUntil(() => ColumnCheckState(s, "Source") == ToggleState.On, 15000),
                    "two files: the Source column should show itself");
            }
            finally { TryDelete(a, b); }
        }

        /// <summary>Open Columns ▾ and read one column's checkbox (then close the popover again).</summary>
        private static ToggleState ColumnCheckState(Session s, string column)
        {
            Invoke(UiTestHelpers.FindByIdSkippingGrid(s.Window, "ColumnsButton", 10000));
            try
            {
                var pid = s.App.ProcessId;
                AutomationElement box = null;
                WaitUntil(() =>
                {
                    foreach (var top in s.Automation.GetDesktop().FindAllChildren(cf => cf.ByProcessId(pid)))
                    {
                        box = top.FindFirstDescendant(cf => cf.ByControlType(ControlType.CheckBox).And(cf.ByName(column)));
                        if (box != null) return true;
                    }
                    return false;
                }, 8000);
                Assert.IsNotNull(box, $"the Columns popover has no '{column}' checkbox.");
                return box.Patterns.Toggle.PatternOrDefault?.ToggleState.ValueOrDefault ?? ToggleState.Indeterminate;
            }
            finally
            {
                try { s.Window.Focus(); } catch { }
                try { Invoke(UiTestHelpers.FindByIdSkippingGrid(s.Window, "ColumnsButton", 3000)); } catch { }
            }
        }
    }
}
