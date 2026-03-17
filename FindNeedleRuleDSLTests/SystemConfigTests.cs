using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using findneedle.RuleDSL;
using FindNeedleRuleDSL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleRuleDSLTests;

/// <summary>
/// Comprehensive unit tests for SystemConfig functionality in RuleDSL.
/// Tests deserialization, merging, and integration with PluginConfig structure.
/// </summary>
[TestClass]
public class SystemConfigTests
{
    private RuleLoader _ruleLoader = null!;
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _ruleLoader = new RuleLoader();
        _tempDir = Path.Combine(Path.GetTempPath(), "FindNeedleTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    #region Deserialization Tests

    [TestMethod]
    public void SystemConfig_Deserialize_MinimalConfig()
    {
        // Arrange
        var json = @"{
  ""schemaVersion"": ""2.0"",
  ""title"": ""Minimal Test"",
  ""sections"": []
}";
        var path = WriteJsonToTempFile(json, "minimal.rules.json");

        // Act
        var ruleSet = _ruleLoader.LoadUnifiedRuleSet(path);

        // Assert
        Assert.IsNotNull(ruleSet);
        Assert.IsNull(ruleSet.SystemConfig, "SystemConfig should be null when not provided");
        Assert.AreEqual("Minimal Test", ruleSet.Title);
    }

    [TestMethod]
    public void SystemConfig_Deserialize_CompleteStandaloneConfig()
    {
        // Arrange - mirrors the structure of PluginConfig.json
        var json = @"{
  ""schemaVersion"": ""2.0"",
  ""version"": ""1.0"",
  ""title"": ""Complete Standalone Config"",
  ""systemConfig"": {
    ""useGlobalPluginConfig"": false,
    ""plugins"": {
      ""entries"": [
        {
          ""name"": ""BasicFilters"",
          ""path"": ""BasicFiltersPlugin.dll"",
          ""enabled"": true
        },
        {
          ""name"": ""BasicText"",
          ""path"": ""BasicTextPlugin.dll"",
          ""enabled"": true
        },
        {
          ""name"": ""ETWPlugin"",
          ""path"": ""ETWPlugin.dll"",
          ""enabled"": false,
          ""disabledReason"": ""Not needed for this test""
        }
      ],
      ""fakeLoadPluginPath"": ""FakeLoadPlugin.exe"",
      ""searchQueryClass"": ""NuSearchQuery"",
      ""userRegistryPluginKey"": ""Software\\FindNeedle\\Plugins"",
      ""userRegistryPluginKeyEnabled"": true
    },
    ""search"": {
      ""name"": ""Test Search"",
      ""storageType"": ""Auto"",
      ""useSynchronousSearch"": false,
      ""defaultDepth"": ""Intermediate""
    },
    ""tools"": {
      ""plantUmlPath"": ""C:\\Tools\\plantuml.jar"",
      ""mermaidCliPath"": ""C:\\Tools\\mmdc.exe""
    }
  },
  ""sections"": []
}";
        var path = WriteJsonToTempFile(json, "complete.rules.json");

        // Act
        var ruleSet = _ruleLoader.LoadUnifiedRuleSet(path);

        // Assert
        Assert.IsNotNull(ruleSet);
        Assert.IsNotNull(ruleSet.SystemConfig);
        
        // Verify useGlobalPluginConfig
        Assert.IsFalse(ruleSet.SystemConfig.UseGlobalPluginConfig);
        
        // Verify plugin configuration
        Assert.IsNotNull(ruleSet.SystemConfig.Plugins);
        Assert.AreEqual("FakeLoadPlugin.exe", ruleSet.SystemConfig.Plugins.FakeLoadPluginPath);
        Assert.AreEqual("NuSearchQuery", ruleSet.SystemConfig.Plugins.SearchQueryClass);
        Assert.AreEqual("Software\\FindNeedle\\Plugins", ruleSet.SystemConfig.Plugins.UserRegistryPluginKey);
        Assert.IsTrue(ruleSet.SystemConfig.Plugins.UserRegistryPluginKeyEnabled);
        
        // Verify plugin entries
        Assert.IsNotNull(ruleSet.SystemConfig.Plugins.Entries);
        Assert.AreEqual(3, ruleSet.SystemConfig.Plugins.Entries.Count);
        
