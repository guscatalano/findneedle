using Microsoft.VisualStudio.TestTools.UnitTesting;
using FlaUI.Core;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using System;
using System.IO;
using System.Text;
using System.Threading;

namespace FindNeedleUX.UITests
{
    /// <summary>
    /// End-to-end proof that a level-color THEME actually tints the result-grid rows. Rather than drive
    /// the flaky settings combo, this persists the theme to viewer-settings.json (the same store the app
    /// reads) and launches the app twice over a log of ERROR rows — once with theme "None" (no tint) and
    /// once with "Bold" (red-tinted errors) — then counts reddish pixels in a screenshot of the grid. If
    /// the theme reaches the rows, "Bold" has far more reddish pixels than "None". Deterministic (no menu
    /// automation), like RowFontSizeUITests.
    ///
    /// Manual lane (excluded from CI): needs an interactive desktop and the built .exe.
    /// </summary>
    [TestClass]
    [TestCategory("UITests")]
    [TestCategory("SkipCI")]
    public class ThemeApplyUITests
    {
        private static string _logPath;
        private static string _settingsPath;
        private static string _settingsBackup;

        public TestContext TestContext { get; set; }

        [ClassInitialize]
        public static void Setup(TestContext context)
        {
            _logPath = WriteErrorLog(400);
            _settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FindNeedle", "viewer-settings.json");
            try { if (File.Exists(_settingsPath)) _settingsBackup = File.ReadAllText(_settingsPath); }
            catch { _settingsBackup = null; }
        }

        [ClassCleanup]
        public static void TearDown()
        {
            try
            {
                if (_settingsBackup != null) File.WriteAllText(_settingsPath, _settingsBackup);
                else if (_settingsPath != null && File.Exists(_settingsPath)) File.Delete(_settingsPath);
            }
            catch { }
            try { if (_logPath != null && File.Exists(_logPath)) File.Delete(_logPath); } catch { }
        }

        /// <summary>Log of ERROR-level rows (the plain-text plugin parses "[ts] LEVEL: msg"). Error is
        /// tinted in every non-None preset, so it's the level that shows a theme change.</summary>
        private static string WriteErrorLog(int lines)
        {
            var path = Path.Combine(Path.GetTempPath(), $"findneedle_themetest_{Guid.NewGuid():N}.log");
            var start = new DateTime(2026, 1, 1, 0, 0, 0);
            using var sw = new StreamWriter(path, append: false, Encoding.ASCII, 1 << 20);
            for (int i = 0; i < lines; i++)
            {
                sw.Write('['); sw.Write(start.AddSeconds(i).ToString("yyyy-MM-dd HH:mm:ss"));
                sw.Write("] ERROR: theme test error line "); sw.Write(i); sw.Write('\n');
            }
            return path;
        }

        /// <summary>Persist the theme, launch over the ERROR log, wait for the grid, and count reddish
        /// pixels in a screenshot of the grid. -1 if the app/grid didn't come up.</summary>
        private int ReddishGridPixels(string theme)
        {
            File.WriteAllText(_settingsPath, $"{{\"ThemeName\":\"{theme}\"}}");

            UIA3Automation automation = null;
            Application app = null;
            try
            {
                automation = new UIA3Automation();
                app = Application.Launch(UiTestHelpers.GetAppExecutablePath(), $"\"{_logPath}\" --viewer=native");
                Thread.Sleep(3000);
                var window = app.GetMainWindow(automation);
                if (window == null) return -1;
                try { window.Patterns.Window.PatternOrDefault?.SetWindowVisualState(WindowVisualState.Maximized); } catch { }

                var grid = UiTestHelpers.WaitForPopulatedGrid(window, 45000);
                if (grid == null) return -1;
                Thread.Sleep(800); // let the first page + row tinting settle

                using var img = Capture.Element(grid);
                return CountReddish(img.Bitmap);
            }
            finally
            {
                try { app?.Close(); } catch { }
                try { if (app != null && !app.HasExited) app.Kill(); } catch { }
                try { app?.Dispose(); } catch { }
                try { automation?.Dispose(); } catch { }
                Thread.Sleep(800);
            }
        }

        /// <summary>Count pixels that read as a red tint (R clearly above G and B) — the signature of the
        /// Error-row tint. Skips step-2 for speed. Black/white text and gray gridlines are neutral (R≈G≈B)
        /// so they don't count.</summary>
        private static int CountReddish(System.Drawing.Bitmap bmp)
        {
            int count = 0;
            for (int y = 0; y < bmp.Height; y += 2)
                for (int x = 0; x < bmp.Width; x += 2)
                {
                    var p = bmp.GetPixel(x, y);
                    if (p.R - p.G > 18 && p.R - p.B > 18 && p.R > 70) count++;
                }
            return count;
        }

        [TestMethod]
        [Timeout(180000)]
        public void Theme_TintsErrorRows()
        {
            int none = ReddishGridPixels("None");
            Assert.IsTrue(none >= 0, "app/grid didn't come up for theme None");

            int bold = ReddishGridPixels("Bold");
            Assert.IsTrue(bold >= 0, "app/grid didn't come up for theme Bold");

            TestContext.WriteLine($"Reddish grid pixels: None={none}, Bold={bold}");

            // "Bold" red-tints every Error row; "None" leaves them untinted. If the theme actually reaches
            // the grid, Bold must have far more reddish pixels. A small margin guards against noise.
            Assert.IsTrue(bold > none + 500,
                $"Theme did not tint the Error rows (None={none} reddish px, Bold={bold}). " +
                "The level-color theme isn't being applied to the result grid.");
        }
    }
}
