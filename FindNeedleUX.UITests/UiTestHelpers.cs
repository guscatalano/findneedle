using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace FindNeedleUX.UITests
{
    /// <summary>
    /// Shared FlaUI helpers for the native-viewer UI tests. The key idea throughout is to NEVER let
    /// UI Automation walk into the ResultsGrid subtree: a DataGrid bound to thousands of rows exposes
    /// thousands of automation peers, and any window-wide FindAll (or a Grid-pattern cell fetch) times
    /// out or hangs the UI thread. So we walk children-only and prune the grid node.
    /// </summary>
    internal static class UiTestHelpers
    {
        public static string GetAppExecutablePath()
        {
            var testDir = AppContext.BaseDirectory;
            var solutionDir = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", ".."));
            string[] candidates =
            {
                Path.Combine(solutionDir, "FindNeedleUX", "bin", "Debug", "net8.0-windows10.0.19041.0", "win-x64", "FindNeedleUX.exe"),
                Path.Combine(solutionDir, "FindNeedleUX", "bin", "Release", "net8.0-windows10.0.19041.0", "win-x64", "FindNeedleUX.exe"),
                Path.Combine(solutionDir, "FindNeedleUX", "bin", "Debug", "net8.0-windows10.0.19041.0", "FindNeedleUX.exe"),
                Path.Combine(solutionDir, "FindNeedleUX", "bin", "Release", "net8.0-windows10.0.19041.0", "FindNeedleUX.exe"),
            };
            string newest = null; DateTime newestTime = DateTime.MinValue;
            foreach (var p in candidates)
                if (File.Exists(p) && File.GetLastWriteTime(p) > newestTime) { newestTime = File.GetLastWriteTime(p); newest = p; }
            if (newest != null) return newest;
            throw new FileNotFoundException($"Could not find FindNeedleUX.exe. Searched: {string.Join(", ", candidates)}");
        }

        /// <summary>Write a log in the "[yyyy-MM-dd HH:mm:ss] LEVEL: msg" format the plain-text plugin parses.</summary>
        public static string WriteBracketedLog(int lines, string prefix = "findneedle_uitest")
        {
            var path = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}.log");
            var start = new DateTime(2026, 1, 1, 0, 0, 0);
            using var sw = new StreamWriter(path, append: false, Encoding.ASCII, 1 << 20);
            for (int i = 0; i < lines; i++)
            {
                sw.Write('['); sw.Write(start.AddSeconds(i).ToString("yyyy-MM-dd HH:mm:ss"));
                sw.Write("] INFO: scroll test message line number "); sw.Write(i); sw.Write('\n');
            }
            return path;
        }

        public static string SafeName(AutomationElement e)
        { try { return e.Properties.Name.ValueOrDefault ?? ""; } catch { return ""; } }

        public static string SafeAutomationId(AutomationElement e)
        { try { return e.Properties.AutomationId.ValueOrDefault ?? ""; } catch { return ""; } }

        public static ControlType SafeControlType(AutomationElement e)
        { try { return e.Properties.ControlType.ValueOrDefault; } catch { return ControlType.Unknown; } }

        /// <summary>Visit every element via children-only traversal, pruning the ResultsGrid node.</summary>
        public static void WalkSkippingGrid(AutomationElement root, Func<AutomationElement, bool> visit)
        {
            var queue = new Queue<AutomationElement>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                AutomationElement[] children;
                try { children = node.FindAllChildren(); } catch { continue; }
                foreach (var child in children)
                {
                    if (visit(child)) return;
                    if (SafeAutomationId(child) == "ResultsGrid") continue;
                    queue.Enqueue(child);
                }
            }
        }

        public static AutomationElement FindByIdSkippingGrid(AutomationElement root, string automationId, int timeoutMs = 15000)
        {
            var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < deadline)
            {
                AutomationElement found = null;
                WalkSkippingGrid(root, e => { if (SafeAutomationId(e) == automationId) { found = e; return true; } return false; });
                if (found != null) return found;
                Thread.Sleep(300);
            }
            return null;
        }

        public static List<AutomationElement> FindAllSkippingGrid(AutomationElement root, ControlType type)
        {
            var results = new List<AutomationElement>();
            WalkSkippingGrid(root, e => { if (SafeControlType(e) == type) results.Add(e); return false; });
            return results;
        }

        public static AutomationElement WaitForPopulatedGrid(AutomationElement window, int timeoutMs)
        {
            var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            AutomationElement grid = null;
            while (DateTime.Now < deadline)
            {
                grid = FindByIdSkippingGrid(window, "ResultsGrid", 2000);
                if (grid?.FindFirstDescendant(cf => cf.ByControlType(ControlType.DataItem)) != null) return grid;
                Thread.Sleep(1000);
            }
            return grid;
        }

        public static (long start, long end, string raw) ReadPager(AutomationElement window)
        {
            var raw = FindAllSkippingGrid(window, ControlType.Text)
                         .Select(SafeName)
                         .FirstOrDefault(n => n.Contains("Page ") && n.Contains(" of ")) ?? "";
            var m = Regex.Match(raw, @"([\d,]+)\s*[–-]\s*([\d,]+)");
            long s = -1, e = -1;
            if (m.Success)
            {
                long.TryParse(m.Groups[1].Value.Replace(",", ""), out s);
                long.TryParse(m.Groups[2].Value.Replace(",", ""), out e);
            }
            return (s, e, raw);
        }

        public static bool ClickPagerButton(AutomationElement window, string text)
        {
            var btn = FindAllSkippingGrid(window, ControlType.Button)
                         .FirstOrDefault(b => SafeName(b).IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
            if (btn == null) return false;
            btn.Click();
            return true;
        }
    }
}
