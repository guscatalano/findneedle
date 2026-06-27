using Microsoft.VisualStudio.TestTools.UnitTesting;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;

namespace FindNeedleUX.UITests
{
    /// <summary>
    /// FlaUI smoke for the "open a .zip bundle" path the user works with — launches the real app at a
    /// multi-file zip and asserts the archive is extracted, its inner logs flow through the pipeline, and
    /// the result viewer populates and renders its filter row. This is the UI-thread end-to-end the
    /// headless FixtureFilterPerfTests can't see (it catches a hang/blank-viewer on a bundle load).
    ///
    /// Uses a small inline zip of plain-text logs (no ETW/admin) so it's deterministic and fast; the
    /// heavy WPP/TraceLogging + every-filter perf is covered headlessly in ETWPluginTests.
    ///
    /// Manual lane (UITests + SkipCI): needs an interactive desktop and the built .exe.
    /// </summary>
    [TestClass]
    [TestCategory("UITests")]
    [TestCategory("SkipCI")]
    public class ArchiveBundleLoadUITests
    {
        public TestContext TestContext { get; set; }

        private static (UIA3Automation automation, Application app, AutomationElement window) Launch(string pathArg)
        {
            var automation = new UIA3Automation();
            var app = Application.Launch(UiTestHelpers.GetAppExecutablePath(), $"\"{pathArg}\" --viewer=native");
            Thread.Sleep(3000);
            var window = app.GetMainWindow(automation);
            try { window?.Patterns.Window.PatternOrDefault?.SetWindowVisualState(WindowVisualState.Maximized); } catch { }
            return (automation, app, window);
        }

        private static void Shutdown(Application app, UIA3Automation automation)
        {
            try { app?.Close(); } catch { }
            // Close() may not terminate the process and Dispose() never does — Kill so it can't linger.
            try { if (app != null && !app.HasExited) app.Kill(); } catch { }
            try { app?.Dispose(); } catch { }
            try { automation?.Dispose(); } catch { }
            Thread.Sleep(800);
        }

        /// <summary>Build a zip of several plain-text logs (the "[time] LEVEL: msg" format the text plugin
        /// parses), so opening it exercises archive extraction → folder scan → viewer.</summary>
        private static string WriteBundleZip(int files, int linesPerFile)
        {
            var dir = Path.Combine(Path.GetTempPath(), $"fn_uizip_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            var start = new DateTime(2026, 1, 1, 0, 0, 0);
            for (int f = 0; f < files; f++)
            {
                var lf = Path.Combine(dir, $"bundle-{f}.log");
                using var sw = new StreamWriter(lf, append: false, Encoding.ASCII, 1 << 16);
                for (int i = 0; i < linesPerFile; i++)
                {
                    sw.Write('['); sw.Write(start.AddSeconds(i).ToString("yyyy-MM-dd HH:mm:ss"));
                    sw.Write(i % 4 == 0 ? "] ERROR: " : "] INFO: ");
                    sw.Write("bundle file "); sw.Write(f); sw.Write(" message line "); sw.Write(i); sw.Write('\n');
                }
            }
            var zip = Path.Combine(Path.GetTempPath(), $"fn_uibundle_{Guid.NewGuid():N}.zip");
            ZipFile.CreateFromDirectory(dir, zip);
            try { Directory.Delete(dir, true); } catch { }
            return zip;
        }

        [TestMethod]
        [Timeout(180000)]
        public void OpeningZipBundle_PopulatesViewer()
        {
            var zip = WriteBundleZip(files: 4, linesPerFile: 500); // ~2,000 rows across 4 inner logs
            UIA3Automation automation = null; Application app = null;
            try
            {
                (automation, app, var window) = Launch(zip);
                Assert.IsNotNull(window, "the app window did not come up for the zip bundle.");

                // The archive must extract, its inner .log files parse, and the grid fill — the
                // end-to-end "open a bundle" path. (Generous timeout: extraction + scan + first page.)
                var grid = UiTestHelpers.WaitForPopulatedGrid(window, 90000);
                Assert.IsNotNull(grid, "the results grid never appeared for the zip bundle.");
                Assert.IsNotNull(grid.FindFirstDescendant(cf => cf.ByControlType(ControlType.DataItem)),
                    "the zip bundle loaded no rows into the viewer.");

                // And the viewer fully came up (filter row present), not just a bare grid.
                Assert.IsNotNull(UiTestHelpers.FindByIdSkippingGrid(window, "ShowKnownToggle", 15000),
                    "the result viewer filter row did not render after loading the bundle.");
            }
            finally { Shutdown(app, automation); try { File.Delete(zip); } catch { } }
        }
    }
}
