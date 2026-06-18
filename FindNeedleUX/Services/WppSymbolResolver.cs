using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using FindNeedleCoreUtils;
using Microsoft.Win32;

namespace FindNeedleUX.Services;

/// <summary>
/// Builds WPP TMF files from symbols (PDBs and/or binaries resolved through a symbol path/server)
/// using the WDK's <c>tracepdb</c>, into a managed cache that <see cref="TraceFormatConfig"/> puts on
/// <c>TRACE_FORMAT_SEARCH_PATH</c>. Two tracepdb modes are used:
///   • <c>tracepdb -f &lt;dir&gt;\*.pdb -s</c>     — extract TMFs from local PDB files.
///   • <c>tracepdb -i &lt;binary&gt; -r &lt;symPath&gt;</c> — resolve a binary's PDB via the symbol path
///     (which may be a symbol server, <c>srv*cache*https://…</c>) and extract its TMF.
/// </summary>
public static class WppSymbolResolver
{
    /// <summary>Managed folder where built TMFs land (always added to TRACE_FORMAT_SEARCH_PATH).</summary>
    public static string TmfCacheDir => Path.Combine(FileIO.GetAppDataFindNeedlePluginFolder(), "tmf-cache");

    public static string FindTracePdb()
    {
        try
        {
            var kits = (string)Registry.LocalMachine
                .OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots")?
                .GetValue("KitsRoot10");
            if (string.IsNullOrEmpty(kits)) return null;
            var bin = Path.Combine(kits, "bin");
            if (!Directory.Exists(bin)) return null;
            return Directory.GetFiles(bin, "tracepdb.exe", SearchOption.AllDirectories)
                .Where(p => p.Contains(@"\x64\", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p)
                .LastOrDefault();
        }
        catch { return null; }
    }

    /// <summary>
    /// Extract TMFs from the given source folders (PDBs, and binaries via <paramref name="symbolPath"/>)
    /// into the managed cache. Returns the total TMF count now in the cache and a diagnostic log.
    /// </summary>
    public static (int tmfCount, string log) BuildTmfs(string sourceFolders, string symbolPath)
    {
        var sb = new StringBuilder();
        var tracepdb = FindTracePdb();
        if (string.IsNullOrEmpty(tracepdb))
            return (CountTmfs(), "tracepdb.exe not found — install the Windows SDK/WDK (Debugging Tools).");
        sb.AppendLine($"tracepdb: {tracepdb}");

        var cache = TmfCacheDir;
        Directory.CreateDirectory(cache);

        var folders = (sourceFolders ?? "")
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        if (folders.Count == 0)
            sb.AppendLine("No PDB/binary source folder configured.");

        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder)) { sb.AppendLine($"skip (not found): {folder}"); continue; }

            // Local PDBs.
            Run(tracepdb, $"-f \"{Path.Combine(folder, "*.pdb")}\" -s -p \"{cache}\"", sb);

            // Binaries → resolve PDB via the symbol path (incl. symbol servers), then extract.
            if (!string.IsNullOrWhiteSpace(symbolPath))
            {
                foreach (var pattern in new[] { "*.dll", "*.exe", "*.sys" })
                    Run(tracepdb, $"-i \"{Path.Combine(folder, pattern)}\" -r \"{symbolPath}\" -p \"{cache}\"", sb);
            }
        }

        int count = CountTmfs();
        sb.AppendLine($"TMF cache now holds {count} file(s): {cache}");
        return (count, sb.ToString());
    }

    private static int CountTmfs()
    {
        try { return Directory.Exists(TmfCacheDir) ? Directory.GetFiles(TmfCacheDir, "*.tmf").Length : 0; }
        catch { return 0; }
    }

    private static void Run(string exe, string args, StringBuilder log)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            string outp = p.StandardOutput.ReadToEnd();
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            log.AppendLine($"> tracepdb {args}");
            if (!string.IsNullOrWhiteSpace(outp)) log.AppendLine(outp.Trim());
            if (!string.IsNullOrWhiteSpace(err)) log.AppendLine(err.Trim());
        }
        catch (Exception ex)
        {
            log.AppendLine($"tracepdb failed for [{args}]: {ex.Message}");
        }
    }
}
