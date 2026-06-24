using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using FindNeedleCoreUtils;

namespace FindNeedleUX.Services;

/// <summary>
/// Gathers the logs + config + system info we'd need to debug an issue into a single .zip the user can
/// send to the developer. Everything is best-effort: a missing or locked file is skipped, never fatal.
/// </summary>
public static class DiagnosticsBundle
{
    /// <summary>Suggested file name (timestamped) for the bundle.</summary>
    public static string SuggestedFileName() => $"FindNeedle-logs-{DateTime.Now:yyyyMMdd-HHmmss}";

    /// <summary>Build the zip at <paramref name="zipPath"/>. Returns the number of artifacts included.</summary>
    public static int Create(string zipPath)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);
        int count = 0;
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FindNeedle");

        // Small text/json artifacts written to %LocalAppData%\FindNeedle (skip the big SQLite caches).
        foreach (var name in new[]
        {
            "perf-log.txt", "viewer-settings.json", "auto-rules.json", "connections.json",
            "log-catalog.json", "online-sources.json", "last-search-report.json",
        })
        {
            if (TryAddFile(zip, Path.Combine(local, name), $"localappdata/{name}")) count++;
        }

        // The main app log (Logger writes here; appended-then-closed, so it's readable while running).
        try
        {
            var loggerFolder = FileIO.GetAppDataFindNeedlePluginFolder();
            if (TryAddFile(zip, Path.Combine(loggerFolder, "findneedle_log.txt"), "findneedle_log.txt")) count++;
        }
        catch { /* best-effort */ }

        // Plugin config next to the executable.
        if (TryAddFile(zip, Path.Combine(AppContext.BaseDirectory, "PluginConfig.json"), "PluginConfig.json")) count++;

        // Generated artifacts.
        if (AddText(zip, "system-info.txt", SafeSystemInfo())) count++;
        if (AddText(zip, "about.txt", AboutText())) count++;
        // The in-memory log cache catches lines even if the file write failed this session.
        try
        {
            var cache = string.Join(Environment.NewLine, FindNeedlePluginLib.Logger.Instance.LogCache);
            if (!string.IsNullOrEmpty(cache) && AddText(zip, "log-cache.txt", cache)) count++;
        }
        catch { /* best-effort */ }

        return count;
    }

    private static bool TryAddFile(ZipArchive zip, string path, string entryName)
    {
        try
        {
            if (!File.Exists(path)) return false;
            // Open with ReadWrite share so a file the app is actively appending to (the logs) still copies.
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using var es = entry.Open();
            fs.CopyTo(es);
            return true;
        }
        catch { return false; }
    }

    private static bool AddText(ZipArchive zip, string entryName, string content)
    {
        try
        {
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using var w = new StreamWriter(entry.Open(), Encoding.UTF8);
            w.Write(content ?? "");
            return true;
        }
        catch { return false; }
    }

    private static string SafeSystemInfo()
    {
        try { return SystemInfoMiddleware.GetPanelText(); }
        catch (Exception ex) { return "system info unavailable: " + ex.Message; }
    }

    private static string AboutText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Generated:     {DateTime.Now:O}");
        sb.AppendLine($"OS:            {Environment.OSVersion}");
        sb.AppendLine($"64-bit OS:     {Environment.Is64BitOperatingSystem}");
        sb.AppendLine($"Processors:    {Environment.ProcessorCount}");
        try { sb.AppendLine($"App dir:       {AppContext.BaseDirectory}"); } catch { }
        try { sb.AppendLine($"Exe:           {Environment.ProcessPath}"); } catch { }
        try
        {
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            sb.AppendLine($"Assembly:      {ver}");
        }
        catch { }
        try
        {
            var p = global::Windows.ApplicationModel.Package.Current?.Id?.Version;
            if (p != null) sb.AppendLine($"Package:       {p.Value.Major}.{p.Value.Minor}.{p.Value.Build}.{p.Value.Revision}");
        }
        catch { /* unpackaged — no package identity */ }
        return sb.ToString();
    }
}
