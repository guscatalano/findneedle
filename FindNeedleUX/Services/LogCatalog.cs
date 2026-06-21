using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FindNeedleUX.Services;

/// <summary>One entry in the log finder: a named app/component and where its logs live. The path may
/// contain environment variables (e.g. %SystemRoot%); <see cref="ExpandedPath"/> resolves them.</summary>
public sealed class LogCatalogEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Kind { get; set; } = "folder"; // "folder" | "file"
    public string Description { get; set; } = "";
    public bool BuiltIn { get; set; }

    public bool IsFolder => string.Equals(Kind, "folder", StringComparison.OrdinalIgnoreCase);
    public string ExpandedPath => Environment.ExpandEnvironmentVariables(Path ?? "");
    public bool Exists
    {
        get { try { var p = ExpandedPath; return IsFolder ? Directory.Exists(p) : File.Exists(p); } catch { return false; } }
    }
}

/// <summary>
/// The "log finder": a catalog of apps/components and where their logs live. Built-in entries (common
/// Windows log locations) are merged with the user's own. Persisted to a JSON file under
/// <c>%LocalAppData%\FindNeedle\</c>. A storage seam lets tests redirect to a temp file.
/// </summary>
public static class LogCatalog
{
    private static readonly string DefaultPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FindNeedle", "log-catalog.json");

    private static string _path = DefaultPath;

    internal static void SetStorageLocationForTests(string path) => _path = path;
    internal static void ResetStorageForTests() => _path = DefaultPath;

    public static event Action Changed;

    /// <summary>Common Windows log locations, shipped so the finder is useful out of the box. Paths use
    /// environment variables so they resolve per-machine.</summary>
    public static readonly IReadOnlyList<LogCatalogEntry> BuiltIns = new[]
    {
        new LogCatalogEntry { Id = "builtin:winevt", Name = "Windows Event Logs (.evtx)", Kind = "folder",
            Path = @"%SystemRoot%\System32\winevt\Logs", Description = "Saved event log files", BuiltIn = true },
        new LogCatalogEntry { Id = "builtin:cbs", Name = "Windows Servicing (CBS)", Kind = "folder",
            Path = @"%SystemRoot%\Logs\CBS", Description = "Component-based servicing logs", BuiltIn = true },
        new LogCatalogEntry { Id = "builtin:dism", Name = "DISM", Kind = "folder",
            Path = @"%SystemRoot%\Logs\DISM", Description = "Deployment Image Servicing logs", BuiltIn = true },
        new LogCatalogEntry { Id = "builtin:panther", Name = "Windows Setup (Panther)", Kind = "folder",
            Path = @"%SystemRoot%\Panther", Description = "Setup / upgrade logs", BuiltIn = true },
        new LogCatalogEntry { Id = "builtin:wu", Name = "Windows Update (ETL)", Kind = "folder",
            Path = @"%SystemRoot%\Logs\WindowsUpdate", Description = "Windows Update trace logs (.etl)", BuiltIn = true },
        new LogCatalogEntry { Id = "builtin:defender", Name = "Microsoft Defender", Kind = "folder",
            Path = @"%ProgramData%\Microsoft\Windows Defender\Support", Description = "Defender support logs", BuiltIn = true },
        new LogCatalogEntry { Id = "builtin:temp", Name = "User Temp", Kind = "folder",
            Path = @"%TEMP%", Description = "Many apps drop logs here", BuiltIn = true },
    };

    /// <summary>All entries: built-ins (re-pathed from the shipped list) first, then user entries.</summary>
    public static List<LogCatalogEntry> GetAll()
    {
        var data = Load();
        // Built-ins are authoritative for path/name/description (so upgrades fix them); only their
        // presence is implied. User entries (BuiltIn=false) come from the file as-is.
        var userEntries = data.Where(e => !e.BuiltIn).ToList();
        return BuiltIns.Select(Clone).Concat(userEntries).ToList();
    }

    public static LogCatalogEntry GetById(string id) => GetAll().FirstOrDefault(e => e.Id == id);

    /// <summary>Add or update a user entry (built-ins aren't editable). Returns the stored entry.</summary>
    public static LogCatalogEntry Upsert(LogCatalogEntry entry)
    {
        if (entry == null) return null;
        entry.BuiltIn = false; // users can only manage their own entries
        if (string.IsNullOrEmpty(entry.Id) || entry.Id.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase))
            entry.Id = Guid.NewGuid().ToString("N");

        var data = Load().Where(e => !e.BuiltIn).ToList();
        var idx = data.FindIndex(e => e.Id == entry.Id);
        if (idx >= 0) data[idx] = entry; else data.Add(entry);
        Save(data);
        Changed?.Invoke();
        return entry;
    }

    public static void Remove(string id)
    {
        var data = Load().Where(e => !e.BuiltIn).ToList();
        if (data.RemoveAll(e => e.Id == id) > 0) { Save(data); Changed?.Invoke(); }
    }

    private static LogCatalogEntry Clone(LogCatalogEntry e) => new()
    {
        Id = e.Id, Name = e.Name, Path = e.Path, Kind = e.Kind, Description = e.Description, BuiltIn = e.BuiltIn,
    };

    private static List<LogCatalogEntry> Load()
    {
        try
        {
            if (!File.Exists(_path)) return new List<LogCatalogEntry>();
            return JsonSerializer.Deserialize<List<LogCatalogEntry>>(File.ReadAllText(_path)) ?? new List<LogCatalogEntry>();
        }
        catch { return new List<LogCatalogEntry>(); }
    }

    private static void Save(List<LogCatalogEntry> userEntries)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_path, JsonSerializer.Serialize(userEntries, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"LogCatalog.Save failed: {ex.Message}"); }
    }
}
