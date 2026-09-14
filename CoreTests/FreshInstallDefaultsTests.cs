using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FindPluginCore.PluginSubsystem;
using FindPluginCore.Searching;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

/// <summary>
/// "Does a fresh install make sense?" checks for things that ship in the box: the plugin config and
/// the automatic storage-tier thresholds. (UI preference defaults are covered in FindNeedleUXTests;
/// the AutoRules master-switch default lives in AutoRulesStoreTests.)
/// </summary>
[TestClass]
public class FreshInstallDefaultsTests
{
    private static PluginConfig LoadShippedConfig()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ShippedPluginConfig.json");
        Assert.IsTrue(File.Exists(path), $"shipped plugin config not copied to output: {path}");
        var json = File.ReadAllText(path);
        var cfg = JsonSerializer.Deserialize<PluginConfig>(json, new JsonSerializerOptions { IncludeFields = true });
        Assert.IsNotNull(cfg, "PluginConfig.json failed to deserialize");
        return cfg!;
    }

    [TestMethod]
    public void ShippedConfig_HasBaselinePluginsEnabled()
    {
        var cfg = LoadShippedConfig();
        string[] expected =
        {
            "BasicTextPlugin", "CsvPlugin", "JsonPlugin", "EventLogPlugin",
            "ETWPlugin", "ZipFilePlugin", "PcapPlugin",
        };
        foreach (var name in expected)
        {
            var entry = cfg.Entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(entry, $"{name} should ship in PluginConfig.json");
            Assert.IsTrue(entry!.Enabled, $"{name} should be enabled out of the box");
            Assert.IsFalse(string.IsNullOrWhiteSpace(entry.Path), $"{name} should have a dll path");
        }
    }

    [TestMethod]
    public void ShippedConfigShape_RoundTrips_ThroughTheSerializer()
    {
        // The on-disk keys are fixed ("entries" with lowercase "name"/"path"/"enabled"; PascalCase settings)
        // and System.Text.Json is case-sensitive. A refactor that renamed the members or made the list
        // read-only loaded every config as EMPTY with no error, so pin the exact shape both ways.
        const string shipped = @"{ ""entries"": [ { ""name"": ""A"", ""path"": ""A.dll"", ""enabled"": false } ],
                                   ""PathToFakeLoadPlugin"": ""FakeLoadPlugin.exe"", ""SearchQueryClass"": ""NuSearchQuery"",
                                   ""UserRegistryPluginKeyEnabled"": true }";
        var opts = new JsonSerializerOptions { IncludeFields = true };
        var cfg = JsonSerializer.Deserialize<PluginConfig>(shipped, opts);
        Assert.IsNotNull(cfg);
        Assert.AreEqual(1, cfg!.Entries.Count, "the lowercase 'entries' array must populate Entries");
        Assert.AreEqual("A", cfg.Entries[0].Name);
        Assert.AreEqual("A.dll", cfg.Entries[0].Path);
        Assert.IsFalse(cfg.Entries[0].Enabled, "lowercase 'enabled' must populate Enabled");
        Assert.AreEqual("FakeLoadPlugin.exe", cfg.PathToFakeLoadPlugin);
        Assert.AreEqual("NuSearchQuery", cfg.SearchQueryClass);
        Assert.IsTrue(cfg.UserRegistryPluginKeyEnabled);

        cfg.AddEntry(PluginConfigEntry.Create("B", "B.dll"));
        var written = JsonSerializer.Serialize(cfg, opts);
        StringAssert.Contains(written, "\"entries\"", "written back with the on-disk key, not a PascalCase rename");
        StringAssert.Contains(written, "\"name\":\"B\"");
        var again = JsonSerializer.Deserialize<PluginConfig>(written, opts)!;
        Assert.AreEqual(2, again.Entries.Count, "save then load keeps every entry");
    }

    [TestMethod]
    public void ShippedConfig_UsesNuSearchQuery()
        => Assert.AreEqual("NuSearchQuery", LoadShippedConfig().SearchQueryClass);

    // ── Auto storage-tier thresholds: <10k InMemory / 10k–50k Hybrid / >50k SQLite ──

    [DataTestMethod]
    [DataRow(0, StorageType.InMemory)]
    [DataRow(9_999, StorageType.InMemory)]
    [DataRow(10_000, StorageType.Hybrid)]
    [DataRow(49_999, StorageType.Hybrid)]
    [DataRow(50_000, StorageType.SqlLite)]
    [DataRow(5_000_000, StorageType.SqlLite)]
    public void AutoStorage_PicksTierByRowCount(int rows, StorageType expected)
        => Assert.AreEqual(expected, NuSearchQuery.ChooseAutoStorageType(rows, TimeSpan.Zero));

    [TestMethod]
    public void AutoStorage_SlowSmallScan_AvoidsInMemory()
    {
        // Few rows but a slow (>30s) estimate → don't keep it all in memory; settle to Hybrid.
        Assert.AreEqual(StorageType.Hybrid,
            NuSearchQuery.ChooseAutoStorageType(5_000, TimeSpan.FromSeconds(45)));
    }
}
