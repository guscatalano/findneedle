using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace findneedle.ETWPlugin;

/// <summary>
/// Reads metadata out of an .etl trace (no tracefmt needed — uses the TraceEvent library, so it
/// works for kernel, manifest, and TraceLogging/self-describing traces). Powers the Settings →
/// "Inspect ETL" action.
/// </summary>
public class EtlInfoExtractor
{
    public static List<string> GetProviders(string etlPath)
    {
        if (!File.Exists(etlPath))
            throw new FileNotFoundException($"ETL file not found: {etlPath}");
        var providers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var traceLog = TraceLog.OpenOrConvert(etlPath);
        foreach (var ev in traceLog.Events)
        {
            if (!string.IsNullOrEmpty(ev.ProviderName))
                providers.Add(ev.ProviderName);
        }
        return providers.ToList();
    }

    public static Dictionary<string, string> GetSystemInfo(string etlPath)
    {
        if (!File.Exists(etlPath))
            throw new FileNotFoundException($"ETL file not found: {etlPath}");
        var sysInfo = new Dictionary<string, string>();
        using var traceLog = TraceLog.OpenOrConvert(etlPath);
        foreach (var ev in traceLog.Events)
        {
            // Look for system/build info events
            if (ev.ProviderName.Contains("SystemConfig", StringComparison.OrdinalIgnoreCase) ||
                ev.EventName.Contains("Build", StringComparison.OrdinalIgnoreCase) ||
                ev.ProviderName.Contains("Kernel", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(ev.FormattedMessage))
                {
                    sysInfo[$"{ev.ProviderName} {ev.EventName} {ev.ProviderGuid}"] = ev.FormattedMessage;
                }
            }
        }
        return sysInfo;
    }

