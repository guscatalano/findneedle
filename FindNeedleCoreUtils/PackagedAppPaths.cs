using System;
using System.IO;

namespace FindNeedleCoreUtils;

/// <summary>
/// Provides correct file system paths for packaged (MSIX) and unpackaged apps.
/// Handles the path virtualization that occurs with MSIX packages.
/// </summary>
public static class PackagedAppPaths
{
    /// <summary>
    /// Gets the local application data directory.
    /// For both packaged and unpackaged apps, this returns the standard LocalAppData path.
    /// The file system virtualization layer will transparently redirect to the package-specific
    /// location (LocalCache\Local) for packaged apps.
    /// </summary>
    public static string LocalAppData => 
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>
    /// Gets the base directory for FindNeedle dependencies.
    /// </summary>
    public static string DependenciesBaseDir => 
        Path.Combine(LocalAppData, "FindNeedle", "Dependencies");

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
