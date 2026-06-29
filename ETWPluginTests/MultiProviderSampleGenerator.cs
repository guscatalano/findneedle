using System;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Diagnostics.Tracing.Session;

namespace ETWPluginTests;

/// <summary>
/// Generates a MULTI-PROVIDER .etl sample for exercising the triage provider-picker — unlike
/// large-5M.etl/cats-5M.etl (single EventSource → the picker only ever shows ~2 providers), this writes
/// events from several distinct, named TraceLogging providers, interleaved so they all appear within the
/// triage scan's first events. Sized above the 500 MB triage threshold so opening it offers the picker.
///
/// No native build / xperf / kernel needed (that's MultiProvider50MGenerator). Just a local ETW file
/// session over runtime-named EventSources — but ETW session creation needs an elevated shell.
/// SkipCI / Fixtures / local-only. Output: LargeSamples\multi-provider-sample.etl (gitignored; regenerate).
/// Tune the event count with FINDNEEDLE_MP_EVENTS (default 4,000,000 ≈ 0.6–0.8 GB).
/// </summary>
[TestClass]
[TestCategory("Fixtures")]
[TestCategory("SkipCI")]
[DoNotParallelize]
public sealed class MultiProviderSampleGenerator
{
    public TestContext TestContext { get; set; } = null!;

    // Realistic-looking, distinct provider names so the picker has something meaningful to show.
    private static readonly string[] ProviderNames =
    {
        "Contoso-App-Frontend",
        "Contoso-App-Backend",
        "Contoso-Auth-Service",
        "Contoso-Database",
        "Contoso-Network-Stack",
        "Contoso-Cache",
        "Contoso-Scheduler",
        "Contoso-Telemetry",
    };

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "findneedle.sln"))) d = d.Parent;
        return d?.FullName ?? AppContext.BaseDirectory;
    }

    [TestMethod]
    [Timeout(1_800_000)]
    public void Generate_MultiProvider_Sample()
    {
        long target = long.TryParse(Environment.GetEnvironmentVariable("FINDNEEDLE_MP_EVENTS"), out var t) && t > 0
            ? t : 4_000_000;

        var outDir = Path.Combine(RepoRoot(), "LargeSamples");
        Directory.CreateDirectory(outDir);
        var outEtl = Path.Combine(outDir, "multi-provider-sample.etl");
        try { if (File.Exists(outEtl)) File.Delete(outEtl); } catch { }

        // Self-describing TraceLogging EventSources, named at runtime, decoded by their Name (no manifest).
        var sources = ProviderNames
            .Select(n => new EventSource(n, EventSourceSettings.EtwSelfDescribingEventFormat))
            .ToArray();

        var sw = Stopwatch.StartNew();
        try
        {
            using var session = new TraceEventSession("FindNeedle_MultiProvider_Session", outEtl);
            foreach (var s in sources) session.EnableProvider(s.Guid);
            Thread.Sleep(500); // let the enables propagate to the session before we emit

            // Round-robin across providers so the triage scan (first ~50k events) sees every provider, and
            // give each a different share so the per-provider counts in the picker aren't all identical.
            // Weights sum to 20; e.g. Network/Telemetry are the noisy ones, Auth/Scheduler the quiet ones.
            int[] weights = { 3, 3, 1, 2, 5, 2, 1, 3 };
            int wsum = weights.Sum();
            long emitted = 0;
            long iter = 0;
            while (emitted < target)
            {
                for (int p = 0; p < sources.Length && emitted < target; p++)
                {
                    for (int w = 0; w < weights[p] && emitted < target; w++)
                    {
                        sources[p].Write("Log", new
                        {
                            Id = emitted,
                            Severity = (emitted % 17 == 0) ? "Error" : (emitted % 5 == 0) ? "Warning" : "Info",
                            Message = $"{ProviderNames[p]} event {emitted} on iteration {iter} — needle payload for search",
                        });
                        emitted++;
                    }
                }
                iter++;
                if (emitted % 1_000_000 < wsum) TestContext.WriteLine($"  emitted {emitted:N0} / {target:N0}");
            }
            Thread.Sleep(800); // flush
        }
        finally
        {
            foreach (var s in sources) s.Dispose();
        }
        sw.Stop();

        var fi = new FileInfo(outEtl);
        Assert.IsTrue(fi.Exists && fi.Length > 0, "the multi-provider .etl should have been produced");
        TestContext.WriteLine($"Wrote {outEtl} ({fi.Length / 1024.0 / 1024.0:N0} MB) in {sw.Elapsed.TotalSeconds:N0}s");

        // Verify the triage scan actually sees all the providers we wrote.
        var (providers, _) = findneedle.ETWPlugin.EtlInfoExtractor.QuickScan(outEtl);
        var seen = providers.Where(p => p.StartsWith("Contoso-", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p).ToList();
        TestContext.WriteLine($"QuickScan saw {providers.Count} providers ({seen.Count} Contoso-*): {string.Join(", ", seen)}");
        Assert.IsTrue(fi.Length >= 500L * 1024 * 1024, $"sample should exceed the 500 MB triage threshold (was {fi.Length / 1024.0 / 1024.0:N0} MB) — raise FINDNEEDLE_MP_EVENTS");
        Assert.AreEqual(ProviderNames.Length, seen.Count, "the triage scan should see every provider we emitted");
    }
}
