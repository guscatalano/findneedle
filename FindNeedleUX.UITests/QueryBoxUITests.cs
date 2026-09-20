using Microsoft.VisualStudio.TestTools.UnitTesting;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace FindNeedleUX.UITests
{
    /// <summary>
    /// The search box as a power user drives it: completion while typing a query, the inline parse
    /// error, the "Around ▾" neighbourhood pivot from a row, and Ctrl+G go-to-time. The typing tests
    /// use the real keyboard (they run in CI's own desktop); the Around test only invokes patterns.
    /// </summary>
    [TestClass]
    [TestCategory("UITests")]
    [TestCategory("SkipCI")]
    [TestCategory("UiSmoke")] // self-contained (generated data, small): runs in the CI ui-smoke job
    public class QueryBoxUITests
    {
        private sealed class Session : IDisposable
        {
            public UIA3Automation Automation; public Application App; public AutomationElement Window; public string SettingsPath;
            public void Dispose()
            {
                try { App?.Close(); } catch { }
                try { if (App != null && !App.HasExited) App.Kill(); } catch { }
                try { App?.Dispose(); } catch { }
                try { Automation?.Dispose(); } catch { }
                try { if (SettingsPath != null && File.Exists(SettingsPath)) File.Delete(SettingsPath); } catch { }
                Thread.Sleep(800);
            }
        }

        private static Session Launch(string args)
        {
            var s = new Session { Automation = new UIA3Automation() };
            var psi = UiTestHelpers.IsolatedLaunch(UiTestHelpers.GetAppExecutablePath(), args, out s.SettingsPath);
            s.App = Application.Launch(psi);
            Thread.Sleep(3000);
            s.Window = s.App.GetMainWindow(s.Automation);
            try { s.Window?.Patterns.Window.PatternOrDefault?.SetWindowVisualState(WindowVisualState.Maximized); } catch { }
            Assert.IsNotNull(s.Window, "the app window did not come up.");
            return s;
        }

        /// <summary>A log whose rows are one second apart from 2026-01-01 00:00:00, so times are predictable.</summary>
        private static string WriteLog(int lines)
        {
            var path = Path.Combine(Path.GetTempPath(), $"fn_query_{Guid.NewGuid():N}.log");
            var start = new DateTime(2026, 1, 1, 0, 0, 0);
            using var sw = new StreamWriter(path, false, Encoding.ASCII);
            for (int i = 0; i < lines; i++)
            {
                sw.Write('['); sw.Write(start.AddSeconds(i).ToString("yyyy-MM-dd HH:mm:ss")); sw.Write("] ");
                sw.Write(i % 7 == 0 ? "ERROR: failure number " : "INFO: routine message "); sw.Write(i); sw.Write('\n');
            }
            return path;
        }

        private static bool WaitUntil(Func<bool> cond, int timeoutMs)
        {
            var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < deadline) { if (cond()) return true; Thread.Sleep(250); }
            return cond();
        }

        private static long PagerTotal(AutomationElement window)
        {
            var (_, _, raw) = UiTestHelpers.ReadPager(window);
            var ms = System.Text.RegularExpressions.Regex.Matches(raw ?? "", @"of\s+([\d,]+)");
            return ms.Count > 0 ? long.Parse(ms[ms.Count - 1].Groups[1].Value.Replace(",", "")) : -1;
        }

        private static AutomationElement SearchBox(AutomationElement window)
            => UiTestHelpers.FindByIdSkippingGrid(window, "SearchBox", 10000);

        private static string SearchText(AutomationElement window)
        {
            // The AutoSuggestBox's inner TextBox carries the value.
            var box = SearchBox(window);
            var inner = box?.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit)) ?? box;
            try { return inner?.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault ?? ""; } catch { return ""; }
        }

        private static void FocusSearchBox(AutomationElement window)
        {
            var box = SearchBox(window);
            Assert.IsNotNull(box, "SearchBox not found");
            var inner = box.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit)) ?? box;
            inner.Focus();
            Thread.Sleep(300);
        }

        private static void Invoke(AutomationElement e)
        {
            Assert.IsNotNull(e, "element to invoke was not found");
            var inv = e.Patterns.Invoke.PatternOrDefault;
            if (inv != null) inv.Invoke(); else e.Click();
        }

        [TestMethod]
        [Timeout(180000)]
        public void Typing_AField_OffersCompletions_AndABadQuery_ShowsTheErrorBeforeEnter()
        {
            var log = WriteLog(120);
            try
            {
                using var s = Launch($"\"{log}\" --viewer=native");
                Assert.IsNotNull(UiTestHelpers.WaitForPopulatedGrid(s.Window, 45000), "the log did not load.");

                FocusSearchBox(s.Window);
                Keyboard.Type("lev");
                // The suggestion list is a popup: look for it from the desktop.
                var desktop = s.Automation.GetDesktop();
                Assert.IsTrue(WaitUntil(() => desktop.FindFirstDescendant(cf => cf.ByControlType(ControlType.ListItem).And(cf.ByName("level", PropertyConditionFlags.MatchSubstring))) != null, 8000),
                    "typing 'lev' should offer the 'level' field.");

                // Arrow onto the first completion and accept it with Enter (Enter with nothing highlighted
                // is "search for this text"), then the operators are offered.
                Keyboard.Type(VirtualKeyShort.DOWN);
                Thread.Sleep(200);
                Keyboard.Type(VirtualKeyShort.RETURN);
                Assert.IsTrue(WaitUntil(() => SearchText(s.Window).StartsWith("level "), 5000), $"Enter should complete the field (text: '{SearchText(s.Window)}').");
                Assert.IsTrue(WaitUntil(() => desktop.FindFirstDescendant(cf => cf.ByControlType(ControlType.ListItem).And(cf.ByName("regex", PropertyConditionFlags.MatchSubstring))) != null, 8000),
                    "after the field the operators (incl. =~ regex) should be offered.");

                // A malformed query is flagged while typing, before Enter.
                Keyboard.Type("== ");
                Keyboard.Type("(oops");
                var err = UiTestHelpers.FindByIdSkippingGrid(s.Window, "SearchQueryErrorText", 5000);
                Assert.IsTrue(WaitUntil(() => err != null && !err.IsOffscreen && !string.IsNullOrEmpty(UiTestHelpers.SafeName(err)), 5000),
                    "a malformed query should show the inline error without pressing Enter.");
            }
            finally { try { File.Delete(log); } catch { } }
        }

        [TestMethod]
        [Timeout(180000)]
        public void Around_FromARow_ShowsTheNeighbourhood_InTimeOrder()
        {
            var log = WriteLog(200); // one row per second
            try
            {
                using var s = Launch($"\"{log}\" --viewer=native");
                var grid = UiTestHelpers.WaitForPopulatedGrid(s.Window, 45000);
                Assert.IsNotNull(grid, "the log did not load.");

                // Select the 50th row (time 00:00:49) and ask for ±10 s around it.
                var rows = grid.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem));
                Assert.IsTrue(rows.Length > 50, "expected at least 50 realized rows on page 1");
                rows[50].Patterns.SelectionItem.Pattern.Select();
                try { rows[50].Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView(); } catch { }
                AutomationElement around = null;
                Assert.IsTrue(WaitUntil(() => (around = grid.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Around ▾")))) != null, 10000),
                    "the in-row action bar is missing 'Around ▾'.");
                Invoke(around);

                var desktop = s.Automation.GetDesktop();
                AutomationElement item = null;
                Assert.IsTrue(WaitUntil(() => (item = desktop.FindFirstDescendant(cf => cf.ByName("±10 seconds"))) != null, 8000), "the Around menu did not open.");
                Invoke(item);

                // 21 rows (±10 s inclusive), the query in the box, and the row still selected.
                Assert.IsTrue(WaitUntil(() => PagerTotal(s.Window) == 21, 20000), $"±10 s around a 1-row-per-second log should show 21 rows (pager: {PagerTotal(s.Window)}).");
                StringAssert.Contains(SearchText(s.Window), "time ~", "the search box carries the time-window query");
                StringAssert.Contains(SearchText(s.Window), "±10s");
                var selected = grid.FindFirstDescendant(cf => cf.ByControlType(ControlType.DataItem).And(cf.ByName("row"))); // any realized row
                Assert.IsNotNull(selected, "rows should be realized after the pivot");
            }
            finally { try { File.Delete(log); } catch { } }
        }

        [TestMethod]
        [Timeout(180000)]
        public void CtrlG_GoToTime_LandsOnTheRowAtThatTime()
        {
            var log = WriteLog(300); // 00:00:00 .. 00:04:59, one per second, default page size 100
            try
            {
                using var s = Launch($"\"{log}\" --viewer=native");
                var grid = UiTestHelpers.WaitForPopulatedGrid(s.Window, 45000);
                Assert.IsNotNull(grid, "the log did not load.");
                grid.Focus();
                Thread.Sleep(300);

                Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_G);
                var desktop = s.Automation.GetDesktop();
                AutomationElement box = null;
                Assert.IsTrue(WaitUntil(() => (box = desktop.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit).And(cf.ByName("12:34:56", PropertyConditionFlags.MatchSubstring)))) != null
                                             || (box = FindGoToBox(desktop)) != null, 8000),
                    "Ctrl+G should open the Go to time box.");
                Keyboard.Type("00:03:20");
                Keyboard.Type(VirtualKeyShort.RETURN);

                // Row 200 (00:03:20) is on page 3 with a page size of 100; the grid pages there and selects it.
                Assert.IsTrue(WaitUntil(() =>
                {
                    var (start, _, _) = UiTestHelpers.ReadPager(s.Window);
                    return start == 201;
                }, 20000), $"go to 00:03:20 should page to rows 201–300 (pager: {UiTestHelpers.ReadPager(s.Window).raw}).");
                var sel = grid.Patterns.Selection.PatternOrDefault?.Selection.ValueOrDefault;
                Assert.IsTrue(sel != null && sel.Length == 1, "one row should be selected after go-to-time");
                StringAssert.Contains(sel[0].Name + " " + string.Join(" ", sel[0].FindAllChildren().Select(c => UiTestHelpers.SafeName(c))), "00:03:20",
                    "the selected row should be the one at 00:03:20");
            }
            finally { try { File.Delete(log); } catch { } }
        }

        [TestMethod]
        [Timeout(180000)]
        public void Pivots_AddToTheQuery_AndReplaceTheSameAxis()
        {
            var log = WriteLog(100);
            try
            {
                using var s = Launch($"\"{log}\" --viewer=native");
                var grid = UiTestHelpers.WaitForPopulatedGrid(s.Window, 45000);
                Assert.IsNotNull(grid, "the log did not load.");
                var desktop = s.Automation.GetDesktop();

                // Follow this provider (the only axis a text log has).
                var row = grid.FindFirstChild(cf => cf.ByControlType(ControlType.DataItem));
                row.Patterns.SelectionItem.Pattern.Select();
                AutomationElement follow = null;
                Assert.IsTrue(WaitUntil(() => (follow = grid.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Follow ▾")))) != null, 10000), "no Follow ▾");
                Invoke(follow);
                AutomationElement item = null;
                Assert.IsTrue(WaitUntil(() => (item = desktop.FindFirstDescendant(cf => cf.ByName("Follow this provider", PropertyConditionFlags.MatchSubstring))) != null, 8000), "no provider axis");
                Invoke(item);
                Assert.IsTrue(WaitUntil(() => SearchText(s.Window).StartsWith("provider == "), 10000), $"Follow should write a provider clause (text: '{SearchText(s.Window)}').");
                Assert.IsTrue(WaitUntil(() => PagerTotal(s.Window) == 100, 10000));

                // Then Filter in on Level: the provider clause stays, the level clause is added.
                row = grid.FindFirstChild(cf => cf.ByControlType(ControlType.DataItem));
                row.Patterns.SelectionItem.Pattern.Select();
                AutomationElement filterIn = null;
                Assert.IsTrue(WaitUntil(() => (filterIn = grid.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Filter in ▾")))) != null, 10000), "no Filter in ▾");
                Invoke(filterIn);
                Assert.IsTrue(WaitUntil(() => (item = desktop.FindFirstDescendant(cf => cf.ByName("Level ==", PropertyConditionFlags.MatchSubstring))) != null, 8000), "no Level item");
                Invoke(item);
                Assert.IsTrue(WaitUntil(() => SearchText(s.Window).Contains(" AND level == "), 10000), $"Filter in should ADD to the query (text: '{SearchText(s.Window)}').");
                StringAssert.StartsWith(SearchText(s.Window), "provider == ", "the earlier Follow clause is kept");

                // Filter in on Level again (same value): nothing is duplicated.
                var before = SearchText(s.Window);
                row = grid.FindFirstChild(cf => cf.ByControlType(ControlType.DataItem));
                row.Patterns.SelectionItem.Pattern.Select();
                Assert.IsTrue(WaitUntil(() => (filterIn = grid.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Filter in ▾")))) != null, 10000));
                Invoke(filterIn);
                Assert.IsTrue(WaitUntil(() => (item = desktop.FindFirstDescendant(cf => cf.ByName("Level ==", PropertyConditionFlags.MatchSubstring))) != null, 8000));
                Invoke(item);
                Thread.Sleep(1500);
                Assert.AreEqual(before, SearchText(s.Window), "the same clause is not added twice");
            }
            finally { try { File.Delete(log); } catch { } }
        }

        /// <summary>
        /// The row actions are reachable from the keyboard: with the grid focused, I opens Filter in,
        /// O opens Filter out, F opens Follow and T opens Tag - the same menus the right-click offers.
        /// Uses the real keyboard, so it runs on CI's own desktop.
        /// </summary>
        [TestMethod]
        [Timeout(180000)]
        public void RowActionKeys_OpenTheSameMenusAsTheRightClick()
        {
            var log = WriteLog(60);
            try
            {
                using var s = Launch($"\"{log}\" --viewer=native");
                var grid = UiTestHelpers.WaitForPopulatedGrid(s.Window, 45000);
                Assert.IsNotNull(grid, "the log did not load.");
                var desktop = s.Automation.GetDesktop();

                // Select a row and put the focus on the grid (SetFocus, not a synthetic click).
                var row = grid.FindFirstChild(cf => cf.ByControlType(ControlType.DataItem));
                Assert.IsNotNull(row, "no rows");
                row.Patterns.SelectionItem.Pattern.Select();
                grid.Focus();
                Thread.Sleep(500);

                // I → the Filter in menu, listing the row's fields as predicates.
                Keyboard.Type(VirtualKeyShort.KEY_I);
                Assert.IsTrue(WaitUntil(() => desktop.FindFirstDescendant(cf => cf.ByName("Level ==", PropertyConditionFlags.MatchSubstring)) != null, 8000),
                    "I should open Filter in for the selected row.");
                Keyboard.Type(VirtualKeyShort.ESCAPE);
                Thread.Sleep(500);

                // O → the same fields, negated.
                Keyboard.Type(VirtualKeyShort.KEY_O);
                Assert.IsTrue(WaitUntil(() => desktop.FindFirstDescendant(cf => cf.ByName("Level !=", PropertyConditionFlags.MatchSubstring)) != null, 8000),
                    "O should open Filter out for the selected row.");
                Keyboard.Type(VirtualKeyShort.ESCAPE);
                Thread.Sleep(500);

                // T → the tag categories.
                Keyboard.Type(VirtualKeyShort.KEY_T);
                Assert.IsTrue(WaitUntil(() => desktop.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName("Important"))) != null, 8000),
                    "T should open the Tag menu for the selected row.");
                Keyboard.Type(VirtualKeyShort.ESCAPE);
                Thread.Sleep(500);

                // And a letter in the search box is still just a letter.
                FocusSearchBox(s.Window);
                Keyboard.Type("i");
                Thread.Sleep(800);
                Assert.AreEqual("i", SearchText(s.Window), "letters typed in the search box are text, not row actions");
                Assert.IsNull(desktop.FindFirstDescendant(cf => cf.ByName("Level ==", PropertyConditionFlags.MatchSubstring)),
                    "no row-action menu should have opened from the search box");
            }
            finally { try { File.Delete(log); } catch { } }
        }

        private static AutomationElement FindGoToBox(AutomationElement desktop)
        {
            // The flyout's TextBox has a long placeholder; the label above it reads "Go to time".
            var label = desktop.FindFirstDescendant(cf => cf.ByName("Go to time"));
            return label?.Parent?.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit));
        }
    }
}
