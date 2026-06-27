using Microsoft.VisualStudio.TestTools.UnitTesting;
using FlaUI.Core;
using FlaUI.UIA3;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace FindNeedleUX.UITests
{
    /// <summary>
    /// Shutdown health: a graceful window close must terminate the process quickly. If it doesn't,
    /// something (a non-background thread, an undisposed ETW/SQLite/MCP resource, a background index
    /// build that doesn't cancel) is keeping the app alive after the window is gone — a real bug a user
    /// would see as the app lingering in Task Manager. So we close, wait, and ASSERT a fast exit rather
    /// than force-killing and hiding the regression.
    /// </summary>
    [TestClass]
    [TestCategory("UITests")]
    [TestCategory("SkipCI")]
    public class AppShutdownUITests
    {
        // Graceful exit was ~1s in practice; budget is generous headroom but still catches a true hang.
        private const int ShutdownBudgetMs = 8000;

        // Big enough that load picks SQLite (> 50k) and the background index may still be in flight when
        // we close — so this also proves closing doesn't wait on / get blocked by background work.
        private const int RowCount = 500_000;

        public TestContext TestContext { get; set; }

        [TestMethod]
        [Timeout(120_000)]
        public void App_ClosesGracefully_WithinBudget()
        {
            var logPath = UiTestHelpers.WriteBracketedLog(RowCount, "findneedle_shutdown");
            var psi = new ProcessStartInfo(UiTestHelpers.GetAppExecutablePath())
            {
                Arguments = $"\"{logPath}\" --viewer=native",
                UseShellExecute = false,
            };

            Process proc = null;
            UIA3Automation automation = null;
            try
            {
                proc = Process.Start(psi);
                Assert.IsNotNull(proc, "failed to start the app process");

                // Wait until the window + grid are up, so we're closing a fully-loaded app.
                automation = new UIA3Automation();
                var app = Application.Attach(proc.Id);
                var window = app.GetMainWindow(automation);
                Assert.IsNotNull(window, "main window never appeared");
                var grid = UiTestHelpers.WaitForPopulatedGrid(window, 60_000);
                Assert.IsNotNull(grid, "grid never populated — load didn't complete");
                Thread.Sleep(1000); // let the first frame settle

                // Graceful close (WM_CLOSE), then time how long the process takes to actually exit.
                proc.Refresh();
                Assert.IsTrue(proc.CloseMainWindow(), "could not post the close message to the main window");

                var sw = Stopwatch.StartNew();
                bool exited = proc.WaitForExit(ShutdownBudgetMs);
                sw.Stop();
                TestContext.WriteLine($"graceful close → process exit in {sw.ElapsedMilliseconds} ms");

                Assert.IsTrue(exited,
                    $"app did not exit within {ShutdownBudgetMs} ms of a graceful close ({sw.ElapsedMilliseconds} ms and counting) — " +
                    "shutdown is hung. Something is keeping the process alive after the window closed " +
                    "(a non-background thread, an undisposed ETW/SQLite/MCP resource, or a background index build that doesn't cancel).");
            }
            finally
            {
                try { automation?.Dispose(); } catch { }
                try { if (proc != null && !proc.HasExited) proc.Kill(); } catch { }
                try { proc?.Dispose(); } catch { }
                try { if (File.Exists(logPath)) File.Delete(logPath); } catch { }
            }
        }
    }
}
