using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using findneedle.Wpp;
using Microsoft.Diagnostics.Tracing;

namespace ETWPluginTests;

/// <summary>
/// Calibration probe (NOT a normal test): dumps how TraceEvent surfaces raw WPP events from a real capture,
/// so the managed wire-reader can be calibrated (which GUID carries the message id, where the message number
/// is, where the arg blob starts). Gated on env vars so it only runs when pointed at a capture; writes a
/// human-readable dump to FN_WPP_PROBE_OUT.
/// </summary>
[TestClass]
public sealed class ManagedWppProbe
{
    [TestMethod]
    [TestCategory("SkipCI")]
    public void Probe_DumpRawWppEvents()
    {
        var etl = Environment.GetEnvironmentVariable("FN_WPP_PROBE_ETL");
        var outPath = Environment.GetEnvironmentVariable("FN_WPP_PROBE_OUT");
        if (string.IsNullOrEmpty(etl) || !File.Exists(etl) || string.IsNullOrEmpty(outPath))
            Assert.Inconclusive("set FN_WPP_PROBE_ETL + FN_WPP_PROBE_OUT to run the probe");

        var sb = new StringBuilder();
        int n = 0;
        using (var source = new ETWTraceEventSource(etl))
        {
            source.AllEvents += ev =>
            {
                if (n++ >= 20) { source.StopProcessing(); return; }
                sb.AppendLine($"#{n} provider='{ev.ProviderName}' providerGuid={ev.ProviderGuid} taskGuid={ev.TaskGuid}");
                sb.AppendLine($"    id={(int)ev.ID} opcode={(int)ev.Opcode} task={(int)ev.Task} version={ev.Version} level={(int)ev.Level} dataLen={ev.EventDataLength}");
                sb.AppendLine($"    formattedMessage='{Safe(() => ev.FormattedMessage)}' eventName='{ev.EventName}'");
                var data = ev.EventData();
                sb.AppendLine("    bytes=" + BitConverter.ToString(data));
            };
            source.Process();
        }
        File.WriteAllText(outPath, sb.ToString());
        Assert.IsTrue(n > 0, "no events read from the capture");
    }

    private static string Safe(Func<string> f)
    {
        try { return f() ?? ""; } catch (Exception e) { return "<" + e.GetType().Name + ">"; }
    }

    /// <summary>
    /// Throw a real, broad capture at the managed decoder and report coverage + robustness: how many events,
    /// how many are WPP-shaped (classic/unhandled with a task GUID), how many distinct message GUIDs, how many
    /// DECODE against the loaded TMFs, and whether the reader survives real varied wire data without throwing.
    /// Gated: FN_WPP_COVER_ETL (the .etl), FN_WPP_COVER_OUT (report path), FN_WPP_TMF_DIR (optional ';'-list of
    /// TMF dirs to try to actually decode with).
    /// </summary>
    [TestMethod]
    [TestCategory("SkipCI")]
    public void Probe_CoverageReport()
    {
        var etl = Environment.GetEnvironmentVariable("FN_WPP_COVER_ETL");
        var outPath = Environment.GetEnvironmentVariable("FN_WPP_COVER_OUT");
        if (string.IsNullOrEmpty(etl) || !File.Exists(etl) || string.IsNullOrEmpty(outPath))
            Assert.Inconclusive("set FN_WPP_COVER_ETL + FN_WPP_COVER_OUT");

        var tmf = new TmfDatabase();
        var tmfDirs = (Environment.GetEnvironmentVariable("FN_WPP_TMF_DIR") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var d in tmfDirs)
            try { foreach (var f in Directory.EnumerateFiles(d, "*.tmf", SearchOption.AllDirectories)) tmf.AddFile(f); }
            catch { /* skip an unreadable dir */ }

        long total = 0, handledDynamic = 0, handledKernel = 0, unhandled = 0;
        long wppShaped = 0, decodeAttempts = 0, decoded = 0, decodeExceptions = 0;
        var byGuid = new Dictionary<Guid, long>();
        var decodedSamples = new List<string>();
        int pointerSize = 8;

        using (var source = new ETWTraceEventSource(etl))
        {
            pointerSize = source.PointerSize > 0 ? source.PointerSize : 8;
            source.Dynamic.All += _ => handledDynamic++;
            source.Kernel.All += _ => handledKernel++;
            source.UnhandledEvents += ev =>
            {
                unhandled++;
                var g = ev.TaskGuid;
                if (g == Guid.Empty) return;
                wppShaped++;
                byGuid[g] = byGuid.TryGetValue(g, out var c) ? c + 1 : 1;

                if (tmf.TryGet(g, (int)ev.ID, out var entry))
                {
                    decodeAttempts++;
                    try
                    {
                        var msg = WppMessageFormatter.Format(entry, ev.EventData(), pointerSize);
                        decoded++;
                        if (decodedSamples.Count < 25) decodedSamples.Add($"[{g}#{(int)ev.ID}] {msg}");
                    }
                    catch { decodeExceptions++; }
                }
            };
            source.AllEvents += _ => total++;
            source.Process();
        }

        var sb = new StringBuilder();
        sb.AppendLine($"ETL: {etl}  ({new FileInfo(etl).Length / (1024.0 * 1024):F1} MB, pointerSize={pointerSize})");
        sb.AppendLine($"total events          : {total:N0}");
        sb.AppendLine($"  handled (manifest)  : {handledDynamic:N0}");
        sb.AppendLine($"  handled (kernel)    : {handledKernel:N0}");
        sb.AppendLine($"  unhandled           : {unhandled:N0}");
        sb.AppendLine($"WPP-shaped (unhandled + task GUID): {wppShaped:N0}  across {byGuid.Count:N0} distinct message GUID(s)");
        sb.AppendLine($"decode: attempts(had TMF)={decodeAttempts:N0}  decoded={decoded:N0}  exceptions={decodeExceptions:N0}");
        sb.AppendLine($"TMF entries loaded    : {tmf.Count:N0}  from dirs: {string.Join(" ; ", tmfDirs)}");
        sb.AppendLine();
        sb.AppendLine("Top 30 WPP message GUIDs by event count (these are the providers whose TMFs you'd need):");
        foreach (var kv in byGuid.OrderByDescending(k => k.Value).Take(30))
            sb.AppendLine($"  {kv.Key}  x{kv.Value:N0}");
        sb.AppendLine();
        sb.AppendLine("Sample decoded messages (if any TMFs matched):");
        if (decodedSamples.Count == 0) sb.AppendLine("  (none — no loaded TMF matched a captured provider)");
        else foreach (var s in decodedSamples) sb.AppendLine("  " + s);

        File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine(sb.ToString());
        Assert.IsTrue(total > 0, "no events read");
    }
}
