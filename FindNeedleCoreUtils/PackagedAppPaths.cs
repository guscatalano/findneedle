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
    /// Gets the local application data directory.
    /// For both packaged and unpackaged apps, this returns the standard LocalAppData path.
    /// The file system virtualization layer will transparently redirect to the package-specific
    /// location (LocalCache\Local) for packaged apps.
    /// Overridden wholesale by <see cref="DataHomeEnvVar"/> for the new-user-preview profile.
    /// </summary>
    public static string LocalAppData =>
        DataHomeOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>Roaming AppData root (where the cached-search DBs + plugin cache live), honoring the
    /// new-user-preview override so those relocate into the throwaway profile too.</summary>
    public static string AppData =>
        DataHomeOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

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
