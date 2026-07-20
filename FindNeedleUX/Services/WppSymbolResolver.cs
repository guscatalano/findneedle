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
/// Builds WPP TMF files from symbols into a managed cache that <see cref="TraceFormatConfig"/> puts
/// on <c>TRACE_FORMAT_SEARCH_PATH</c>. PDB DISCOVERY is done in managed code (issue #4): each
/// binary's expected PDB name + GUID + age is read from its PE debug directory
/// (<see cref="WppSymbols.PdbIdentity"/>) and resolved through loose folders and the user's symbol
/// path by <see cref="WppSymbols.PdbResolver"/> (symstore/SSQP conventions: two-tier stores,
/// file.ptr, compressed .pd_, HTTP servers), with every probe logged. The WDK's <c>tracepdb</c> is
/// then used ONLY as the TMF extractor (<c>-f &lt;resolved.pdb&gt;</c>) — its opaque <c>-i</c>
/// resolution mode is no longer used. Loose PDBs that mismatch a binary's GUID/age are rejected
/// loudly, never silently extracted.
/// </summary>
public static class WppSymbolResolver
{
    /// <summary>Managed folder where built TMFs land (always added to TRACE_FORMAT_SEARCH_PATH).</summary>
    public static string TmfCacheDir => Path.Combine(FileIO.GetAppDataFindNeedlePluginFolder(), "tmf-cache");

    /// <summary>Managed store (symstore layout) where server-resolved PDBs land when the user's
    /// symbol path has no local cache element of its own.</summary>
    public static string PdbCacheDir => Path.Combine(FileIO.GetAppDataFindNeedlePluginFolder(), "pdb-cache");

    // --- Test seams (only the unit-test assembly sees these, via InternalsVisibleTo) ---
    // Let orchestration tests run BuildTmfs's discovery/skip/extract decisions on a machine (CI)
    // with no WDK: fake the tracepdb location and capture invocations instead of spawning it.
    internal static Func<string> FindTracePdbOverride;
    internal static Action<string, string, StringBuilder> RunTracePdbOverride;
    internal static void ResetOverridesForTests() { FindTracePdbOverride = null; RunTracePdbOverride = null; }

    public static string FindTracePdb()
    {
        if (FindTracePdbOverride != null) return FindTracePdbOverride();
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

        var resolver = new WppSymbols.PdbResolver();
        // PDB paths already extracted (or rejected as stale) this run — so the loose-PDB sweep
        // below neither re-extracts a resolved PDB nor touches one that failed verification.
        var extracted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rejected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder)) { sb.AppendLine($"skip (not found): {folder}"); continue; }

            // 1) Binaries: read the exact PDB identity from the PE, resolve it ourselves (loose
            //    folders first, then the symbol path), then let tracepdb extract from the match.
            foreach (var pattern in new[] { "*.dll", "*.exe", "*.sys" })
            {
                foreach (var binary in SafeEnumerate(folder, pattern))
                {
                    var id = WppSymbols.PdbIdentity.TryReadFromBinary(binary, out var idErr);
                    if (id == null)
                    {
                        sb.AppendLine($"skip {Path.GetFileName(binary)}: {idErr}");
                        continue;
                    }
                    sb.AppendLine($"{Path.GetFileName(binary)} needs: {id} (key {id.Key})");
                    var res = resolver.Resolve(id, folders, symbolPath, PdbCacheDir, sb);
                    foreach (var r in res.RejectedLooseCandidates) rejected.Add(r);
                    if (!res.Found)
                    {
                        sb.AppendLine($"FAILED to resolve {id.PdbFileName} for {Path.GetFileName(binary)} — probes above show every location tried.");
                        continue;
                    }
                    if (extracted.Add(res.ResolvedPath))
                    {
                        // Snapshot-diff (not count-based) so re-extracting a PDB whose TMFs are
                        // already cached — same files rewritten, count unchanged — doesn't misfire.
                        var beforeTmfs = SnapshotTmfs(cache);
                        Run(tracepdb, $"-f \"{res.ResolvedPath}\" -p \"{cache}\"", sb);
                        // The most confusing failure mode is "everything resolved, still no TMFs".
                        // Per Microsoft's docs, WPP trace-format data is STRIPPED from public
                        // symbols (tracepdb needs the full/private PDB); since Win8 a component can
                        // opt individual trace functions into its public PDB, but most don't.
                        // A GUID+age match can't tell the two apart — a stripped PDB keeps the
                        // private one's identity — so explain it here, at extraction time.
                        if (!AnyTmfChanged(cache, beforeTmfs))
                            sb.AppendLine(
                                $"note: {id.PdbFileName} matched (GUID+age) but produced no TMFs — it carries no " +
                                "WPP trace-format data. Either the binary doesn't use WPP, or this is a " +
                                "public/stripped PDB (symbol servers like msdl strip TMF data; WPP decoding " +
                                "needs the component's private PDB).");
                    }
                }
            }

            // 2) Loose PDBs with no binary alongside (the "folder of PDBs" workflow) — extracted
            //    as before, EXCEPT files a binary's GUID/age check explicitly rejected.
            foreach (var pdb in SafeEnumerate(folder, "*.pdb", recurse: true))
            {
                if (extracted.Contains(pdb)) continue;
                if (rejected.Contains(pdb))
                {
                    sb.AppendLine($"skip (stale — rejected by GUID/age check above): {pdb}");
                    continue;
                }
                if (extracted.Add(pdb))
                    Run(tracepdb, $"-f \"{pdb}\" -p \"{cache}\"", sb);
            }
        }

        int count = CountTmfs();
        sb.AppendLine($"TMF cache now holds {count} file(s): {cache}");
        return (count, sb.ToString());
    }

    private static IEnumerable<string> SafeEnumerate(string folder, string pattern, bool recurse = false)
    {
        try
        {
            return Directory.EnumerateFiles(folder, pattern,
                recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).ToList();
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }

    /// <summary>Per-file write times of the cache's TMFs, taken right before an extraction so
    /// <see cref="AnyTmfChanged"/> can tell whether THAT extraction wrote anything (a rewrite of an
    /// already-cached TMF bumps its timestamp; a wall-clock window would false-positive on files
    /// another recent run just wrote).</summary>
    private static Dictionary<string, DateTime> SnapshotTmfs(string cacheDir)
    {
        var snap = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var f in Directory.EnumerateFiles(cacheDir, "*.tmf"))
                snap[f] = File.GetLastWriteTimeUtc(f);
        }
        catch { /* unreadable cache — empty snapshot */ }
        return snap;
    }

    private static bool AnyTmfChanged(string cacheDir, Dictionary<string, DateTime> before)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(cacheDir, "*.tmf"))
                if (!before.TryGetValue(f, out var prev) || File.GetLastWriteTimeUtc(f) != prev)
                    return true;
        }
        catch { /* unreadable cache — treat as nothing written */ }
        return false;
    }

    private static int CountTmfs()
    {
        try { return Directory.Exists(TmfCacheDir) ? Directory.GetFiles(TmfCacheDir, "*.tmf").Length : 0; }
        catch { return 0; }
    }

    private static void Run(string exe, string args, StringBuilder log)
    {
        if (RunTracePdbOverride != null)
        {
            log.AppendLine($"> tracepdb {args}");
            RunTracePdbOverride(exe, args, log);
            return;
        }
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
