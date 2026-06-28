using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ETWPluginTests;

/// <summary>
/// Ad-hoc characterization of a real .etl (set FINDNEEDLE_ETL) — dumps OS/header info, the provider mix,
/// event counts, modern-vs-kernel split, and bytes/event. Used to design a portable synthetic fixture that
/// resembles a real capture. SkipCI / local-only (needs a real file + a full decode pass).
/// </summary>
[TestClass]
[TestCategory("SkipCI")]
public sealed class EtlInspectAdHoc
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [Timeout(1_200_000)]
    public void Inspect_EnvEtl()
    {
        var p = Environment.GetEnvironmentVariable("FINDNEEDLE_ETL");
        if (string.IsNullOrEmpty(p) || !File.Exists(p)) { Assert.Inconclusive("set FINDNEEDLE_ETL to a .etl"); return; }

        var info = findneedle.ETWPlugin.EtlInfoExtractor.Inspect(p);
        TestContext.WriteLine(findneedle.ETWPlugin.EtlInfoExtractor.Format(info));

        long total = info.EventCount;
        long manifestTl = info.ManifestOrTraceLoggingEventCount;
        TestContext.WriteLine("");
        TestContext.WriteLine($"SUMMARY bytes={info.FileSizeBytes:N0} events={total:N0} " +
            $"manifestOrTL={manifestTl:N0} kernelOrOther={total - manifestTl:N0} " +
            $"bytesPerEvent={(total > 0 ? info.FileSizeBytes / total : 0)} providers={info.Providers.Count}");
        TestContext.WriteLine($"durationSec={info.SessionDuration.TotalSeconds:N0} os={info.OsVersion} procs={info.NumberOfProcessors}");
        TestContext.WriteLine("--- top providers by event count ---");
        foreach (var kv in info.Providers.OrderByDescending(k => k.Value).Take(40))
            TestContext.WriteLine($"PROVIDER\t{kv.Value,14:N0}\t{kv.Key}");
    }
}
