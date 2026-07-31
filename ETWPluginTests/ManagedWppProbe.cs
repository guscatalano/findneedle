using System;
using System.IO;
using System.Text;
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
}