    /// <summary>
    /// Full "what's in this .etl?" summary: OS/Windows build, machine shape, capture window, event
    /// counts, a manifest/kernel format breakdown, and the providers present (with per-provider
    /// counts). Reads the ETW header for the cheap fields and a single pass over the events.
    /// </summary>
    public static EtlInfo Inspect(string etlPath)
    {
        if (string.IsNullOrEmpty(etlPath) || !File.Exists(etlPath))
            throw new FileNotFoundException($"ETL file not found: {etlPath}");

        var info = new EtlInfo { FilePath = etlPath, FileSizeBytes = new FileInfo(etlPath).Length };

        using var source = new ETWTraceEventSource(etlPath);

        // Header fields — available without walking every event.
        info.OsVersion = source.OSVersion?.ToString() ?? string.Empty;
        info.PointerSizeBits = source.PointerSize * 8;
        info.NumberOfProcessors = source.NumberOfProcessors;
        info.CpuSpeedMHz = source.CpuSpeedMHz;
        info.SessionStartTime = source.SessionStartTime;
        info.SessionEndTime = source.SessionEndTime;
        info.SessionDuration = source.SessionDuration;

        int total = 0, kernel = 0, manifest = 0, buildNumber = 0;

        // AllEvents = every dispatched event (total + provider tally). Kernel.All / Dynamic.All fire
        // additionally for their subsets (own counters), giving the format breakdown without
        // double-counting the total.
        source.AllEvents += e =>
        {
            total++;
            var p = e.ProviderName;
            if (!string.IsNullOrEmpty(p))
                info.Providers[p] = info.Providers.TryGetValue(p, out var c) ? c + 1 : 1;

            // The OS build number lives in the ETW header event's ProviderVersion (the header's
            // OSVersion is often just 10.0.0.0). Read it once. (BuildLabEx / branch like
            // "rs_prerelease" is a registry value and is NOT present in ETW traces.)
            if (buildNumber == 0 && p != null
                && p.Equals("Windows Kernel", StringComparison.OrdinalIgnoreCase)
                && e.EventName.IndexOf("EventTrace", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                try
                {
                    var pv = e.PayloadByName("ProviderVersion");
                    if (pv != null) buildNumber = Convert.ToInt32(pv);
                }
                catch { /* not this event */ }
            }
        };
        source.Kernel.All += _ => kernel++;
        source.Dynamic.All += _ => manifest++;

        source.Process();

        info.EventCount = total;
        info.KernelEventCount = kernel;
        info.ManifestOrTraceLoggingEventCount = manifest;
        info.EventsLost = source.EventsLost;

        // Prefer the real build number from the header event; the source.OSVersion build field is
        // often 0. Compose major.minor.build so the report shows e.g. 10.0.26100.
        info.BuildNumber = buildNumber;
        var osv = source.OSVersion;
        if (osv != null && buildNumber > 0 && osv.Build <= 0)
            info.OsVersion = $"{osv.Major}.{osv.Minor}.{buildNumber}";

        // The full BuildLabEx (e.g. "26100.3194.amd64fre.ge_release.240331-1435") isn't a decoded
        // event field — it's a string inside a kernel SystemConfig/header blob — so the event walk
        // above can't see it. Recover it with a raw byte scan of the file.
        info.BuildLab = FindBuildLab(etlPath);
        if (!string.IsNullOrEmpty(info.BuildLab))
        {
            // build.rev.archfre.branch.date-time  →  branch is the 4th dot-segment.
            var parts = info.BuildLab.Split('.');
            if (parts.Length >= 4) info.Branch = parts[3];
        }

        info.Providers = info.Providers
            .OrderByDescending(kv => kv.Value)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        return info;
    }

    // BuildLabEx, e.g. "26100.3194.amd64fre.ge_release.240331-1435".
    private static readonly Regex BuildLabRx =
        new(@"\d{4,6}\.\d+\.[a-z0-9]*fre\.[A-Za-z0-9_]+\.\d{6}-\d{4}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Raw-scan the .etl bytes for the BuildLabEx string (stored as UTF-16 in an undecoded kernel
    /// blob). For files larger than the cap, scans the head + tail (where the header / config
    /// rundown live). Returns "" if not present. Never throws.
    /// </summary>
    public static string FindBuildLab(string etlPath)
    {
        try
        {
            const long Cap = 96L * 1024 * 1024; // bound memory for large traces
            var len = new FileInfo(etlPath).Length;
            byte[] bytes;
            if (len <= Cap)
            {
                bytes = File.ReadAllBytes(etlPath);
            }
            else
            {
                int half = (int)(Cap / 2);
                bytes = new byte[half * 2];
                using var fs = File.OpenRead(etlPath);
                ReadFully(fs, bytes, 0, half);
                fs.Seek(-half, SeekOrigin.End);
                ReadFully(fs, bytes, half, half);
            }
            // Most Windows strings (incl. BuildLabEx in the trace) are UTF-16; check ASCII too.
            foreach (var enc in new[] { Encoding.Unicode, Encoding.ASCII })
            {
                var m = BuildLabRx.Match(enc.GetString(bytes));
                if (m.Success) return m.Value;
            }
        }
        catch { /* best-effort */ }
        return string.Empty;
    }

    private static void ReadFully(Stream s, byte[] buf, int offset, int count)
    {
        int read = 0;
        while (read < count)
        {
            int n = s.Read(buf, offset + read, count - read);
            if (n <= 0) break;
            read += n;
        }
    }

    /// <summary>Multi-line human-readable report (used by the UI/CLI inspector).</summary>
    public static string Format(EtlInfo info)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"File:        {Path.GetFileName(info.FilePath)} ({info.FileSizeBytes / 1024.0 / 1024.0:F2} MB)");
        sb.AppendLine($"Windows:     {info.OsVersion}   ({info.PointerSizeBits}-bit, {info.NumberOfProcessors} CPUs"
                      + (info.CpuSpeedMHz > 0 ? $" @ {info.CpuSpeedMHz} MHz)" : ")"));
        if (!string.IsNullOrEmpty(info.BuildLab))
            sb.AppendLine($"Build lab:   {info.BuildLab}   (branch: {info.Branch})");
        sb.AppendLine($"Captured:    {info.SessionStartTime:yyyy-MM-dd HH:mm:ss} → {info.SessionEndTime:HH:mm:ss}  (duration {info.SessionDuration})");
        sb.AppendLine($"Events:      {info.EventCount:N0}  (lost {info.EventsLost:N0})");
        sb.AppendLine($"Format:      {info.FormatSummary}");
        sb.AppendLine($"Providers:   {info.Providers.Count}");
        foreach (var kv in info.Providers)
            sb.AppendLine($"   {kv.Value,10:N0}  {kv.Key}");
        return sb.ToString();
    }

    // ----- exports (for "copy as" in the inspector) -----

    /// <summary>Plain-text report (same as <see cref="Format"/>).</summary>
    public static string ToPlainText(EtlInfo info) => Format(info);

