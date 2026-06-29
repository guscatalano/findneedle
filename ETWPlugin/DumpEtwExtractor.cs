using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace findneedle.ETWPlugin;

/// <summary>Result of pulling ETW trace buffers out of a crash dump.</summary>
public sealed class DumpEtwExtractionResult
{
    public string CdbPath { get; set; } = "";
    /// <summary>The .etl files written from the dump's in-memory logger buffers (non-empty).</summary>
    public List<string> EtlFiles { get; } = new();
    /// <summary>Full cdb console output (the !wmitrace.strdump logger listing + logsave narration).</summary>
    public string CdbOutput { get; set; } = "";

    /// <summary>True when cdb's wmitrace extension is OLDER than the target OS build, so it misparses the
    /// trace-buffer layout ("Unrecognized EtwpDebuggerData Version" / "Invalid State" / "Update your
    /// debugger"). Loggers still ENUMERATE (names parse), but buffers won't decode — the fix is a newer
    /// Debugging Tools for Windows (>= the dump's OS build). Distinguishes "toolchain too old" from "broken".</summary>
    public bool LikelyToolVersionMismatch =>
        CdbOutput.Contains("Unrecognized EtwpDebuggerData Version", StringComparison.OrdinalIgnoreCase)
        || CdbOutput.Contains("Update your debugger", StringComparison.OrdinalIgnoreCase)
        || CdbOutput.Contains("Invalid State", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// PROTOTYPE: extract ETW logs from a WinDbg crash dump by orchestrating cdb.exe (console WinDbg) against it.
/// ETW logger buffers live in (kernel) memory, so a kernel/complete dump captures the recent tail of the
/// in-flight trace buffers. We run the debugger's <c>!wmitrace</c> extension to (a) enumerate the loggers
/// (<c>!wmitrace.strdump</c>) and (b) save each logger's buffers to a real .etl (<c>!wmitrace.logsave</c>),
/// which then decodes through FindNeedle's normal ETL pipeline (<see cref="findneedle.Implementations.FileExtensions.ETLProcessor"/>).
///
/// Caveats: needs Debugging Tools for Windows (cdb.exe); works on KERNEL/complete dumps (a user-mode minidump
/// usually has no logger buffers); you get the circular-buffer TAIL that was resident at crash time, not the
/// full history; <c>!wmitrace</c> wants symbols (we point cdb at the MS symbol server with a local cache).
/// </summary>
public static class DumpEtwExtractor
{
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

    /// <summary>
    /// Save the dump's ETW logger buffers to .etl files under <paramref name="outDir"/> (a fresh temp dir if
    /// null). Enumerates with !wmitrace.strdump and tries !wmitrace.logsave for logger ids 0..maxLoggerId
    /// (invalid ids just error harmlessly), keeping the .etl files that were actually written.
    /// </summary>
    public static DumpEtwExtractionResult ExtractToEtls(string dumpPath, string? outDir = null,
        int maxLoggerId = 63, int timeoutMs = 600_000)
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
        var sb = new StringBuilder();
        sb.AppendLine(".symfix " + symCache);
        sb.AppendLine(".reload /f");
        sb.AppendLine("!wmitrace.strdump");
        // NOTE: !wmitrace parses the LoggerId argument as HEX (e.g. "55" → 0x55 = 85), so emit the id in hex
        // to probe logger ids 0..maxLoggerId contiguously — a decimal loop would skip ids 10-15, 26-31, ….
        for (int id = 0; id <= maxLoggerId; id++)
        {
            var etl = Path.Combine(outDir, $"logger_{id}.etl");
            sb.AppendLine($"!wmitrace.logsave {id:x} {etl}");
        }
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

        // Keep the logger .etl files cdb actually wrote (non-empty). logsave on an absent logger id writes
        // nothing; on a present-but-empty logger it may write only a header — let the caller's decode decide
        // usefulness, but drop zero-byte files here.
        foreach (var f in Directory.GetFiles(outDir, "logger_*.etl").OrderBy(f => f))
        {
            try
            {
                if (new FileInfo(f).Length > 0) result.EtlFiles.Add(f);
                else File.Delete(f);
            }
            catch { /* ignore */ }
        }
        return result;
    }
}
