using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using FindNeedleCoreUtils;

namespace FindPluginCore.Diagnostics;

/// <summary>
/// Builds a human-readable (Markdown) performance report from the data the app already collects:
/// the machine spec, the most recent search pipeline run (<see cref="PerfReport"/>), slow UX
/// interactions (the app's <c>ux-slow.log</c>), and the on-disk cache/temp footprint.
///
/// Lives here (not in the WinUI app) so both consumers can use it: the in-app "Export performance
/// report" button, and the perf-test lane — which drives the app from a separate process and passes
/// its own benchmark numbers. Everything except the optional benchmarks is read from disk, so it works
/// cross-process.
/// </summary>
public static class PerformanceReport
{
    /// <summary>Render the full report. <paramref name="benchmarks"/> (label,value) rows are included as
    /// a "Benchmarks" section when supplied (the perf-test lane passes its measured numbers); the in-app
    /// button passes none.</summary>
    public static string GenerateMarkdown(IEnumerable<(string label, string value)> benchmarks = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# FindNeedle performance report");
        sb.AppendLine();
        sb.AppendLine($"_Generated {DateTime.Now:yyyy-MM-dd HH:mm:ss}_");
        sb.AppendLine();

        AppendMachineSpec(sb);
        if (benchmarks != null) AppendBenchmarks(sb, benchmarks);
        AppendSearchPipeline(sb);
        AppendSlowUx(sb);
        AppendStorageCache(sb);
        return sb.ToString();
    }

