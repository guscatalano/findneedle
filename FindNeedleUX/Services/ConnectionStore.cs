using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace FindNeedleUX.Services;

/// <summary>A saved online-source connection: the reusable endpoint + credentials (no per-query data),
/// so the user enters org/repo/cluster + auth once and then just adds queries under it. Secrets are
/// stored DPAPI-encrypted (per-user).</summary>
public sealed class SavedConnection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "";   // "ado" | "github" | "kusto"
    public string Name { get; set; } = "";   // display label

    // ADO
    public string AdoOrg { get; set; } = "";
    public string AdoProject { get; set; } = "";
    public int AdoAuthMode { get; set; }     // 0 = PAT, 1 = AAD interactive
    public string AdoPatEnc { get; set; } = "";

    // GitHub
    public string GithubRepo { get; set; } = "";
    public string GithubTokenEnc { get; set; } = "";

    // Kusto
    public string KustoCluster { get; set; } = "";
    public string KustoDatabase { get; set; } = "";
    public int KustoAuthMode { get; set; }

    // --- secret accessors (plaintext in memory only) ---
    public string AdoPat { get => ConnectionStore.Unprotect(AdoPatEnc); set => AdoPatEnc = ConnectionStore.Protect(value); }
    public string GithubToken { get => ConnectionStore.Unprotect(GithubTokenEnc); set => GithubTokenEnc = ConnectionStore.Protect(value); }

    /// <summary>A reasonable default display name from the connection fields.</summary>
    public string DefaultName() => Kind switch
    {
        "ado"    => $"{ShortHost(AdoOrg)}/{AdoProject}".Trim('/'),
        "github" => GithubRepo,
        "kusto"  => $"{ShortHost(KustoCluster)}/{KustoDatabase}".Trim('/'),
        _        => Name,
    };

    private static string ShortHost(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        var s = url.Replace("https://", "").Replace("http://", "").Trim('/');
        return s;
    }
}

/// <summary>
/// Persisted registry of <see cref="SavedConnection"/>s, backed by a JSON file under
/// <c>%LocalAppData%\FindNeedle\</c> (works packaged or unpackaged). A storage seam lets tests
/// redirect to a temp file.
/// </summary>
public static class ConnectionStore
{
    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FindNeedle", "connections.json");

    private static string _path = DefaultPath;

    // --- Test seam ---
    internal static void SetStorageLocationForTests(string path) => _path = path;
    internal static void ResetStorageForTests() => _path = DefaultPath;

    public static event Action Changed;

    /// <summary>All saved connections (optionally filtered by kind), newest-saved order preserved.</summary>
    public static List<SavedConnection> GetAll(string kind = null)
    {
        var all = Load();
        return string.IsNullOrEmpty(kind)
            ? all
            : all.Where(c => string.Equals(c.Kind, kind, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public static SavedConnection GetById(string id) => Load().FirstOrDefault(c => c.Id == id);

    /// <summary>Insert or update a connection (by Id). Returns the stored instance.</summary>
    public static SavedConnection Upsert(SavedConnection conn)
    {
        if (conn == null) return null;
        if (string.IsNullOrEmpty(conn.Id)) conn.Id = Guid.NewGuid().ToString("N");
        if (string.IsNullOrWhiteSpace(conn.Name)) conn.Name = conn.DefaultName();

        var all = Load();
        var idx = all.FindIndex(c => c.Id == conn.Id);
        if (idx >= 0) all[idx] = conn; else all.Add(conn);
        Save(all);
        Changed?.Invoke();
        return conn;
    }

    public static void Remove(string id)
    {
        var all = Load();
        if (all.RemoveAll(c => c.Id == id) > 0) { Save(all); Changed?.Invoke(); }
    }

    // --- storage ---
    private static List<SavedConnection> Load()
    {
        try
        {
            if (!File.Exists(_path)) return new List<SavedConnection>();
            return JsonSerializer.Deserialize<List<SavedConnection>>(File.ReadAllText(_path))
                   ?? new List<SavedConnection>();
        }
        catch { return new List<SavedConnection>(); }
    }

    private static void Save(List<SavedConnection> all)
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_path, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"ConnectionStore.Save failed: {ex.Message}"); }
    }

    // --- DPAPI helpers (shared with SavedConnection's secret accessors) ---
    internal static string Protect(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        try { return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(s), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }

    internal static string Unprotect(string b64)
    {
        if (string.IsNullOrEmpty(b64)) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(b64), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }
}
