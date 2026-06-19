using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FindPluginCore.Searching.AutoRules;

/// <summary>
/// Discovers the bundled "common rules" library shipped with the app: a <c>CommonRules</c> folder next
/// to the executable containing <c>*.rules.json</c> files and an optional <c>common-rules.manifest.json</c>
/// that gives each one a friendly name and a default auto-add condition. Rules without a manifest entry
/// are still offered (the user sets their own condition).
/// </summary>
public static class CommonRuleLibrary
{
    /// <summary>Folder the library is deployed to (alongside the app binaries).</summary>
    public static string LibraryDir =>
        Path.Combine(AppContext.BaseDirectory, "CommonRules");

    /// <summary>Build an <see cref="AutoRuleEntry"/> per bundled rule file (stable id derived from the
    /// file name so re-discovery doesn't create duplicates).</summary>
    public static List<AutoRuleEntry> Discover()
    {
        var entries = new List<AutoRuleEntry>();
        try
        {
            if (!Directory.Exists(LibraryDir)) return entries;

            var manifest = LoadManifest();
            foreach (var file in Directory.EnumerateFiles(LibraryDir, "*.rules.json").OrderBy(f => f))
            {
                var fileName = Path.GetFileName(file);
                manifest.TryGetValue(fileName, out var meta);
                entries.Add(new AutoRuleEntry
                {
                    Id = "builtin:" + fileName.ToLowerInvariant(),
                    Name = meta?.Name ?? Path.GetFileNameWithoutExtension(fileName),
                    RulePath = file,
                    BuiltIn = true,
                    Enabled = false,
                    Condition = meta?.Condition ?? new AutoRuleCondition(),
                });
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"CommonRuleLibrary.Discover failed: {ex.Message}"); }
        return entries;
    }

    private static Dictionary<string, ManifestEntry> LoadManifest()
    {
        var map = new Dictionary<string, ManifestEntry>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var path = Path.Combine(LibraryDir, "common-rules.manifest.json");
            if (!File.Exists(path)) return map;
            var list = JsonSerializer.Deserialize<List<ManifestEntry>>(File.ReadAllText(path)) ?? new();
            foreach (var m in list)
                if (!string.IsNullOrWhiteSpace(m.File)) map[m.File] = m;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"CommonRuleLibrary.LoadManifest failed: {ex.Message}"); }
        return map;
    }

    private sealed class ManifestEntry
    {
        public string File { get; set; }
        public string Name { get; set; }
        public AutoRuleCondition Condition { get; set; }
    }
}
