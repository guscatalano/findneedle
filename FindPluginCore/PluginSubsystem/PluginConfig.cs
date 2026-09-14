using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FindPluginCore.PluginSubsystem;

public enum StorageType
{
    InMemory,
    SqlLite,
    Hybrid,
    Auto
}

/// <summary>
/// The plugin configuration as persisted in PluginConfig.json. The on-disk shape is fixed - it is
/// shipped with the app and hand-edited by users - so every member carries the exact JSON name it has
/// always had ("entries" with lowercase "name" / "path" / "enabled"; the top-level settings in
/// PascalCase). Read and written with System.Text.Json, which is case-SENSITIVE and needs a settable
/// property (or a mutable collection) for every member; an earlier pass made <see cref="Entries"/>
/// read-only, and every config loaded as empty without any error.
/// </summary>
[ExcludeFromCodeCoverage]
public class PluginConfig
{
    [JsonPropertyName("entries")]
    public List<PluginConfigEntry> Entries { get; set; } = new();

    public void AddEntry(PluginConfigEntry entry) { if (entry != null) Entries.Add(entry); }
    public void RemoveEntry(PluginConfigEntry entry) => Entries.Remove(entry);
    public void ClearEntries() => Entries.Clear();

    [JsonPropertyName("PathToFakeLoadPlugin")]
    public string PathToFakeLoadPlugin { get; set; } = "";

    [JsonPropertyName("SearchQueryClass")]
    public string SearchQueryClass { get; set; } = "";

    /// <summary>Optional: path to the PlantUML JAR.</summary>
    [JsonPropertyName("PlantUMLPath")]
    public string PlantUMLPath { get; set; } = string.Empty;

    /// <summary>Optional: registry key in HKCU for extra plugins.</summary>
    [JsonPropertyName("UserRegistryPluginKey")]
    public string UserRegistryPluginKey { get; set; } = string.Empty;

    /// <summary>Enable/disable registry plugin loading.</summary>
    [JsonPropertyName("UserRegistryPluginKeyEnabled")]
    public bool UserRegistryPluginKeyEnabled { get; set; }

    [JsonPropertyName("UseSynchronousSearch")]
    public bool UseSynchronousSearch { get; set; }

    [JsonPropertyName("SearchStorageType")]
    public StorageType SearchStorageType { get; set; } = StorageType.Auto;
}

/// <summary>One plugin in the config. JSON: <c>{ "name": "...", "path": "...", "enabled": true }</c>.</summary>
public class PluginConfigEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Why the plugin is disabled (set by <see cref="Disable"/>); empty when enabled.</summary>
    [JsonPropertyName("disabledReason")]
    public string DisabledReason { get; set; } = "";

    public PluginConfigEntry() { }

    public static PluginConfigEntry Create(string name, string path, bool enabled = true)
        => new PluginConfigEntry { Name = name ?? "", Path = path ?? "", Enabled = enabled, DisabledReason = "" };

    public void Disable(string reason)
    {
        Enabled = false;
        DisabledReason = reason ?? "";
    }

    public void Enable()
    {
        Enabled = true;
        DisabledReason = "";
    }
}
