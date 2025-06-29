using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace findneedle.ETWPlugin;

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
}
