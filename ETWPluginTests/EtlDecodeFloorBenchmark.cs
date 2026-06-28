using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Diagnostics.Tracing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ETWPluginTests;

/// <summary>
/// SPIKE (measurement only): decompose real ETL ingest on a large .etl into its phases so we can tell how
/// much of it is parallelisable. Row sharding / a producer-consumer insert can only parallelise the
/// INSERT (and arguably the per-event wrap); the TraceEvent decode (source.Process) is single-pass and
/// sequential, so it is the hard floor for ETL ingest. This measures:
///   1. decode-only          — source.Process with a counting handler (the irreducible floor).
///   2. decode + wrap        — also constructs an ETLLogLine per event (no insert).
///   (full ingest decode+wrap+insert is measured end-to-end via the app; ~46s warm on this file.)
/// insert ≈ full - (decode+wrap). The merge-back ceiling for ETL ≈ decode-only + merge.
///
/// Set the file via FINDNEEDLE_ETL; defaults to the repo's LargeSamples\large-5M.etl. Inconclusive if absent.
/// </summary>
[TestClass]
[TestCategory("Performance")]
[TestCategory("SkipCI")]
public class EtlDecodeFloorBenchmark
{
    public TestContext TestContext { get; set; }

    private static string FindEtl()
    {
        var env = Environment.GetEnvironmentVariable("FINDNEEDLE_ETL");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
        // Walk up from the test assembly to find the repo's LargeSamples folder.
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            var cand = Path.Combine(dir, "LargeSamples", "large-5M.etl");
            if (File.Exists(cand)) return cand;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

    [TestMethod]
    [Timeout(600_000)]
    public void EtlIngest_DecodeFloor_Decomposition()
    {
        var etl = FindEtl();
        if (etl == null) { Assert.Inconclusive("No ETL file (set FINDNEEDLE_ETL or place LargeSamples\\large-5M.etl)."); return; }
        var mb = new FileInfo(etl).Length / 1024.0 / 1024.0;
        TestContext.WriteLine($"file: {Path.GetFileName(etl)} ({mb:N0} MB)");

        // 1) Pure decode: TraceEvent's source.Process, counting events only (no wrap, no insert).
        long decodeCount = 0;
        var swD = Stopwatch.StartNew();
        using (var source = new ETWTraceEventSource(etl))
        {
            void H(TraceEvent e) => decodeCount++;
            source.Dynamic.All += H;
            source.Kernel.All += H;
            source.Process();
        }
        swD.Stop();
        TestContext.WriteLine($"1) decode-only      : {swD.ElapsedMilliseconds,7:N0} ms   ({decodeCount:N0} events, {Rate(decodeCount, swD.ElapsedMilliseconds)})");

        // 2) Decode + per-event wrap (construct ETLLogLine, discard). Isolates the ctor cost on top of decode.
        long wrapCount = 0;
        var swW = Stopwatch.StartNew();
        using (var source = new ETWTraceEventSource(etl))
        {
            void H(TraceEvent e) { var _ = new ETLLogLine(e); wrapCount++; }
            source.Dynamic.All += H;
            source.Kernel.All += H;
            source.Process();
        }
        swW.Stop();
        TestContext.WriteLine($"2) decode + wrap    : {swW.ElapsedMilliseconds,7:N0} ms   ({wrapCount:N0} events, {Rate(wrapCount, swW.ElapsedMilliseconds)})");

        long wrapOnly = swW.ElapsedMilliseconds - swD.ElapsedMilliseconds;
        TestContext.WriteLine($"   → per-event wrap  : {wrapOnly,7:N0} ms  (decode is the floor: {swD.ElapsedMilliseconds:N0} ms = {(100.0 * swD.ElapsedMilliseconds / Math.Max(1, swW.ElapsedMilliseconds)):F0}% of decode+wrap)");
        TestContext.WriteLine("   NOTE: full ingest (decode+wrap+INSERT) was ~46,000 ms warm on this file; insert ≈ full − (decode+wrap).");
        TestContext.WriteLine("   Row-sharding/merge-back can only parallelise the insert (and overlap it under the decode);");
        TestContext.WriteLine("   the ETL ingest FLOOR is decode-only + merge, so the achievable gain is bounded by line (1).");

        Assert.IsTrue(decodeCount > 0, "decoded zero events?");
        Assert.AreEqual(decodeCount, wrapCount, "same event count both passes");
    }

    private static string Rate(long n, long ms) => ms > 0 ? $"{n * 1000L / ms:N0} ev/s" : "n/a";
}
