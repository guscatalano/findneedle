using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FindNeedleCoreUtils;
using FindNeedlePluginLib;

namespace findneedle.Implementations;

/// <summary>A logger whose in-memory buffers were saved out of a dump to an .etl.</summary>
public sealed class SavedLogger
{
    public string EtlPath { get; set; } = "";
    public int LoggerId { get; set; }
    public string LoggerName { get; set; } = "";
}

/// <summary>Result of pulling ETW trace buffers out of a crash dump.</summary>
public sealed class DumpEtwExtractionResult
{
    public string CdbPath { get; set; } = "";
    /// <summary>Saved loggers (non-empty .etl + the logger id/name parsed from strdump).</summary>
    public List<SavedLogger> Loggers { get; } = new();
    /// <summary>Just the .etl paths, for callers that don't need the names.</summary>
    public List<string> EtlFiles => Loggers.Select(l => l.EtlPath).ToList();
    /// <summary>Full cdb console output (!wmitrace.strdump listing + logsave narration).</summary>
    public string CdbOutput { get; set; } = "";

    /// <summary>True when cdb's wmitrace extension is OLDER than the target OS build, so it misparses the
    /// trace-buffer layout ("Unrecognized EtwpDebuggerData Version" / "Invalid State" / "Update your
    /// debugger"). Loggers still ENUMERATE (names parse), but buffers won't decode — the fix is a newer
    /// Debugging Tools for Windows (>= the dump's OS build).</summary>
    public bool LikelyToolVersionMismatch =>
        CdbOutput.Contains("Unrecognized EtwpDebuggerData Version", StringComparison.OrdinalIgnoreCase)
        || CdbOutput.Contains("Update your debugger", StringComparison.OrdinalIgnoreCase)
        || CdbOutput.Contains("Invalid State", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Extracts ETW logs from a WinDbg crash dump by orchestrating cdb.exe (console WinDbg). ETW logger buffers
/// live in (kernel) memory, so a kernel/complete dump holds the recent tail of the in-flight buffers. We run
/// the debugger's <c>!wmitrace</c> extension to enumerate loggers (<c>strdump</c>) and save each logger's
/// buffers to a real .etl (<c>logsave</c>) — which then decodes through the normal .etl pipeline. Mirrors
/// <see cref="ArchiveExtractor"/> (a container that the folder scan expands and re-feeds).
///
/// Caveats: needs Debugging Tools for Windows (cdb.exe); kernel/complete dump only; you get the circular-
/// buffer TAIL resident at crash time, not the full history; cdb must be >= the dump's OS build or its
/// wmitrace misparses the buffers (see <see cref="DumpEtwExtractionResult.LikelyToolVersionMismatch"/>);
/// symbols are fetched from the MS symbol server. Everything here is best-effort and never throws from
/// <see cref="TryExtract"/>.
/// </summary>
public static class DumpEtwExtractor
{
    public static bool IsDump(string? extension) =>
        !string.IsNullOrEmpty(extension) && extension.Equals(".dmp", StringComparison.OrdinalIgnoreCase);

    /// <summary>The most recent extraction's user-facing diagnostic (set by <see cref="TryExtract"/>), e.g.
    /// "cdb not found" or the version-mismatch hint — so the UI/logs can explain an empty result. Null = ok.</summary>
    public static string? LastDiagnostic { get; private set; }

    /// <summary>Locate cdb.exe: FINDNEEDLE_CDB override, the Windows Kits debuggers folder, then PATH.</summary>
    public static string? FindCdb()
    {
        var env = Environment.GetEnvironmentVariable("FINDNEEDLE_CDB");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;

        foreach (var pf in new[]
                 {
                     Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                     Environment.GetEnvironmentVariable("ProgramFiles"),
                 })
        {
            if (string.IsNullOrEmpty(pf)) continue;
            var c = Path.Combine(pf, "Windows Kits", "10", "Debuggers", "x64", "cdb.exe");
            if (File.Exists(c)) return c;
        }

        try
        {
            var psi = new ProcessStartInfo("where", "cdb.exe")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            var o = p!.StandardOutput.ReadToEnd();
            p.WaitForExit();
            var first = o.Split('\n').Select(s => s.Trim()).FirstOrDefault(File.Exists);
            if (first != null) return first;
        }
        catch { /* not on PATH */ }
        return null;
    }

    /// <summary>FolderLocation hook (mirrors <see cref="ArchiveExtractor.TryExtract"/>): expand a dump's ETW
    /// logger buffers into <paramref name="destDir"/> as one .etl per logger, named after the logger. Returns
    /// true if any .etl was produced; sets <see cref="LastDiagnostic"/> and logs (never throws) otherwise.</summary>
    public static bool TryExtract(string dumpPath, string destDir, System.Threading.CancellationToken cancellationToken = default)
    {
        LastDiagnostic = null;
        try
        {
            if (FindCdb() == null)
            {
                LastDiagnostic = "Can't read ETW logs from this .dmp: cdb.exe (Debugging Tools for Windows) was not found. Install it or set FINDNEEDLE_CDB.";
                Logger.Instance.Log("DumpEtwExtractor: " + LastDiagnostic);
                return false;
            }
            var res = ExtractToEtls(dumpPath, destDir, cancellationToken: cancellationToken);
            if (res.Loggers.Count > 0) return true;

            LastDiagnostic = res.LikelyToolVersionMismatch
                ? "Read the dump's ETW loggers but couldn't decode their buffers: the debugger's wmitrace extension is older than this dump's OS build (\"Update your debugger\"). Install Debugging Tools for Windows >= the dump's OS, or set FINDNEEDLE_CDB to a newer cdb.exe."
                : "No ETW logger buffers were found in this .dmp. ETW buffers live in kernel memory — a kernel/complete dump is required (a user-mode minidump has none).";
            Logger.Instance.Log("DumpEtwExtractor: " + LastDiagnostic);
            return false;
        }
        catch (Exception ex)
        {
            LastDiagnostic = "Failed to extract ETW logs from the .dmp: " + ex.Message;
            Logger.Instance.Log("DumpEtwExtractor: " + LastDiagnostic);
            return false;
        }
    }

    /// <summary>
    /// Save the dump's ETW logger buffers to .etl files under <paramref name="outDir"/> (a fresh temp dir if
    /// null), named after each logger (from strdump). Enumerates with !wmitrace.strdump and tries
    /// !wmitrace.logsave for logger ids 0..maxLoggerId (invalid ids just error harmlessly).
    /// </summary>
    public static DumpEtwExtractionResult ExtractToEtls(string dumpPath, string? outDir = null,
        System.Threading.CancellationToken cancellationToken = default, int maxLoggerId = 63, int timeoutMs = 600_000)
    {
        if (!File.Exists(dumpPath)) throw new FileNotFoundException("dump not found", dumpPath);
        var cdb = FindCdb()
                  ?? throw new FileNotFoundException(
                      "cdb.exe not found — install Debugging Tools for Windows or set FINDNEEDLE_CDB.");

        outDir ??= Path.Combine(Path.GetTempPath(), "fn_dmp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        var symCache = Path.Combine(Path.GetTempPath(), "fn_symbols");
        Directory.CreateDirectory(symCache);

        // cdb command script: fix symbols → reload → list loggers → save each logger's buffers → quit.
        // NOTE: !wmitrace parses the LoggerId arg as HEX, so emit ids in hex to probe 0..maxLoggerId
        // contiguously (a decimal loop would skip 10-15, 26-31, …); the saved file keeps the DECIMAL id so it
        // maps back to strdump's "Logger Id 0xNN".
        var sb = new StringBuilder();
        sb.AppendLine(".symfix " + symCache);
        sb.AppendLine(".reload /f");
        sb.AppendLine("!wmitrace.strdump");
        for (int id = 0; id <= maxLoggerId; id++)
            sb.AppendLine($"!wmitrace.logsave {id:x} {Path.Combine(outDir, $"logger_{id}.etl")}");
        sb.AppendLine("q");
        var scriptPath = Path.Combine(outDir, "extract.cdbscript");
        File.WriteAllText(scriptPath, sb.ToString());

        var result = new DumpEtwExtractionResult { CdbPath = cdb };
        var psi2 = new ProcessStartInfo
        {
            FileName = cdb,
            Arguments = $"-z \"{dumpPath}\" -cf \"{scriptPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        var outSb = new StringBuilder();
        using (var p = Process.Start(psi2))
        {
            if (p == null) throw new Exception("failed to start cdb");
            p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (outSb) outSb.AppendLine(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (outSb) outSb.AppendLine(e.Data); };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } throw new TimeoutException("cdb timed out"); }
            p.WaitForExit();
        }
        result.CdbOutput = outSb.ToString();

        var idToName = ParseLoggerNames(result.CdbOutput);

        // Keep the .etl files cdb actually wrote (non-empty), name them after the logger, and record id+name.
        foreach (var f in Directory.GetFiles(outDir, "logger_*.etl").OrderBy(f => f))
        {
            try
            {
                if (new FileInfo(f).Length <= 0) { File.Delete(f); continue; }
                int id = ParseIdFromFileName(f);
                string name = idToName.TryGetValue(id, out var n) ? n : $"logger {id}";
                var finalPath = RenameToLoggerName(f, name, id);
                result.Loggers.Add(new SavedLogger { EtlPath = finalPath, LoggerId = id, LoggerName = name });
            }
            catch { /* ignore a single bad file */ }
        }
        result.Loggers.Sort((a, b) => a.LoggerId.CompareTo(b.LoggerId));
        return result;
    }

    // strdump lines look like:  "    Logger Id 0x02 @ 0xFFFF... Named 'Circular Kernel Context Logger'"
    private static readonly Regex LoggerLine =
        new(@"Logger\s+Id\s+0x([0-9a-fA-F]+).*?Named\s+'(.*?)'", RegexOptions.Compiled);

    private static Dictionary<int, string> ParseLoggerNames(string cdbOutput)
    {
        var map = new Dictionary<int, string>();
        foreach (Match m in LoggerLine.Matches(cdbOutput ?? ""))
        {
            if (int.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out var id))
                map[id] = m.Groups[2].Value.Trim();
        }
        return map;
    }

    private static int ParseIdFromFileName(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path); // logger_<decimalid>
        var us = stem.LastIndexOf('_');
        return us >= 0 && int.TryParse(stem.Substring(us + 1), out var id) ? id : -1;
    }

    // Rename logger_<id>.etl → "<LoggerName>.etl" so the viewer's file/source shows the logger name. Sanitizes
    // invalid filename chars and disambiguates a name collision with the logger id.
    private static string RenameToLoggerName(string path, string loggerName, int id)
    {
        var safe = string.Join("_", loggerName.Split(Path.GetInvalidFileNameChars())).Trim();
        if (string.IsNullOrEmpty(safe)) safe = $"logger {id}";
        if (safe.Length > 120) safe = safe.Substring(0, 120);
        var dir = Path.GetDirectoryName(path)!;
        var target = Path.Combine(dir, safe + ".etl");
        if (File.Exists(target)) target = Path.Combine(dir, $"{safe} (logger {id}).etl");
        try { if (!string.Equals(target, path, StringComparison.OrdinalIgnoreCase)) { File.Delete(target); File.Move(path, target); return target; } }
        catch { return path; } // keep the original name if rename fails
        return target;
    }
}