    public static string ToJson(EtlInfo info)
        => JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });

    public static string ToXml(EtlInfo info)
    {
        var providers = new XElement("Providers",
            info.Providers.Select(kv =>
                new XElement("Provider", new XAttribute("name", kv.Key), new XAttribute("count", kv.Value))));
        var root = new XElement("EtlInfo",
            new XElement("FilePath", info.FilePath),
            new XElement("FileSizeBytes", info.FileSizeBytes),
            new XElement("OsVersion", info.OsVersion),
            new XElement("BuildNumber", info.BuildNumber),
            new XElement("BuildLab", info.BuildLab),
            new XElement("Branch", info.Branch),
            new XElement("PointerSizeBits", info.PointerSizeBits),
            new XElement("NumberOfProcessors", info.NumberOfProcessors),
            new XElement("CpuSpeedMHz", info.CpuSpeedMHz),
            new XElement("SessionStartTime", info.SessionStartTime.ToString("o")),
            new XElement("SessionEndTime", info.SessionEndTime.ToString("o")),
            new XElement("SessionDuration", info.SessionDuration.ToString()),
            new XElement("EventCount", info.EventCount),
            new XElement("EventsLost", info.EventsLost),
            new XElement("KernelEventCount", info.KernelEventCount),
            new XElement("ManifestOrTraceLoggingEventCount", info.ManifestOrTraceLoggingEventCount),
            new XElement("FormatSummary", info.FormatSummary),
            providers);
        return new XDocument(new XDeclaration("1.0", "utf-8", null), root).ToString();
    }

    public static string ToCsv(EtlInfo info)
    {
        static string Q(string s) => "\"" + (s ?? string.Empty).Replace("\"", "\"\"") + "\"";
        var sb = new StringBuilder();
        sb.AppendLine("Field,Value");
        sb.AppendLine($"File,{Q(Path.GetFileName(info.FilePath))}");
        sb.AppendLine($"FileSizeBytes,{info.FileSizeBytes}");
        sb.AppendLine($"OsVersion,{Q(info.OsVersion)}");
        sb.AppendLine($"BuildNumber,{info.BuildNumber}");
        sb.AppendLine($"BuildLab,{Q(info.BuildLab)}");
        sb.AppendLine($"Branch,{Q(info.Branch)}");
        sb.AppendLine($"PointerSizeBits,{info.PointerSizeBits}");
        sb.AppendLine($"NumberOfProcessors,{info.NumberOfProcessors}");
        sb.AppendLine($"CpuSpeedMHz,{info.CpuSpeedMHz}");
        sb.AppendLine($"SessionStartTime,{Q(info.SessionStartTime.ToString("o"))}");
        sb.AppendLine($"SessionEndTime,{Q(info.SessionEndTime.ToString("o"))}");
        sb.AppendLine($"SessionDuration,{Q(info.SessionDuration.ToString())}");
        sb.AppendLine($"EventCount,{info.EventCount}");
        sb.AppendLine($"EventsLost,{info.EventsLost}");
        sb.AppendLine($"KernelEventCount,{info.KernelEventCount}");
        sb.AppendLine($"ManifestOrTraceLoggingEventCount,{info.ManifestOrTraceLoggingEventCount}");
        sb.AppendLine($"FormatSummary,{Q(info.FormatSummary)}");
        sb.AppendLine();
        sb.AppendLine("Provider,Count");
        foreach (var kv in info.Providers)
            sb.AppendLine($"{Q(kv.Key)},{kv.Value}");
        return sb.ToString();
    }
}

/// <summary>A quick "what's in this .etl?" summary produced by <see cref="EtlInfoExtractor.Inspect"/>.</summary>
public sealed class EtlInfo
{
    public string FilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }

    public string OsVersion { get; set; } = string.Empty;     // e.g. "10.0.26100" — the Windows build
    public int BuildNumber { get; set; }                      // OS build from the ETW header (e.g. 26100); 0 if absent
    public string BuildLab { get; set; } = string.Empty;      // full BuildLabEx, e.g. "26100.3194.amd64fre.ge_release.240331-1435"
    public string Branch { get; set; } = string.Empty;        // branch from BuildLabEx, e.g. "ge_release" / "rs_prerelease"
    public int PointerSizeBits { get; set; }                  // 32 or 64
    public int NumberOfProcessors { get; set; }
    public int CpuSpeedMHz { get; set; }

    public DateTime SessionStartTime { get; set; }
    public DateTime SessionEndTime { get; set; }
    public TimeSpan SessionDuration { get; set; }

    public int EventCount { get; set; }
    public int EventsLost { get; set; }

    // Format breakdown — which decode path claimed each event.
    public int KernelEventCount { get; set; }                 // NT kernel logger events
    public int ManifestOrTraceLoggingEventCount { get; set; } // manifest + self-describing (EventSource/TraceLogging)

    /// <summary>Provider name → event count, most frequent first.</summary>
    public Dictionary<string, int> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>One-line human summary of the dominant format(s).</summary>
    public string FormatSummary
    {
        get
        {
            var parts = new List<string>();
            if (KernelEventCount > 0) parts.Add($"kernel ({KernelEventCount:N0})");
            if (ManifestOrTraceLoggingEventCount > 0) parts.Add($"manifest/TraceLogging ({ManifestOrTraceLoggingEventCount:N0})");
            int other = EventCount - KernelEventCount - ManifestOrTraceLoggingEventCount;
            if (other > 0) parts.Add($"other/WPP ({other:N0})");
            return parts.Count == 0 ? "(no events)" : string.Join(", ", parts);
        }
    }
}
