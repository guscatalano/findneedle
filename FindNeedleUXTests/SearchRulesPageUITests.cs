using Microsoft.VisualStudio.TestTools.UnitTesting;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace FindNeedleUXTests
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

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
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
                
                // Launch the application
                _app = Application.Launch(appPath);
                
                // Wait for app to start and be ready
                Thread.Sleep(3000);
                
                // Get the main window
                _mainWindow = _app.GetMainWindow(_automation);
                Assert.IsNotNull(_mainWindow, "Failed to get main window");
                
                // Navigate to SearchRulesPage via menu: SearchQuery -> Rules
                NavigateToSearchRulesPage();
            }
            catch (Exception ex)
            {
                Assert.Fail($"Failed to initialize application. Error: {ex.Message}");
            }
        }

        private static void NavigateToSearchRulesPage()
        {
            // Find and click the SearchQuery menu
            var searchQueryMenu = _mainWindow.FindFirstDescendant(cf => cf.ByName("SearchQuery"));
            Assert.IsNotNull(searchQueryMenu, "SearchQuery menu should exist");
            searchQueryMenu.Click();
            Thread.Sleep(500);
            
            // Find and click the Rules menu item
            var rulesMenuItem = _mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("search_rules"));
            if (rulesMenuItem == null)
            {
                // Try by name as fallback
                rulesMenuItem = _mainWindow.FindFirstDescendant(cf => cf.ByName("Rules"));
            }
            Assert.IsNotNull(rulesMenuItem, "Rules menu item should exist");
            rulesMenuItem.Click();
            
            // Wait for navigation to complete
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
        /// </summary>
        [TestMethod]
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
        /// </summary>
        [TestMethod]
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
            var applyButton = FindElementByName("ApplyButton");
            // Cancel button doesn't have x:Name, find by Content text
            var cancelButton = _mainWindow?.FindFirstDescendant(cf => cf.ByName("Cancel"));

            // Assert
            Assert.IsNotNull(browseButton, "BrowseButton should be initialized");
            Assert.IsNotNull(ruleFilesListBox, "RuleFilesListBox should be initialized");
            Assert.IsNotNull(ruleSectionsListView, "RuleSectionsListView should be initialized");
            Assert.IsNotNull(applyButton, "ApplyButton should be initialized");
            Assert.IsNotNull(cancelButton, "Cancel button should be initialized");
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
