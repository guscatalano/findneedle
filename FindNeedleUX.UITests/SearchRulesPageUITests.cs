using Microsoft.VisualStudio.TestTools.UnitTesting;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace FindNeedleUX.UITests
{
    /// <summary>
    /// UI Automation tests for SearchRulesPage using FlaUI
    /// These tests verify the actual UI behavior when interacting with the file picker and ListBox
    /// 
    /// Prerequisites:
    /// 1. The FindNeedleUX app must be built and available
    /// 2. Run tests in Release mode on Windows 10+
    /// 3. For manual UI testing: requires WinUI 3 runtime
    /// 4. These tests require an interactive desktop - skip in CI
    /// </summary>
    [TestClass]
    [TestCategory("UITests")]
    [TestCategory("SkipCI")]
    public class SearchRulesPageUITests
    {
        private static Application _app;
        private static Window _mainWindow;
        private static UIA3Automation _automation;
        private const string AppName = "FindNeedleUX";


        private static string GetAppExecutablePath()
        {
            // Get the solution directory by going up from the test output folder
            var testDir = AppContext.BaseDirectory;
            var solutionDir = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", ".."));
            
            // Try common build output locations (FindNeedleUX uses win-x64 RuntimeIdentifier)
            string[] possiblePaths = new[]
            {
                Path.Combine(solutionDir, "FindNeedleUX", "bin", "Debug", "net8.0-windows10.0.19041.0", "win-x64", "FindNeedleUX.exe"),
                Path.Combine(solutionDir, "FindNeedleUX", "bin", "Release", "net8.0-windows10.0.19041.0", "win-x64", "FindNeedleUX.exe"),
                Path.Combine(solutionDir, "FindNeedleUX", "bin", "Debug", "net8.0-windows10.0.19041.0", "FindNeedleUX.exe"),
                Path.Combine(solutionDir, "FindNeedleUX", "bin", "Release", "net8.0-windows10.0.19041.0", "FindNeedleUX.exe"),
            };

            // Find the most recently modified executable to ensure we use the latest build
            string newestPath = null;
            DateTime newestTime = DateTime.MinValue;
            
            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    var modTime = File.GetLastWriteTime(path);
                    if (modTime > newestTime)
                    {
                        newestTime = modTime;
                        newestPath = path;
                    }
                }
            }
            
            if (newestPath != null)
            {
                System.Diagnostics.Debug.WriteLine($"Selected executable: {newestPath} (modified: {newestTime})");
                return newestPath;
            }

            throw new FileNotFoundException($"Could not find FindNeedleUX.exe in expected locations. Searched: {string.Join(", ", possiblePaths)}");
        }


        [ClassInitialize]
        public static void Setup(TestContext context)
        {
            try
            {
                _automation = new UIA3Automation();
                string appPath = GetAppExecutablePath();
                
                System.Diagnostics.Debug.WriteLine($"Launching app from: {appPath}");
                
                // Launch the application
                _app = Application.Launch(appPath);
                
                // Wait for app to start and be ready
                Thread.Sleep(3000);
                
                // Get the main window
                _mainWindow = _app.GetMainWindow(_automation);
                Assert.IsNotNull(_mainWindow, "Failed to get main window");
                
                // Navigate to SearchRulesPage via menu: Configure -> Rules
                NavigateToSearchRulesPage();
            }
            catch (Exception ex)
            {
                Assert.Fail($"Failed to initialize application. Error: {ex.Message}");
            }
        }

        private static void NavigateToSearchRulesPage()
        {
            // Open the "Configure" top-level menu (the Rules page lives under it).
            var configureMenu = _mainWindow.FindFirstDescendant(cf => cf.ByName("Configure"));
            Assert.IsNotNull(configureMenu, "Configure menu should exist");
            configureMenu.Click();
            Thread.Sleep(500);

            // Click the "Rules" flyout item (x:Name="rules" → AutomationId "rules"; fall back to its text).
            var rulesMenuItem = _mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("rules"))
                ?? _mainWindow.FindFirstDescendant(cf => cf.ByName("Rules"));
            Assert.IsNotNull(rulesMenuItem, "Rules menu item should exist");
            rulesMenuItem.Click();
            Thread.Sleep(1000);

            // The Rules hub opens on its "Active" tab; SearchRulesPage lives under the "Rule files" tab.
            var filesTab = _mainWindow.FindFirstDescendant(cf => cf.ByName("Rule files"));
            Assert.IsNotNull(filesTab, "Rule files tab should exist");
            filesTab.Click();

            // Wait for the tab's frame to navigate to SearchRulesPage
            Thread.Sleep(1000);
        }


        [ClassCleanup]
        public static void TearDown()
        {
            try
            {
                _mainWindow?.Close();
                _app?.Dispose();
                _automation?.Dispose();
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        [TestInitialize]
        public void TestSetup()
        {
            // Verify app is still running
            if (_app?.HasExited ?? true)
            {
                Assert.Inconclusive("Application closed unexpectedly between tests");
            }
            Thread.Sleep(300);
        }

        /// <summary>
        /// Test that the Browse button exists and is clickable
        /// </summary>
        [TestMethod]
        public void BrowseButton_Exists_And_IsClickable()
        {
            // Arrange
            var browseButton = FindElementByName("BrowseButton");

            // Assert
            Assert.IsNotNull(browseButton, "BrowseButton should exist");
            if (browseButton != null)
            {
                Assert.IsTrue(browseButton.IsEnabled, "BrowseButton should be enabled");
                Assert.IsTrue(browseButton.IsOffscreen == false, "BrowseButton should be visible");
            }
        }

        /// <summary>
        /// Test that RuleFilesListBox exists
        /// </summary>
        [TestMethod]
        public void RuleFilesListBox_Exists()
        {
            // Arrange & Act
            var listBox = FindElementByName("RuleFilesListBox");

            // Assert
            Assert.IsNotNull(listBox, "RuleFilesListBox should exist");
        }

        /// <summary>
        /// Test that RuleSections ListView exists
        /// </summary>
        [TestMethod]
        public void RuleSectionsListView_Exists()
        {
            // Arrange & Act
            var listView = FindElementByName("RuleSectionsListView");

            // Assert
            Assert.IsNotNull(listView, "RuleSectionsListView should exist in the UI");
        }

        /// <summary>
        /// Test that Apply button exists and is visible
        /// NOTE: Ignored because SearchRulesPage does not implement Apply/Cancel buttons.
        /// The page is a view-only configuration browser without submission functionality.
        /// </summary>
        [TestMethod]
        [Ignore("SearchRulesPage does not have Apply button - page is view-only")]
        public void ApplyButton_Exists()
        {
            // Arrange & Act
            var applyButton = FindElementByName("ApplyButton");

            // Assert
            Assert.IsNotNull(applyButton, "ApplyButton should exist");
            if (applyButton != null)
            {
                Assert.IsTrue(applyButton.IsEnabled, "ApplyButton should be enabled");
            }
        }

        /// <summary>
        /// Test that Cancel button exists and is visible
        /// NOTE: Ignored because SearchRulesPage does not implement Apply/Cancel buttons.
        /// The page is a view-only configuration browser without submission functionality.
        /// </summary>
        [TestMethod]
        [Ignore("SearchRulesPage does not have Cancel button - page is view-only")]
        public void CancelButton_Exists()
        {
            // Arrange & Act - Cancel button doesn't have x:Name, find by Content text
            var cancelButton = _mainWindow?.FindFirstDescendant(cf => cf.ByName("Cancel"));

            // Assert
            Assert.IsNotNull(cancelButton, "Cancel button should exist");
            if (cancelButton != null)
            {
                Assert.IsTrue(cancelButton.IsEnabled, "Cancel button should be enabled");
            }
        }

        /// <summary>
        /// Test that page elements are properly initialized
        /// </summary>
        [TestMethod]
        public void SearchRulesPage_Elements_AreInitialized()
        {
            // Arrange & Act
            var browseButton = FindElementByName("BrowseButton");
            var ruleFilesListBox = FindElementByName("RuleFilesListBox");
            var ruleSectionsListView = FindElementByName("RuleSectionsListView");
            var removeButton = FindElementByName("RemoveButton");
            var purposeFilterCombo = FindElementByName("PurposeFilterCombo");

            // Assert - Test only elements that actually exist in SearchRulesPage.xaml
            Assert.IsNotNull(browseButton, "BrowseButton should be initialized");
            Assert.IsNotNull(ruleFilesListBox, "RuleFilesListBox should be initialized");
            Assert.IsNotNull(ruleSectionsListView, "RuleSectionsListView should be initialized");
            Assert.IsNotNull(removeButton, "RemoveButton should be initialized");
            Assert.IsNotNull(purposeFilterCombo, "PurposeFilterCombo should be initialized");
        }

        /// <summary>
        /// Helper method to wait for file dialog to appear
        /// </summary>
        private Window WaitForFileDialog(int timeoutMs)
        {
            var startTime = DateTime.Now;
            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                // Look for the file dialog window
                var desktop = _automation.GetDesktop();
                var dialogs = desktop.FindAllChildren(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Window));
                
                foreach (var dialog in dialogs)
                {
                    var title = dialog.Name ?? "";
                    // File picker dialogs typically have "Open" or similar in the title
                    if (title.Contains("Open") || title.Contains("Select") || title.Contains("Browse") || title.Contains("Pick"))
                    {
                        return dialog.AsWindow();
                    }
                }
                
                Thread.Sleep(200);
            }
            return null;
        }

        /// <summary>
        /// Helper method to find elements by automation ID (x:Name in XAML)
        /// </summary>
        private AutomationElement FindElementByName(string elementName)
        {
            try
            {
                if (_mainWindow == null)
                {
                    return null;
                }
                // In WinUI 3, x:Name is exposed as AutomationId, not Name
                return _mainWindow.FindFirstDescendant(cf => cf.ByAutomationId(elementName));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error finding element '{elementName}': {ex.Message}");
                return null;
            }
        }
    }
}
