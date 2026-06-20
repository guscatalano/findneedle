using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FindNeedleUX.Services;

/// <summary>
/// Remembers the most-recently-added file/folder location paths so they can be re-added with one click.
/// File-backed under <c>%LocalAppData%\FindNeedle\</c> (works packaged or unpackaged). The cap is
/// user-configurable. A storage seam lets tests redirect to a temp file.
/// </summary>
public static class RecentLocationsStore
{
    public const int DefaultMax = 10;
    private const int HardMax = 50;

    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FindNeedle", "recent-locations.json");

    private static string _path = DefaultPath;

    // --- Test seam ---
    internal static void SetStorageLocationForTests(string path) => _path = path;
    internal static void ResetStorageForTests() => _path = DefaultPath;

    public static event Action Changed;

    /// <summary>How many recent paths to keep (1..50). Persisted; trims the list when lowered.</summary>
    public static int MaxRecent
    {
        get => Math.Clamp(Load().Max ?? DefaultMax, 1, HardMax);
        set
        {
            var d = Load();
            d.Max = Math.Clamp(value, 1, HardMax);
            if (d.Items.Count > d.Max.Value) d.Items = d.Items.Take(d.Max.Value).ToList();
            Save(d);
            Changed?.Invoke();
        }
    }

    /// <summary>Recent paths, most-recent first.</summary>
    public static List<string> Get() => Load().Items.ToList();

    /// <summary>Record a freshly-used path: move it to the front, de-duplicate, and trim to the cap.</summary>
    public static void Record(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var d = Load();
        d.Items.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        d.Items.Insert(0, path);
        var cap = Math.Clamp(d.Max ?? DefaultMax, 1, HardMax);
        if (d.Items.Count > cap) d.Items = d.Items.Take(cap).ToList();
        Save(d);
        Changed?.Invoke();
    }

    public static void Remove(string path)
    {
        var d = Load();
        if (d.Items.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0)
        { Save(d); Changed?.Invoke(); }
    }

    public static void Clear()
    {
        var d = Load();
        if (d.Items.Count > 0) { d.Items.Clear(); Save(d); Changed?.Invoke(); }
    }

    // --- storage ---
    private static Data Load()
    {
        try
        {
            if (!File.Exists(_path)) return new Data();
            return JsonSerializer.Deserialize<Data>(File.ReadAllText(_path)) ?? new Data();
        }
        catch { return new Data(); }
    }

    private static void Save(Data d)
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_path, JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"RecentLocationsStore.Save failed: {ex.Message}"); }
    }

    private sealed class Data
    {
        public int? Max { get; set; }
        public List<string> Items { get; set; } = new();
    }
}