        var basicFilters = ruleSet.SystemConfig.Plugins.Entries[0];
        Assert.AreEqual("BasicFilters", basicFilters.Name);
        Assert.AreEqual("BasicFiltersPlugin.dll", basicFilters.Path);
        Assert.IsTrue(basicFilters.Enabled);
        
        var etw = ruleSet.SystemConfig.Plugins.Entries[2];
        Assert.AreEqual("ETWPlugin", etw.Name);
        Assert.IsFalse(etw.Enabled);
        Assert.AreEqual("Not needed for this test", etw.DisabledReason);
        
        // Verify search configuration
        Assert.IsNotNull(ruleSet.SystemConfig.Search);
        Assert.AreEqual("Test Search", ruleSet.SystemConfig.Search.Name);
        Assert.AreEqual("Auto", ruleSet.SystemConfig.Search.StorageType);
        Assert.IsFalse(ruleSet.SystemConfig.Search.UseSynchronousSearch);
        Assert.AreEqual("Intermediate", ruleSet.SystemConfig.Search.DefaultDepth);
        
        // Verify tool configuration
        Assert.IsNotNull(ruleSet.SystemConfig.Tools);
        Assert.AreEqual("C:\\Tools\\plantuml.jar", ruleSet.SystemConfig.Tools.PlantUmlPath);
        Assert.AreEqual("C:\\Tools\\mmdc.exe", ruleSet.SystemConfig.Tools.MermaidCliPath);
    }

    [TestMethod]
    public void SystemConfig_Deserialize_HybridConfig()
    {
        // Arrange
        var json = @"{
  ""schemaVersion"": ""2.0"",
  ""title"": ""Hybrid Config"",
  ""systemConfig"": {
    ""useGlobalPluginConfig"": true,
    ""search"": {
      ""name"": ""Quick Scan"",
      ""storageType"": ""InMemory"",
      ""defaultDepth"": ""Shallow""
    }
  },
  ""sections"": []
}";
        var path = WriteJsonToTempFile(json, "hybrid.rules.json");

        // Act
        var ruleSet = _ruleLoader.LoadUnifiedRuleSet(path);

        // Assert
        Assert.IsNotNull(ruleSet);
        Assert.IsNotNull(ruleSet.SystemConfig);
        Assert.IsTrue(ruleSet.SystemConfig.UseGlobalPluginConfig, "Should use global config");
        Assert.IsNull(ruleSet.SystemConfig.Plugins, "Plugins should be null (using global)");
        Assert.IsNotNull(ruleSet.SystemConfig.Search);
        Assert.AreEqual("Quick Scan", ruleSet.SystemConfig.Search.Name);
        Assert.AreEqual("InMemory", ruleSet.SystemConfig.Search.StorageType);
        Assert.AreEqual("Shallow", ruleSet.SystemConfig.Search.DefaultDepth);
    }

    [TestMethod]
    public void SystemConfig_DefaultValues_AreCorrect()
    {
        // Arrange
        var config = new SystemConfig();

        // Assert
        Assert.IsTrue(config.UseGlobalPluginConfig, "Default should be true for backward compatibility");
        Assert.IsNull(config.Plugins);
        Assert.IsNull(config.Search);
        Assert.IsNull(config.Tools);
    }

    [TestMethod]
    public void PluginConfiguration_DefaultValues_AreCorrect()
    {
        // Arrange
        var config = new PluginConfiguration();

        // Assert
        Assert.IsNull(config.Entries);
        Assert.IsNull(config.FakeLoadPluginPath);
        Assert.IsNull(config.SearchQueryClass);
        Assert.IsNull(config.UserRegistryPluginKey);
        Assert.IsFalse(config.UserRegistryPluginKeyEnabled, "Default should be false");
    }

    [TestMethod]
    public void PluginEntry_DefaultValues_AreCorrect()
    {
        // Arrange
        var entry = new PluginEntry();

        // Assert
        Assert.AreEqual(string.Empty, entry.Name);
        Assert.AreEqual(string.Empty, entry.Path);
        Assert.IsTrue(entry.Enabled, "Default should be true");
        Assert.IsNull(entry.DisabledReason);
    }

    [TestMethod]
    public void SearchConfiguration_DefaultValues_AreCorrect()
    {
        // Arrange
        var config = new SearchConfiguration();

        // Assert
        Assert.IsNull(config.StorageType);
        Assert.IsFalse(config.UseSynchronousSearch);
        Assert.IsNull(config.DefaultDepth);
        Assert.IsNull(config.Name);
    }

    #endregion

    #region Config Merging Tests

    [TestMethod]
    public void LoadMergedSystemConfig_SingleFile_ReturnsConfig()
    {
        // Arrange
        var json = @"{
  ""schemaVersion"": ""2.0"",
  ""systemConfig"": {
    ""search"": {
      ""name"": ""Single Config"",
      ""storageType"": ""Auto""
    }
  },
  ""sections"": []
}";
        var path = WriteJsonToTempFile(json, "single.rules.json");

        // Act
        var merged = _ruleLoader.LoadMergedSystemConfig(new[] { path });

        // Assert
        Assert.IsNotNull(merged);
        Assert.IsNotNull(merged.Search);
        Assert.AreEqual("Single Config", merged.Search.Name);
        Assert.AreEqual("Auto", merged.Search.StorageType);
    }

    [TestMethod]
    public void LoadMergedSystemConfig_MultipleFiles_LastWins()
    {
        // Arrange
        var json1 = @"{
  ""schemaVersion"": ""2.0"",
  ""systemConfig"": {
    ""search"": {
      ""name"": ""First Config"",
      ""storageType"": ""InMemory"",
      ""defaultDepth"": ""Shallow""
    },
    ""tools"": {
      ""plantUmlPath"": ""First.jar""
    }
  },
  ""sections"": []
}";
        var json2 = @"{
  ""schemaVersion"": ""2.0"",
  ""systemConfig"": {
    ""search"": {
      ""name"": ""Second Config"",
      ""storageType"": ""SqlLite""
    },
    ""tools"": {
      ""mermaidCliPath"": ""Second.exe""
    }
  },
  ""sections"": []
}";
        var path1 = WriteJsonToTempFile(json1, "first.rules.json");
        var path2 = WriteJsonToTempFile(json2, "second.rules.json");

        // Act
        var merged = _ruleLoader.LoadMergedSystemConfig(new[] { path1, path2 });

        // Assert
        Assert.IsNotNull(merged);
        Assert.IsNotNull(merged.Search);
        
        // Last file wins for conflicting properties
        Assert.AreEqual("Second Config", merged.Search.Name, "Last file should win for name");
        Assert.AreEqual("SqlLite", merged.Search.StorageType, "Last file should win for storageType");
        
        // Non-conflicting properties preserved
        Assert.AreEqual("Shallow", merged.Search.DefaultDepth, "First file's defaultDepth should be preserved");
        
        // Tools should merge (both properties present)
        Assert.IsNotNull(merged.Tools);
        Assert.AreEqual("First.jar", merged.Tools.PlantUmlPath, "First file's plantUmlPath should be preserved");
        Assert.AreEqual("Second.exe", merged.Tools.MermaidCliPath, "Second file's mermaidCliPath should be added");
    }

    [TestMethod]
    public void LoadMergedSystemConfig_PluginEntries_Merge()
    {
        // Arrange
        var json1 = @"{
  ""schemaVersion"": ""2.0"",
  ""systemConfig"": {
    ""plugins"": {
      ""searchQueryClass"": ""SearchQuery"",
      ""entries"": [
        { ""name"": ""PluginA"", ""path"": ""A.dll"", ""enabled"": true },
        { ""name"": ""PluginB"", ""path"": ""B.dll"", ""enabled"": true }
      ]
    }
  },
  ""sections"": []
}";
        var json2 = @"{
  ""schemaVersion"": ""2.0"",
  ""systemConfig"": {
    ""plugins"": {
      ""searchQueryClass"": ""NuSearchQuery"",
      ""entries"": [
        { ""name"": ""PluginB"", ""path"": ""B_Updated.dll"", ""enabled"": false },
        { ""name"": ""PluginC"", ""path"": ""C.dll"", ""enabled"": true }
      ]
    }
  },
  ""sections"": []
}";
        var path1 = WriteJsonToTempFile(json1, "plugins1.rules.json");
        var path2 = WriteJsonToTempFile(json2, "plugins2.rules.json");

        // Act
        var merged = _ruleLoader.LoadMergedSystemConfig(new[] { path1, path2 });

        // Assert
        Assert.IsNotNull(merged);
        Assert.IsNotNull(merged.Plugins);
        Assert.IsNotNull(merged.Plugins.Entries);
        
        // SearchQueryClass: last wins
        Assert.AreEqual("NuSearchQuery", merged.Plugins.SearchQueryClass);
        
        // Plugin entries: should have A, B (updated), C
        Assert.AreEqual(3, merged.Plugins.Entries.Count);
        
        var pluginA = merged.Plugins.Entries.FirstOrDefault(e => e.Name == "PluginA");
        Assert.IsNotNull(pluginA);
        Assert.AreEqual("A.dll", pluginA.Path);
        Assert.IsTrue(pluginA.Enabled);
        
        var pluginB = merged.Plugins.Entries.FirstOrDefault(e => e.Name == "PluginB");
        Assert.IsNotNull(pluginB);
        Assert.AreEqual("B_Updated.dll", pluginB.Path, "PluginB should be updated from second file");
        Assert.IsFalse(pluginB.Enabled, "PluginB should be disabled per second file");
        
        var pluginC = merged.Plugins.Entries.FirstOrDefault(e => e.Name == "PluginC");
        Assert.IsNotNull(pluginC);
        Assert.AreEqual("C.dll", pluginC.Path);
        Assert.IsTrue(pluginC.Enabled);
    }

    [TestMethod]
    public void LoadMergedSystemConfig_NoConfigFiles_ReturnsNull()
    {
        // Act
        var merged = _ruleLoader.LoadMergedSystemConfig(Array.Empty<string>());

        // Assert
        Assert.IsNull(merged);
    }

    [TestMethod]
    public void LoadMergedSystemConfig_FilesWithoutConfig_ReturnsNull()
    {
        // Arrange
        var json = @"{
  ""schemaVersion"": ""2.0"",
  ""title"": ""No Config"",
  ""sections"": []
}";
        var path = WriteJsonToTempFile(json, "noconfig.rules.json");

        // Act
        var merged = _ruleLoader.LoadMergedSystemConfig(new[] { path });

        // Assert
        Assert.IsNull(merged, "Should return null when no files have systemConfig");
    }

    [TestMethod]
    public void LoadMergedSystemConfig_UseGlobalPluginConfig_LastFileWins()
    {
        // Arrange
        var json1 = @"{
  ""systemConfig"": {
    ""useGlobalPluginConfig"": true,
    ""search"": { ""name"": ""First"" }
  },
  ""sections"": []
}";
        var json2 = @"{
  ""systemConfig"": {
    ""useGlobalPluginConfig"": false,
    ""search"": { ""name"": ""Second"" }
  },
  ""sections"": []
}";
        var path1 = WriteJsonToTempFile(json1, "global1.rules.json");
        var path2 = WriteJsonToTempFile(json2, "global2.rules.json");

        // Act
        var merged = _ruleLoader.LoadMergedSystemConfig(new[] { path1, path2 });

        // Assert
        Assert.IsNotNull(merged);
        Assert.IsFalse(merged.UseGlobalPluginConfig, "If any file sets false, result should be false");
    }

    #endregion

    #region Integration with PluginConfig.json Structure

    [TestMethod]
    public void SystemConfig_MatchesPluginConfigJson_Structure()
    {
        // Arrange - This JSON structure mirrors the actual PluginConfig.json file
        var json = @"{
  ""schemaVersion"": ""2.0"",
  ""systemConfig"": {
    ""useGlobalPluginConfig"": false,
    ""plugins"": {
      ""entries"": [
        { ""name"": ""BasicFilters"", ""path"": ""BasicFiltersPlugin.dll"", ""enabled"": true },
        { ""name"": ""BasicText"", ""path"": ""BasicTextPlugin.dll"", ""enabled"": true },
        { ""name"": ""BasicOutputs"", ""path"": ""BasicOutputsPlugin.dll"" },
        { ""name"": ""EventLogPlugin"", ""path"": ""EventLogPlugin.dll"" },
        { ""name"": ""ETWPlugin"", ""path"": ""ETWPlugin.dll"", ""enabled"": true },
        { ""name"": ""SessionManagementProcessor"", ""path"": ""SessionManagementProcessor.dll"", ""enabled"": true },
        { ""name"": ""KustoPlugin"", ""path"": ""KustoPlugin.dll"", ""enabled"": true }
      ],
      ""fakeLoadPluginPath"": ""FakeLoadPlugin.exe"",
      ""searchQueryClass"": ""NuSearchQuery"",
      ""userRegistryPluginKey"": ""Software\\FindNeedle\\Plugins"",
      ""userRegistryPluginKeyEnabled"": true
    }
  },
  ""sections"": []
}";
        var path = WriteJsonToTempFile(json, "pluginconfig-match.rules.json");

        // Act
        var ruleSet = _ruleLoader.LoadUnifiedRuleSet(path);

        // Assert
        Assert.IsNotNull(ruleSet);
        Assert.IsNotNull(ruleSet.SystemConfig);
        Assert.IsNotNull(ruleSet.SystemConfig.Plugins);
        Assert.IsNotNull(ruleSet.SystemConfig.Plugins.Entries);
        
        // Verify all 7 plugin entries
        Assert.AreEqual(7, ruleSet.SystemConfig.Plugins.Entries.Count);
        
        // Verify entries match PluginConfig.json structure
        var expectedPlugins = new[]
        {
            ("BasicFilters", "BasicFiltersPlugin.dll", true),
            ("BasicText", "BasicTextPlugin.dll", true),
            ("BasicOutputs", "BasicOutputsPlugin.dll", true), // default enabled
            ("EventLogPlugin", "EventLogPlugin.dll", true), // default enabled
            ("ETWPlugin", "ETWPlugin.dll", true),
            ("SessionManagementProcessor", "SessionManagementProcessor.dll", true),
            ("KustoPlugin", "KustoPlugin.dll", true)
        };
        
        for (int i = 0; i < expectedPlugins.Length; i++)
        {
            var expected = expectedPlugins[i];
            var actual = ruleSet.SystemConfig.Plugins.Entries[i];
            
            Assert.AreEqual(expected.Item1, actual.Name, $"Plugin {i} name mismatch");
            Assert.AreEqual(expected.Item2, actual.Path, $"Plugin {i} path mismatch");
            Assert.AreEqual(expected.Item3, actual.Enabled, $"Plugin {i} enabled mismatch");
        }
        
        // Verify other PluginConfig.json properties
        Assert.AreEqual("FakeLoadPlugin.exe", ruleSet.SystemConfig.Plugins.FakeLoadPluginPath);
        Assert.AreEqual("NuSearchQuery", ruleSet.SystemConfig.Plugins.SearchQueryClass);
        Assert.AreEqual("Software\\FindNeedle\\Plugins", ruleSet.SystemConfig.Plugins.UserRegistryPluginKey);
        Assert.IsTrue(ruleSet.SystemConfig.Plugins.UserRegistryPluginKeyEnabled);
    }

    #endregion

    #region Example Files Validation

    [TestMethod]
    public void CompleteConfigExample_IsValid()
    {
        // Arrange
        var examplesDir = FindExamplesDirectory();
        var path = Path.Combine(examplesDir, "complete-config.rules.json");
        
        // Skip if file doesn't exist (may not be committed yet)
        if (!File.Exists(path))
        {
            Assert.Inconclusive("complete-config.rules.json not found in Examples folder");
            return;
        }

        // Act
        var ruleSet = _ruleLoader.LoadUnifiedRuleSet(path);

        // Assert
        Assert.IsNotNull(ruleSet);
        Assert.IsNotNull(ruleSet.SystemConfig);
        Assert.IsFalse(ruleSet.SystemConfig.UseGlobalPluginConfig);
        Assert.IsNotNull(ruleSet.SystemConfig.Plugins);
        Assert.IsNotNull(ruleSet.SystemConfig.Search);
        Assert.IsNotNull(ruleSet.SystemConfig.Tools);
    }

    [TestMethod]
    public void HybridConfigExample_IsValid()
    {
        // Arrange
        var examplesDir = FindExamplesDirectory();
        var path = Path.Combine(examplesDir, "hybrid-config.rules.json");
        
        // Skip if file doesn't exist
        if (!File.Exists(path))
        {
            Assert.Inconclusive("hybrid-config.rules.json not found in Examples folder");
            return;
        }

        // Act
        var ruleSet = _ruleLoader.LoadUnifiedRuleSet(path);

        // Assert
        Assert.IsNotNull(ruleSet);
        Assert.IsNotNull(ruleSet.SystemConfig);
        Assert.IsTrue(ruleSet.SystemConfig.UseGlobalPluginConfig);
        Assert.IsNotNull(ruleSet.SystemConfig.Search);
    }

    [TestMethod]
    public void MinimalRulesOnlyExample_IsValid()
    {
        // Arrange
        var examplesDir = FindExamplesDirectory();
        var path = Path.Combine(examplesDir, "minimal-rules-only.rules.json");
        
        // Skip if file doesn't exist
        if (!File.Exists(path))
        {
            Assert.Inconclusive("minimal-rules-only.rules.json not found in Examples folder");
            return;
        }

        // Act
        var ruleSet = _ruleLoader.LoadUnifiedRuleSet(path);

        // Assert
        Assert.IsNotNull(ruleSet);
        Assert.IsNull(ruleSet.SystemConfig, "Minimal example should not have systemConfig");
        Assert.IsNotNull(ruleSet.Sections);
        Assert.IsTrue(ruleSet.Sections.Count > 0);
    }

    #endregion

    #region Backward Compatibility Tests

    [TestMethod]
    public void BackwardCompatibility_OldRulesFiles_StillWork()
    {
        // Arrange - Old format without systemConfig
        var json = @"{
  ""schemaVersion"": ""1.0"",
  ""version"": ""1.0"",
  ""title"": ""Old Format Rules"",
  ""sections"": [
    {
      ""name"": ""ErrorFilter"",
      ""purpose"": ""filter"",
      ""rules"": [
        {
          ""name"": ""include-errors"",
          ""match"": ""ERROR"",
          ""action"": { ""type"": ""include"" }
        }
      ]
    }
  ]
}";
        var path = WriteJsonToTempFile(json, "old-format.rules.json");

        // Act
        var ruleSet = _ruleLoader.LoadUnifiedRuleSet(path);

        // Assert
        Assert.IsNotNull(ruleSet);
        Assert.IsNull(ruleSet.SystemConfig, "Old format should not have systemConfig");
        Assert.AreEqual("1.0", ruleSet.SchemaVersion);
        Assert.AreEqual(1, ruleSet.Sections.Count);
    }

    [TestMethod]
    public void BackwardCompatibility_SystemConfig_Defaults_PreserveOldBehavior()
    {
        // Arrange
        var config = new SystemConfig();

        // Assert - defaults should preserve old behavior
        Assert.IsTrue(config.UseGlobalPluginConfig, 
            "Default should be true to preserve backward compatibility with global PluginConfig.json");
    }

    #endregion

    #region Error Handling Tests

    [TestMethod]
    [ExpectedException(typeof(FileNotFoundException))]
    public void LoadUnifiedRuleSet_NonExistentFile_ThrowsException()
    {
        // Act
        _ruleLoader.LoadUnifiedRuleSet("nonexistent.rules.json");
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidOperationException))]
    public void LoadUnifiedRuleSet_InvalidJson_ThrowsException()
    {
        // Arrange
        var path = WriteJsonToTempFile("{ invalid json", "invalid.rules.json");

        // Act
        _ruleLoader.LoadUnifiedRuleSet(path);
    }

    [TestMethod]
    public void LoadMergedSystemConfig_InvalidFile_ContinuesProcessing()
    {
        // Arrange
        var validJson = @"{
  ""systemConfig"": {
    ""search"": { ""name"": ""Valid Config"" }
  },
  ""sections"": []
}";
        var validPath = WriteJsonToTempFile(validJson, "valid.rules.json");
        var invalidPath = Path.Combine(_tempDir, "nonexistent.rules.json");

        // Act
        var merged = _ruleLoader.LoadMergedSystemConfig(new[] { invalidPath, validPath });

        // Assert
        Assert.IsNotNull(merged, "Should process valid file despite invalid file");
        Assert.IsNotNull(merged.Search);
        Assert.AreEqual("Valid Config", merged.Search.Name);
    }

    #endregion

    #region Helper Methods

    private string WriteJsonToTempFile(string json, string filename)
    {
        var path = Path.Combine(_tempDir, filename);
        File.WriteAllText(path, json);
        return path;
    }

    private string FindExamplesDirectory()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var current = new DirectoryInfo(baseDir);

        while (current != null)
        {
            var examplesPath = Path.Combine(current.FullName, "FindNeedleRuleDSL", "Examples");
            if (Directory.Exists(examplesPath))
                return examplesPath;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find FindNeedleRuleDSL/Examples directory");
    }

    #endregion
}