    /// <summary>Write the report next to the perf log (or <paramref name="directory"/>) and return its path.</summary>
    public static string Save(string directory = null, IEnumerable<(string label, string value)> benchmarks = null)
    {
        directory ??= Path.GetDirectoryName(PerfLog.FilePath);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"performance-report-{DateTime.Now:yyyyMMdd-HHmmss}.md");
        File.WriteAllText(path, GenerateMarkdown(benchmarks));
        return path;
    }

    // ----- sections -----

    private static void AppendMachineSpec(StringBuilder sb)
    {
        sb.AppendLine("## Machine");
        sb.AppendLine();
        sb.AppendLine("| | |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Machine | {Environment.MachineName} |");
        sb.AppendLine($"| OS | {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}) |");
        sb.AppendLine($"| CPU | {CpuName()} · {Environment.ProcessorCount} logical cores |");
        sb.AppendLine($"| RAM | {TotalPhysicalGb():N1} GB |");
        sb.AppendLine($"| Runtime | {RuntimeInformation.FrameworkDescription} |");
        sb.AppendLine($"| App | {AppVersion()} |");
        sb.AppendLine();
    }

    private static void AppendBenchmarks(StringBuilder sb, IEnumerable<(string label, string value)> benchmarks)
    {
        sb.AppendLine("## Benchmarks");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|---|---|");
        foreach (var (label, value) in benchmarks)
            sb.AppendLine($"| {label} | {value} |");
        sb.AppendLine();
    }

    private static void AppendSearchPipeline(StringBuilder sb)
    {
        sb.AppendLine("## Most recent search");
        sb.AppendLine();
        // Last is in-memory (in-app button); LoadPersisted reads the app's on-disk report (so the
        // perf-test lane, which drives the app from another process, still gets the pipeline section).
        var r = PerfReport.Last ?? PerfReport.LoadPersisted();
        if (r == null) { sb.AppendLine("_No search has run yet._"); sb.AppendLine(); return; }

        if (!string.IsNullOrEmpty(r.Source)) sb.AppendLine($"**Source:** `{r.Source}`  ");
        sb.AppendLine($"**Total:** {r.TotalMs:N0} ms (search {r.SearchMs:N0} ms + viewer {r.ViewerMs:N0} ms)  ");
        sb.AppendLine($"**Storage:** {r.StorageType} ({r.StorageMode}" +
                      (r.StorageEstimatedRows >= 0 ? $", estimate {r.StorageEstimatedRows:N0} rows" : "") + ")  ");
        sb.AppendLine($"**Rows:** {r.StoredRows:N0} stored" + (r.RawRows > 0 ? $" ({r.RawRows:N0} raw)" : "") + "  ");
        sb.AppendLine($"**Cache:** {(r.CacheHit ? "hit — scan skipped"
                                   : r.CacheWritten ? "miss — written for next time"
                                   : "not cached" + (string.IsNullOrEmpty(r.CacheWriteSkipReason) ? "" : $" ({r.CacheWriteSkipReason})"))}  ");
        sb.AppendLine($"**FTS index built:** {(r.FtsIndexBuilt ? "yes" : "no")}  ");
        sb.AppendLine();
        sb.AppendLine("**Where the time went**");
        sb.AppendLine();
        sb.AppendLine("| ms | phase |");
        sb.AppendLine("|---:|---|");
        foreach (var p in r.TopPhases())
            sb.AppendLine($"| {p.ElapsedMs:N0} | {p.Name} |");
        sb.AppendLine();
        sb.AppendLine("**Why**");
        sb.AppendLine();
        foreach (var h in r.BuildHints())
            sb.AppendLine($"- {h}");
        sb.AppendLine();
    }

    private static void AppendSlowUx(StringBuilder sb)
    {
        sb.AppendLine("## Slow UX interactions");
        sb.AppendLine();
        var recs = ReadSlowLog();
        if (recs.Count == 0)
        {
            sb.AppendLine("_None recorded — every interaction stayed under the app's threshold._");
            sb.AppendLine();
            return;
        }
        sb.AppendLine("| When (UTC) | Interaction | ms | Sub-phase | Rows | Page | Storage |");
        sb.AppendLine("|---|---|---:|---|---:|---:|---|");
        foreach (var r in recs.OrderByDescending(x => x.LatencyMs).Take(25))
        {
            var sub = r.ScopeChain != null && r.ScopeChain.Count > 0 ? r.ScopeChain[^1] : "";
            var c = r.Conditions;
            sb.AppendLine($"| {r.Timestamp} | {r.Interaction} | {r.LatencyMs:N0} | {sub} | " +
                          $"{(c?.TotalRows ?? 0):N0} | {(c?.PageSize ?? 0)} | {c?.StorageTier} |");
        }
        sb.AppendLine();
    }

    private static void AppendStorageCache(StringBuilder sb)
    {
        sb.AppendLine("## Storage & cache footprint");
        sb.AppendLine();
        var (files, bytes) = CachedStorage.GetCacheStats();
        long capGb = CachedStorage.DefaultMaxCacheBytes / 1024 / 1024 / 1024;
        sb.AppendLine($"- Result cache: {files:N0} file(s), {bytes / 1024.0 / 1024.0:N0} MB (eviction cap {capGb} GB)");
        try
        {
            var dirs = Directory.GetDirectories(Path.GetTempPath(), "FindNeedleTemp_*");
            sb.AppendLine($"- Temp extraction sessions: {dirs.Length:N0} dir(s)");
        }
        catch { /* temp not enumerable — skip */ }
        sb.AppendLine();
    }

    // ----- helpers -----

    /// <summary>Parse the app's <c>ux-slow.log</c> (JSONL, written next to the perf log).</summary>
    private static List<UxSlowEntry> ReadSlowLog()
    {
        var list = new List<UxSlowEntry>();
        try
        {
            var dir = Path.GetDirectoryName(PerfLog.FilePath);
            if (string.IsNullOrEmpty(dir)) return list;
            var path = Path.Combine(dir, "ux-slow.log");
            if (!File.Exists(path)) return list;
            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try { var e = JsonSerializer.Deserialize<UxSlowEntry>(line); if (e != null) list.Add(e); }
                catch { /* skip a malformed line */ }
            }
        }
        catch { /* best-effort */ }
        return list;
    }

    private static string CpuName()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return k?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "unknown";
        }
        catch { return "unknown"; }
    }

    private static double TotalPhysicalGb()
    {
        try
        {
            var m = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(m)) return m.ullTotalPhys / 1024.0 / 1024.0 / 1024.0;
        }
        catch { /* ignore */ }
        return 0;
    }

    private static string AppVersion()
    {
        try
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(asm.Location);
            return string.IsNullOrEmpty(info.FileVersion) ? asm.GetName().Version?.ToString() ?? "?" : info.FileVersion;
        }
        catch { return "?"; }
    }

    // Local DTOs mirroring the app's ux-slow.log records (we don't reference the WinUI app).
    private sealed class UxSlowEntry
    {
        public string Timestamp { get; set; }
        public string Interaction { get; set; }
        public long LatencyMs { get; set; }
        public List<string> ScopeChain { get; set; }
        public UxCond Conditions { get; set; }
    }

    private sealed class UxCond
    {
        public int TotalRows { get; set; }
        public int PageSize { get; set; }
        public string StorageTier { get; set; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private sealed class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);
}
