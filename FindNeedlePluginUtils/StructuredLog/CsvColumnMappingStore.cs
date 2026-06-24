using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FindNeedlePluginUtils.StructuredLog;

/// <summary>
/// Persists user-chosen CSV column→field mappings, keyed by the file's column set (lowercased,
/// order-preserved). So once you map a CSV's columns, any CSV with the same columns — including a
/// cache-reuse reopen of the same file — maps the same way. Backed by
/// %LocalAppData%\FindNeedle\csv-mappings.json. Best-effort IO: a corrupt/missing file starts empty
/// and never throws to callers.
/// </summary>
public static class CsvColumnMappingStore
{
    private static readonly object _gate = new();
    private static Dictionary<string, Dictionary<string, string>>? _cache;

    private static string _path = DefaultPath();
    private static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FindNeedle", "csv-mappings.json");

    // Test seam: point persistence at a temp file and drop the cache so the next access reloads.
    public static void SetStorageLocationForTests(string path) { lock (_gate) { _path = path; _cache = null; } }
    public static void ResetStorageForTests() { lock (_gate) { _path = DefaultPath(); _cache = null; } }

    // Stable key for a column set: lowercased header names, order preserved, unit-separated (
    // can't appear in a real header, so distinct column sets never collide).
    private static string Key(IReadOnlyList<string> headers)
        => string.Join("", headers.Select(h => (h ?? string.Empty).ToLowerInvariant()));

    /// <summary>The saved header→field map for this column set, or null if none.</summary>
    public static Dictionary<string, string>? Get(IReadOnlyList<string> headers)
    {
        if (headers == null || headers.Count == 0) return null;
        lock (_gate)
        {
            Load();
            return _cache!.TryGetValue(Key(headers), out var m) ? new Dictionary<string, string>(m) : null;
        }
    }

    public static bool Has(IReadOnlyList<string> headers)
    {
        if (headers == null || headers.Count == 0) return false;
        lock (_gate) { Load(); return _cache!.ContainsKey(Key(headers)); }
    }

    public static void Set(IReadOnlyList<string> headers, IReadOnlyDictionary<string, string> map)
    {
        if (headers == null || headers.Count == 0 || map == null) return;
        lock (_gate)
        {
            Load();
            _cache![Key(headers)] = new Dictionary<string, string>(map);
            Save();
        }
    }

    private static void Load()
    {
        if (_cache != null) return;
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                _cache = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json) ?? new();
                return;
            }
        }
        catch { /* corrupt file → start fresh */ }
        _cache = new();
    }

    private static void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_path, JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best-effort persistence */ }
    }
}
