using System;
using System.IO;

namespace FindNeedleCoreUtils;

/// <summary>
/// Provides correct file system paths for packaged (MSIX) and unpackaged apps.
/// Handles the path virtualization that occurs with MSIX packages.
/// </summary>
public static class PackagedAppPaths
{
    /// <summary>Env var that, when set, relocates ALL of FindNeedle's per-user state (settings, cached
    /// searches, saved locations, catalog, symbols, dependencies, plugin cache) under the given folder.
    /// Used by the "Preview first-run (new user)" action to run a second instance against an empty,
    /// throwaway profile without touching the real one. Honored by both data roots.</summary>
    public const string DataHomeEnvVar = "FINDNEEDLE_DATA_HOME";

    internal static string? DataHomeOverride
    {
        get
        {
            try
            {
                var v = Environment.GetEnvironmentVariable(DataHomeEnvVar);
                return string.IsNullOrWhiteSpace(v) ? null : v;
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// Root for FindNeedle's per-user state (settings, catalogs, cached-search index, …).
    /// - PACKAGED (MSIX): the app's OWN persistent store (WinRT LocalState). We do NOT write to raw
    ///   <c>%LocalAppData%</c> and trust MSIX to virtualize it — that was unreliable and silently lost
    ///   settings (they landed in the real <c>%LocalAppData%</c> on some machines, nothing in LocalCache).
    ///   LocalState is guaranteed writable by the packaged app and survives updates.
    /// - UNPACKAGED (dev): the standard <c>%LocalAppData%</c> (WinRT ApplicationData is unavailable there).
    /// - Overridden wholesale by <see cref="DataHomeEnvVar"/> for the new-user-preview profile.
    /// Falls back to <c>%LocalAppData%</c> if the packaged store can't be resolved, so it can never be worse
    /// than the previous behavior.
    /// </summary>
    public static string LocalAppData
    {
        get
        {
            if (DataHomeOverride is { } home) return home;
            var packagedStore = PackageContextProviderFactory.Current.PackagedLocalStatePath;
            if (!string.IsNullOrEmpty(packagedStore)) return packagedStore;
            return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }
    }

    /// <summary>Roaming AppData root (where the cached-search DBs + plugin cache live), honoring the
    /// new-user-preview override so those relocate into the throwaway profile too.</summary>
    public static string AppData =>
        DataHomeOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>
    /// One-time migration: earlier packaged builds wrote per-user state to raw <c>%LocalAppData%\FindNeedle</c>
    /// (trusting MSIX virtualization, which didn't reliably persist). Now packaged state lives in the app's
    /// LocalState store (see <see cref="LocalAppData"/>). On first launch after the change, copy any existing
    /// settings JSON from the legacy location(s) into the store so users keep their settings. Best-effort and
    /// gap-filling only — never clobbers a file already present in the store. No-op when unpackaged.
    /// </summary>
    public static void MigrateLegacyPerUserState()
    {
        try
        {
            var provider = PackageContextProviderFactory.Current;
            var store = provider.PackagedLocalStatePath;
            if (string.IsNullOrEmpty(store)) return;   // unpackaged / store unavailable → nothing to migrate
            var newDir = Path.Combine(store, "FindNeedle");
            var realLocal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            MigrateJsonFrom(Path.Combine(realLocal, "FindNeedle"), newDir);
            if (!string.IsNullOrEmpty(provider.PackageFamilyName))
                MigrateJsonFrom(Path.Combine(realLocal, "Packages", provider.PackageFamilyName!,
                                             "LocalCache", "Local", "FindNeedle"), newDir);
        }
        catch { /* best-effort: never let migration break startup */ }
    }

    private static void MigrateJsonFrom(string oldDir, string newDir)
    {
        if (!Directory.Exists(oldDir) || string.Equals(oldDir, newDir, StringComparison.OrdinalIgnoreCase)) return;
        foreach (var src in Directory.EnumerateFiles(oldDir, "*.json", SearchOption.TopDirectoryOnly))
        {
            var dest = Path.Combine(newDir, Path.GetFileName(src));
            if (File.Exists(dest)) continue;   // store already has (newer) state — don't overwrite
            try { Directory.CreateDirectory(newDir); File.Copy(src, dest); } catch { }
        }
    }

    /// <summary>
    /// Gets the base directory for FindNeedle dependencies (Node/Mermaid, PlantUML/Java).
    /// </summary>
    /// <remarks>
    /// For a PACKAGED (MSIX) app the app's writes to <c>%LOCALAPPDATA%</c> are virtualized to the
    /// package's <c>LocalCache\Local</c>, but child processes the app spawns (npm/node for the Mermaid
    /// install) run OUTSIDE the package silo and write to the REAL <c>%LOCALAPPDATA%</c> — so the app
    /// would "install" a tool and then fail to find it ("installed but mmdc not found"). Resolve the
    /// EXPLICIT <c>…\Packages\{family}\LocalCache\Local\…</c> path (the same physical place the
    /// virtualization targets) so the app and the child processes agree on one real location.
    /// </remarks>
    public static string DependenciesBaseDir
    {
        get
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (DataHomeOverride == null && IsPackagedApp && !string.IsNullOrEmpty(PackageFamilyName))
                return Path.Combine(local, "Packages", PackageFamilyName!, "LocalCache", "Local",
                                    "FindNeedle", "Dependencies");
            return Path.Combine(LocalAppData, "FindNeedle", "Dependencies");
        }
    }

    /// <summary>
    /// Gets the directory for PlantUML dependencies (Java JRE and PlantUML JAR).
    /// </summary>
    public static string PlantUmlDir => 
        Path.Combine(DependenciesBaseDir, "PlantUML");

    /// <summary>
    /// Gets the directory for Mermaid CLI dependencies (Node.js and Mermaid).
    /// </summary>
    public static string MermaidDir => 
        Path.Combine(DependenciesBaseDir, "Mermaid");

    /// <summary>
    /// Gets the temp directory. This is NOT virtualized even for packaged apps.
    /// </summary>
    public static string TempDir => Path.GetTempPath();

    /// <summary>
    /// Gets a temp directory specific to FindNeedle operations.
    /// </summary>
    public static string FindNeedleTempDir => 
        Path.Combine(TempDir, "FindNeedle");

    /// <summary>
    /// Returns true if the app is running as a packaged MSIX app.
    /// </summary>
    public static bool IsPackagedApp => PackageContextProviderFactory.Current.IsPackagedApp;

    /// <summary>
    /// Gets the package family name if running as a packaged app, or null if unpackaged.
    /// </summary>
    public static string? PackageFamilyName => PackageContextProviderFactory.Current.PackageFamilyName;

    /// <summary>
    /// Ensures a directory exists, creating it if necessary.
    /// </summary>
    public static void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    /// <summary>
    /// Gets a unique temp file path for diagram generation.
    /// </summary>
    /// <param name="extension">File extension (e.g., ".puml", ".mmd")</param>
    /// <returns>Full path to a unique temp file</returns>
    public static string GetTempFilePath(string extension)
    {
        EnsureDirectoryExists(FindNeedleTempDir);
        var fileName = $"{Guid.NewGuid()}{extension}";
        return Path.Combine(FindNeedleTempDir, fileName);
    }

    /// <summary>
    /// Logs information about the current path configuration.
    /// Useful for debugging path virtualization issues.
    /// </summary>
    public static void LogPathInfo()
    {
        var info = $"IsPackagedApp: {IsPackagedApp}, PackageFamilyName: {PackageFamilyName ?? "null"}, LocalAppData: {LocalAppData}, DependenciesBaseDir: {DependenciesBaseDir}, PlantUmlDir: {PlantUmlDir}, MermaidDir: {MermaidDir}, TempDir: {TempDir}";
        System.Diagnostics.Debug.WriteLine($"[PackagedAppPaths] {info}");
    }
}
