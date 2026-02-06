using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using FindNeedleUX.ViewObjects;

namespace FindNeedleUXTests
{
    /// <summary>
    /// Unit tests for SearchRulesPage logic
    /// These tests can run without WinAppDriver and verify core functionality
    /// </summary>
    [TestClass]
    public class SearchRulesPageLogicTests
    {
        private const string TestDataDir = "TestData";

        [ClassInitialize]
        public static void Setup(TestContext context)
        {
            // Ensure test data directory exists
            if (!Directory.Exists(TestDataDir))
            {
                Directory.CreateDirectory(TestDataDir);
            }
        }

        [TestMethod]
        public void RuleFiles_Collection_StartsEmpty()
        {
            // Arrange
            var collection = new ObservableCollection<RuleFileItem>();

            // Assert
            Assert.AreEqual(0, collection.Count, "RuleFiles collection should start empty");
        }

        [TestMethod]
        public void RuleFiles_WhenItemAdded_CollectionSizeIncreases()
        {
            // Arrange
            var collection = new ObservableCollection<RuleFileItem>();
            var item = new RuleFileItem
            {
                FileName = "test.rules.json",
                FilePath = "C:\\test.rules.json",
                Enabled = true,
                IsValid = true
            };

            // Act
            collection.Add(item);

            // Assert
            Assert.AreEqual(1, collection.Count, "Collection size should be 1 after adding item");
            Assert.AreEqual("test.rules.json", collection[0].FileName);
        }

        [TestMethod]
        public void RuleFiles_WhenMultipleItemsAdded_AllPresent()
        {
            // Arrange
            var collection = new ObservableCollection<RuleFileItem>();
            var files = new[]
            {
                new RuleFileItem { FileName = "file1.rules.json", FilePath = "C:\\file1.rules.json" },
                new RuleFileItem { FileName = "file2.rules.json", FilePath = "C:\\file2.rules.json" },
                new RuleFileItem { FileName = "file3.rules.json", FilePath = "C:\\file3.rules.json" }
            };

            // Act
            foreach (var file in files)
            {
                collection.Add(file);
            }

            // Assert
            Assert.AreEqual(3, collection.Count, "All items should be in collection");
            for (int i = 0; i < files.Length; i++)
            {
                Assert.AreEqual(files[i].FileName, collection[i].FileName);
            }
        }

        [TestMethod]
        public void RuleSectionItem_CreatedCorrectly_WithAllProperties()
        {
            // Arrange
            var section = new RuleSectionItem
            {
                Name = "Security",
                Description = "Security checks",
                Purpose = "Security",
                RuleCount = 5,
                SourceFileName = "security.rules.json",
                Enabled = true
            };

            // Assert
            Assert.AreEqual("Security", section.Name);
            Assert.AreEqual("Security checks", section.Description);
            Assert.AreEqual("Security", section.Purpose);
            Assert.AreEqual(5, section.RuleCount);
            Assert.IsTrue(section.Enabled);
        }

        [TestMethod]
        public void RuleFileItem_WithValidation_StatusSet()
        {
            // Arrange & Act
            var validFile = new RuleFileItem
            {
                FileName = "valid.rules.json",
                IsValid = true,
                ValidationError = null
            };

            var invalidFile = new RuleFileItem
            {
                FileName = "invalid.rules.json",
                IsValid = false,
                ValidationError = "File not found"
            };

            // Assert
            Assert.IsTrue(validFile.IsValid);
            Assert.IsNull(validFile.ValidationError);
            Assert.IsFalse(invalidFile.IsValid);
            Assert.AreEqual("File not found", invalidFile.ValidationError);
        }

        [TestMethod]
        public void RuleFileItem_Sections_EmptyByDefault()
        {
            // Arrange
            var item = new RuleFileItem { FileName = "test.rules.json" };

            // Assert
            Assert.IsNotNull(item.Sections);
            Assert.AreEqual(0, item.Sections.Count);
        }

        [TestMethod]
        public void RuleFileItem_Sections_CanAddMultipleSections()
        {
            // Arrange
            var item = new RuleFileItem { FileName = "test.rules.json" };
            var sections = new[]
            {
                new RuleSectionItem { Name = "Section1", Purpose = "Security" },
                new RuleSectionItem { Name = "Section2", Purpose = "Performance" }
            };

            // Act
            foreach (var section in sections)
            {
                item.Sections.Add(section);
            }

            // Assert
            Assert.AreEqual(2, item.Sections.Count);
            Assert.AreEqual("Section1", item.Sections[0].Name);
            Assert.AreEqual("Section2", item.Sections[1].Name);
        }

        [TestMethod]
        public void ValidRulesJsonFile_CanBeLoaded()
        {
            // Arrange
            var testFilePath = Path.Combine(TestDataDir, "valid-test.rules.json");
            Assert.IsTrue(File.Exists(testFilePath), $"Test file should exist at {testFilePath}");

            // Act
            var json = File.ReadAllText(testFilePath);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Assert
            Assert.IsTrue(root.TryGetProperty("sections", out var sections));
            Assert.IsTrue(sections.GetArrayLength() > 0);
        }

        [TestMethod]
        public void JsonWithSections_ParsesCorrectly()
        {
            // Arrange
            var json = @"{
                ""sections"": [
                    {
                        ""name"": ""Test Section"",
                        ""description"": ""A test"",
                        ""purpose"": ""Testing"",
                        ""rules"": []
                    }
                ]
            }";

            // Act
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            bool hasSections = root.TryGetProperty("sections", out var sectionsArray);
            int sectionCount = hasSections ? sectionsArray.GetArrayLength() : 0;

            // Assert
            Assert.IsTrue(hasSections);
            Assert.AreEqual(1, sectionCount);
        }

        [TestMethod]
        public void RulesCollection_FiresCollectionChanged_OnAdd()
        {
            // Arrange
            var collection = new ObservableCollection<RuleFileItem>();
            int changeCount = 0;
            collection.CollectionChanged += (s, e) => changeCount++;

            // Act
            collection.Add(new RuleFileItem { FileName = "test.rules.json" });

            // Assert
            Assert.AreEqual(1, changeCount, "CollectionChanged event should fire when item is added");
        }

        [TestMethod]
        public void RulesCollection_FiresCollectionChanged_OnRemove()
        {
            // Arrange
            var collection = new ObservableCollection<RuleFileItem>();
            var item = new RuleFileItem { FileName = "test.rules.json" };
            collection.Add(item);
            int changeCount = 0;
            collection.CollectionChanged += (s, e) => changeCount++;

            // Act
            collection.Remove(item);

            // Assert
            Assert.AreEqual(1, changeCount, "CollectionChanged event should fire when item is removed");
            Assert.AreEqual(0, collection.Count);
        }

        [TestMethod]
        public void RuleFileItem_Enabled_CanBeToggled()
        {
            // Arrange
            var item = new RuleFileItem { FileName = "test.rules.json", Enabled = true };

            // Act
            item.Enabled = false;

            // Assert
            Assert.IsFalse(item.Enabled);
        }
    }
}
