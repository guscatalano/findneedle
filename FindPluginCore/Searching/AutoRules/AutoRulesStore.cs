using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FindPluginCore.Searching.AutoRules;

/// <summary>
/// Persisted registry of auto-add rules, backed by
/// <c>%LocalAppData%\FindNeedle\auto-rules.json</c> (same convention as the viewer settings). Holds a
/// global on/off switch plus the user's entries. Built-in (bundled) rules are merged in on load from
/// <see cref="CommonRuleLibrary"/> so new library rules show up for existing installs (disabled by
/// default — the user opts in).
/// </summary>
public static class AutoRulesStore
{
    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FindNeedle", "auto-rules.json");

    private static string _path = DefaultPath;
    private static Data _data;
    private static Data D => _data ??= Load();

    // --- Test seam ---
    public static void SetStorageLocationForTests(string path) { _path = path; _data = null; }
    public static void ResetStorageForTests() { _path = DefaultPath; _data = null; }

    /// <summary>Raised after any mutation completes (so an open UI can refresh).</summary>
    public static event Action Changed;

    /// <summary>Master switch. When false, no rules are ever auto-added.</summary>
    public static bool Enabled
    {
        get => D.Enabled ?? true;
        set { D.Enabled = value; Save(); Changed?.Invoke(); }
    }

    /// <summary>A read-only snapshot of the entries (built-ins first, then user entries).</summary>
    public static IReadOnlyList<AutoRuleEntry> Entries => D.Entries.ToList();

    public static void Upsert(AutoRuleEntry entry)
    {
        if (entry == null) return;
        if (string.IsNullOrEmpty(entry.Id)) entry.Id = Guid.NewGuid().ToString("N");
        var idx = D.Entries.FindIndex(e => e.Id == entry.Id);
        if (idx >= 0) D.Entries[idx] = entry; else D.Entries.Add(entry);
        Save(); Changed?.Invoke();
    }

    public static void Remove(string id)
    {
        if (D.Entries.RemoveAll(e => e.Id == id) > 0) { Save(); Changed?.Invoke(); }
    }

    public static void SetEntryEnabled(string id, bool enabled)
    {
        var e = D.Entries.FirstOrDefault(x => x.Id == id);
        if (e != null && e.Enabled != enabled) { e.Enabled = enabled; Save(); Changed?.Invoke(); }
    }

    /// <summary>
    /// True if any enabled entry has a condition that needs scanned metadata (provider names or a
    /// build range). The search path uses this to decide whether the (more expensive) ETL metadata
    /// pre-scan is worth doing — when no such rule is on, auto-add stays purely path-based and instant.
    /// </summary>
    public static bool AnyEnabledNeedsMetadata() =>
        D.Entries.Any(e => e.Enabled && e.Condition != null
            && ((e.Condition.Providers is { Count: > 0 }) || e.Condition.MinBuild.HasValue || e.Condition.MaxBuild.HasValue));

    /// <summary>
    /// Resolve the rule paths to auto-add for the given context, honoring the master switch and a
    /// per-search opt-out. Missing files are dropped (a bundled rule may not be deployed yet).
    /// </summary>
    public static List<string> ResolveForSearch(AutoRuleContext context, bool skipForThisSearch = false)
    {
        if (skipForThisSearch || !Enabled) return new List<string>();
        return AutoRuleResolver.Resolve(D.Entries, context).Where(File.Exists).ToList();
    }

    private static Data Load()
    {
        Data d;
        try
        {
            d = File.Exists(_path)
                ? JsonSerializer.Deserialize<Data>(File.ReadAllText(_path)) ?? new Data()
                : new Data();
        }
        catch { d = new Data(); }

        // Built-in library rules are keyed by their stable Id (derived from the file name), NOT by the
        // absolute RulePath: the deployment dir changes between runs (bin\Debug vs bin\x64\Debug, dev
        // vs installed), and the user's enabled-state must survive that. So first collapse any duplicate
        // built-ins (same Id, different stale paths) — OR-ing their enabled flags so an enabled copy
        // wins — then for each shipped rule refresh the path to the current location, and add any
        // newly-shipped rules (disabled — the user opts in).
        bool dirty = false;

        var collapsed = new List<AutoRuleEntry>();
        var seenBuiltin = new Dictionary<string, AutoRuleEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in d.Entries)
        {
            if (e.BuiltIn && !string.IsNullOrEmpty(e.Id) && seenBuiltin.TryGetValue(e.Id, out var keep))
            {
                if (e.Enabled) keep.Enabled = true; // any enabled copy wins
                dirty = true;                       // dropping a stale duplicate
                continue;
            }
            if (e.BuiltIn && !string.IsNullOrEmpty(e.Id)) seenBuiltin[e.Id] = e;
            collapsed.Add(e);
        }
        if (collapsed.Count != d.Entries.Count) d.Entries = collapsed;

        var discovered = CommonRuleLibrary.Discover();

        // Drop stale built-ins that are no longer shipped (renamed, removed, or now Hidden helpers) so
        // they don't linger in the list or get auto-added by a dangling path.
        var liveIds = new HashSet<string>(discovered.Select(l => l.Id), StringComparer.OrdinalIgnoreCase);
        int removedStale = d.Entries.RemoveAll(e => e.BuiltIn && !string.IsNullOrEmpty(e.Id) && !liveIds.Contains(e.Id));
        if (removedStale > 0) dirty = true;

        foreach (var lib in discovered)
        {
            var existing = d.Entries.FirstOrDefault(e =>
                string.Equals(e.Id, lib.Id, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                // Re-point to the current deployment location; keep the user's enabled flag + condition.
                if (!string.Equals(existing.RulePath, lib.RulePath, StringComparison.OrdinalIgnoreCase))
                {
                    existing.RulePath = lib.RulePath;
                    dirty = true;
                }
                continue;
            }
            lib.Enabled = false;
            d.Entries.Add(lib);
            dirty = true;
        }
        if (dirty) TrySave(d);
        return d;
    }

    private static void Save() => TrySave(_data);

    private static void TrySave(Data d)
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_path, JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"AutoRulesStore.Save failed: {ex.Message}"); }
    }

    private sealed class Data
    {
        public bool? Enabled { get; set; }
        public List<AutoRuleEntry> Entries { get; set; } = new();
    }
}
