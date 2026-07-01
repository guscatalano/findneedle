using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FindNeedleUX.Services;

/// <summary>
/// Remembers the last values entered in the online-source (Kusto / ADO / GitHub) add dialogs so they
/// pre-fill next time. Backed by %LocalAppData%\FindNeedle\online-sources.json. Secrets (PAT / token)
/// are encrypted with Windows DPAPI (per-user) rather than stored in plaintext.
/// </summary>
public static class OnlineSourceSettings
{
    private static readonly string Path_ = Path.Combine(
        FindNeedleCoreUtils.PackagedAppPaths.LocalAppData,
        "FindNeedle", "online-sources.json");

    private static Data _data;
    private static Data D => _data ??= Load();

    // ----- ADO -----
    public static string AdoOrg { get => D.AdoOrg ?? ""; set { D.AdoOrg = value; Save(); } }
    public static string AdoProject { get => D.AdoProject ?? ""; set { D.AdoProject = value; Save(); } }
    public static int AdoAuthMode { get => D.AdoAuthMode ?? 0; set { D.AdoAuthMode = value; Save(); } }
    public static string AdoPat { get => Unprotect(D.AdoPatEnc); set { D.AdoPatEnc = Protect(value); Save(); } }
    public static string AdoWiql { get => D.AdoWiql ?? ""; set { D.AdoWiql = value; Save(); } }
    public static string AdoIds { get => D.AdoIds ?? ""; set { D.AdoIds = value; Save(); } }
    public static bool AdoOpenAttachments { get => D.AdoOpenAttachments ?? false; set { D.AdoOpenAttachments = value; Save(); } }

    // ----- GitHub -----
    public static string GithubRepo { get => D.GithubRepo ?? ""; set { D.GithubRepo = value; Save(); } }
    public static string GithubToken { get => Unprotect(D.GithubTokenEnc); set { D.GithubTokenEnc = Protect(value); Save(); } }
    public static string GithubState { get => D.GithubState ?? "all"; set { D.GithubState = value; Save(); } }
    public static string GithubIssue { get => D.GithubIssue ?? ""; set { D.GithubIssue = value; Save(); } }

    private static string Protect(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        try { return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(s), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }

    private static string Unprotect(string b64)
    {
        if (string.IsNullOrEmpty(b64)) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(b64), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }

    private static Data Load()
    {
        try { if (File.Exists(Path_)) return JsonSerializer.Deserialize<Data>(File.ReadAllText(Path_)) ?? new Data(); }
        catch { }
        return new Data();
    }

    private static void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(Path_);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(Path_, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"OnlineSourceSettings.Save failed: {ex.Message}"); }
    }

    private sealed class Data
    {
        public string AdoOrg { get; set; }
        public string AdoProject { get; set; }
        public int? AdoAuthMode { get; set; }
        public string AdoPatEnc { get; set; }
        public string AdoWiql { get; set; }
        public string AdoIds { get; set; }
        public bool? AdoOpenAttachments { get; set; }
        public string GithubRepo { get; set; }
        public string GithubTokenEnc { get; set; }
        public string GithubState { get; set; }
        public string GithubIssue { get; set; }
    }
}
