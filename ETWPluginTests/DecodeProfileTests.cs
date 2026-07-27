using System;
using System.IO;
using findneedle.Implementations.FileExtensions;
using FindPluginCore.Diagnostics.PerfBench;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ETWPluginTests;

/// <summary>
/// CPU-profiles the real ETL DECODE path (ETLProcessor.OpenFile + DoPreProcessing) on a large capture,
/// using the shared <see cref="WorkloadProfiler"/> EventPipe self-sampler. Shows which code paths dominate
/// decode — the input to the decode-scoping work (skipping providers we don't care about avoids exactly the
/// hot code this surfaces). SkipCI: needs a large local .etl and tens of seconds of CPU.
/// </summary>
[TestClass]
public class DecodeProfileTests
{
    private static string ResolveEtl()
    {
        var env = Environment.GetEnvironmentVariable("FINDNEEDLE_ETL");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            foreach (var name in new[] { "multi-provider-sample.etl", "large-5M.etl", "cats-5M.etl" })
            {
                var cand = Path.Combine(dir, "LargeSamples", name);
                if (File.Exists(cand)) return cand;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

    public TestContext TestContext { get; set; }

    [TestMethod]
    [TestCategory("SkipCI")]
    [Timeout(600000)]
    public void ProfileDecodeHotPaths()
    {
        var etl = ResolveEtl();
        if (string.IsNullOrEmpty(etl)) { Assert.Inconclusive("No sample .etl found (set FINDNEEDLE_ETL)."); return; }
        Console.WriteLine($"DECODE PROFILE: {etl} ({new FileInfo(etl).Length / 1024 / 1024} MB)");

        var (frames, note) = WorkloadProfiler.ProfileAction(() =>
        {
            var p = new ETLProcessor();
            p.OpenFile(etl);
            p.DoPreProcessing(); // the decode: ETWTraceEventSource.Process() on this thread (LoadEarly inline)
        }, threadMarker: "ETLProcessor", topN: 18);

        Console.WriteLine("note: " + note);
        foreach (var f in frames)
            Console.WriteLine($"  {f.Percent,5:F1}%  {f.Kind,-7}  {f.Method}");
        Assert.IsTrue(frames.Count > 0, "expected decode hot frames: " + note);
    }
}
