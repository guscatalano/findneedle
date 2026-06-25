using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using FindNeedleCoreUtils;
using FindNeedlePluginLib;

namespace findneedle.Implementations;

/// <summary>
/// Extracts the archive container formats the folder scan understands (.zip and .cab) to a directory,
/// so their contents can be fed back through the normal file-extension processors. ZIP uses the
/// in-box <see cref="ZipFile"/>; CAB shells out to Windows' built-in <c>expand.exe</c> — chosen over a
/// managed CAB library because it ships with every Windows install (nothing to provision on a fresh
/// machine) and decodes every CAB compression (MSZIP / LZX / Quantum) that managed readers often can't.
/// Everything here is best-effort and never throws.
/// </summary>
public static class ArchiveExtractor
{
    private static readonly HashSet<string> ArchiveExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".zip", ".cab" };

    public static bool IsArchive(string? extension) =>
        !string.IsNullOrEmpty(extension) && ArchiveExtensions.Contains(extension);

    /// <summary>Extract <paramref name="archivePath"/> into <paramref name="destDir"/>. Returns true if
    /// extraction produced files; false (logged) on any failure — never throws.</summary>
    public static bool TryExtract(string archivePath, string destDir, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath)) return false;
            Directory.CreateDirectory(destDir);
            var ext = Path.GetExtension(archivePath).ToLowerInvariant();
            if (ext == ".zip")
            {
                ZipFile.ExtractToDirectory(archivePath, destDir, overwriteFiles: true);
                return true;
            }
            if (ext == ".cab")
                return ExpandCab(archivePath, destDir, cancellationToken);
            return false;
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"ArchiveExtractor: failed to extract {archivePath}: {ex.Message}");
            return false;
        }
    }

    // Extract every member of a cab into destDir. `-F:* <cab> <dir>` is the standard "all members"
    // form and works for real (FCI-built) cabs; a few oddly-built single-member cabs ignore it, so we
    // fall back to `-I <cab> <dir>` (flatten). The `-F:*` flag must come BEFORE the cab path.
    private static bool ExpandCab(string cabPath, string destDir, CancellationToken cancellationToken)
    {
        var cabName = Path.GetFileName(cabPath);
        RunExpand(new[] { "-F:*", cabPath, destDir }, cabPath, cancellationToken);
        PruneStrayCopy(destDir, cabName);
        if (HasFiles(destDir)) return true;
        RunExpand(new[] { "-I", cabPath, destDir }, cabPath, cancellationToken);
        PruneStrayCopy(destDir, cabName);
        return HasFiles(destDir);
    }

    // expand -F:* on some single-member cabs copies the cab into the destination instead of extracting
    // it; drop that stray copy so it isn't counted as content (or re-expanded as a nested archive).
    private static void PruneStrayCopy(string destDir, string cabName)
    {
        try
        {
            var stray = Path.Combine(destDir, cabName);
            if (File.Exists(stray)) File.Delete(stray);
        }
        catch { /* best-effort */ }
    }

    private static void RunExpand(string[] args, string cabPath, CancellationToken cancellationToken)
    {
        var expand = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "expand.exe");
        if (!File.Exists(expand)) expand = "expand.exe"; // fall back to PATH

        var psi = new ProcessStartInfo
        {
            FileName = expand,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi);
        if (proc == null) return;
        // Drain stdout/stderr asynchronously so a full pipe can't deadlock the child.
        var stdout = proc.StandardOutput.ReadToEndAsync();
        var stderr = proc.StandardError.ReadToEndAsync();
        while (!proc.WaitForExit(200))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return;
            }
        }
        if (proc.ExitCode != 0)
            Logger.Instance.Log($"ArchiveExtractor: expand.exe exit {proc.ExitCode} for {cabPath}: {SafeResult(stderr)}");
    }

    private static bool HasFiles(string dir) =>
        Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any();

    private static string SafeResult(System.Threading.Tasks.Task<string> t)
    {
        try { return t.GetAwaiter().GetResult().Trim(); } catch { return ""; }
    }
}
