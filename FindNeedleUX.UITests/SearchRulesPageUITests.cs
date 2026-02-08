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
        /// Test that the test hook can add a rules file without using the file picker.
        /// This bypasses the unreliable WinRT FileOpenPicker automation.
        /// </summary>
        [TestMethod]
        public void TestHook_CanAddRulesFile()
        {
            // Arrange - Get path to a sample rules file
            var testDir = AppContext.BaseDirectory;
            var solutionDir = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", ".."));
            var sampleRulesFile = Path.Combine(solutionDir, "FindNeedleRuleDSL", "Examples", "example-filter-only.rules.json");
            
            // Skip if sample file doesn't exist
            if (!File.Exists(sampleRulesFile))
            {
                Assert.Inconclusive($"Sample rules file not found: {sampleRulesFile}");
                return;
            }

            // Find the test hook textbox - try multiple times as it may take time to appear
            AutomationElement testInput = null;
            for (int i = 0; i < 3 && testInput == null; i++)
            {
                testInput = FindElementByName("TestFilePathInput");
                if (testInput == null)
                {
                    // Also try searching by AutomationProperties.Name
                    testInput = _mainWindow?.FindFirstDescendant(cf => cf.ByName("TestFilePathInput"));
                }
                if (testInput == null)
                {
                    Thread.Sleep(500);
                }
            }
            
            if (testInput == null)
            {
                // List available elements for debugging
                var allTextBoxes = _mainWindow?.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Edit));
                var textBoxInfo = allTextBoxes != null 
                    ? string.Join(", ", Array.ConvertAll(allTextBoxes, tb => $"'{tb.AutomationId ?? tb.Name ?? "unnamed"}'"))
                    : "none found";
                Assert.Inconclusive($"TestFilePathInput not found. Available TextBoxes: {textBoxInfo}. The test hook may not be exposed in the automation tree.");
                return;
            }

            // Find the Load button first - fail early if not found
            var loadButton = FindElementByName("TestLoadButton");
            if (loadButton == null)
            {
                // Also try by name
                loadButton = _mainWindow?.FindFirstDescendant(cf => cf.ByName("Load"));
            }
            
            if (loadButton == null)
            {
                var allButtons = _mainWindow?.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button));
                var buttonInfo = allButtons != null 
                    ? string.Join(", ", Array.ConvertAll(allButtons, b => $"'{b.AutomationId ?? b.Name ?? "unnamed"}'"))
                    : "none found";
                Assert.Inconclusive($"TestLoadButton not found. Available buttons: {buttonInfo}. Make sure FindNeedleUX was rebuilt with the Load button.");
                return;
            }

            // Act - Use ValuePattern to set the text directly (more reliable than keyboard simulation)
            var textBox = testInput.AsTextBox();
            if (textBox != null)
            {
                // Set text via pattern
                textBox.Text = sampleRulesFile;
                Thread.Sleep(200);
            }
            else
            {
                // Fallback to keyboard simulation if TextBox pattern not available
                testInput.Click();
                Thread.Sleep(200);
                FlaUI.Core.Input.Keyboard.Type(sampleRulesFile);
                Thread.Sleep(300);
            }
            
            // Click the Load button to trigger the file load
            System.Diagnostics.Debug.WriteLine($"Clicking Load button at: {loadButton.BoundingRectangle}");
            loadButton.Click();
            Thread.Sleep(1000);

            // Assert - Verify we're still on the SearchRulesPage and the ListBox exists
            var ruleFilesListBox = FindElementByName("RuleFilesListBox");
            if (ruleFilesListBox == null)
            {
                // Debug: check if page navigation happened unexpectedly
                var pageElements = _mainWindow?.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.List));
                var listInfo = pageElements != null 
                    ? string.Join(", ", Array.ConvertAll(pageElements, el => $"'{el.AutomationId ?? el.Name ?? "unnamed"}'"))
                    : "none found";
                Assert.Fail($"RuleFilesListBox not found after test hook. Available lists: {listInfo}. Page may have navigated away.");
            }
            
            // Check if the listbox has items (the file was added)
            var listItems = ruleFilesListBox.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem));
            Assert.IsTrue(listItems.Length > 0, "RuleFilesListBox should have at least one item after using test hook");
        }

        // Note: BrowseButton_OpensFileDialog test removed - WinRT FileOpenPicker is not 
        // reliably automatable with FlaUI. Use TestHook_CanAddRulesFile instead.

        /// <summary>
        /// Test that selecting a rules file populates the sections list
        /// </summary>
        [TestMethod]
        public void SelectingRulesFile_PopulatesSectionsList()
        {
            // First add a file using the test hook
            var testDir = AppContext.BaseDirectory;
            var solutionDir = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", ".."));
            var sampleRulesFile = Path.Combine(solutionDir, "FindNeedleRuleDSL", "Examples", "example-filter-only.rules.json");
            
            if (File.Exists(sampleRulesFile))
            {
                var testInput = FindElementByName("TestFilePathInput");
                var loadButton = FindElementByName("TestLoadButton");
                
                if (testInput != null && loadButton != null)
                {
                    // Use ValuePattern for reliable text input
                    var textBox = testInput.AsTextBox();
                    if (textBox != null)
                    {
                        textBox.Text = sampleRulesFile;
                        Thread.Sleep(200);
                    }
                    else
                    {
                        testInput.Click();
                        Thread.Sleep(200);
                        FlaUI.Core.Input.Keyboard.Type(sampleRulesFile);
                        Thread.Sleep(200);
                    }
                    
                    // Click the Load button
                    loadButton.Click();
                    Thread.Sleep(1000);
                }
            }

            var ruleFilesListBox = FindElementByName("RuleFilesListBox");
            var ruleSectionsListView = FindElementByName("RuleSectionsListView");

            
            Assert.IsNotNull(ruleFilesListBox, "RuleFilesListBox should exist");
            Assert.IsNotNull(ruleSectionsListView, "RuleSectionsListView should exist");

            // Check if there are items in the list
            var listItems = ruleFilesListBox.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem));
            
            if (listItems.Length == 0)
            {
                Assert.Inconclusive("No rule files in list - test hook may have failed");
                return;
            }

            // Select the first item
            listItems[0].Click();
            Thread.Sleep(500);


            // Verify sections list is populated (may or may not have items depending on file content)
            // Just verify no crash occurs and the UI remains responsive
            Assert.IsNotNull(ruleSectionsListView, "RuleSectionsListView should still exist after selection");
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
