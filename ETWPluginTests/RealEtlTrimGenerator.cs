using System;
using System.IO;
using Microsoft.Diagnostics.Tracing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ETWPluginTests;

/// <summary>
/// Produce a portable "real enough" .etl fixture by RELOGGING a contiguous prefix of a real capture
/// (set FINDNEEDLE_ETL) into a smaller output file. Unlike the synthetic EventSource generators, this
/// keeps GENUINE events — real Windows Kernel / .NET Runtime / Defender / RPC / WPP events with their real
/// providers and payloads — so it exercises the same decode paths a real capture does, just trimmed to a
/// committable size and self-contained (it no longer needs the multi-GB original).
///
/// PRIVACY: the output contains real machine data copied from the source (computer name, process names,
/// paths, etc.). Treat it exactly as you would the original capture before sharing/committing.
///
/// SkipCI / local-only (needs a real source file + admin-grade ETW libs).
/// Run: set FINDNEEDLE_ETL=...\test.etl  then  vstest ETWPluginTests.dll /Tests:Generate_TrimmedRealEtl
/// Tune the size with FINDNEEDLE_RELOG_EVENTS (default 5,000,000 events ≈ a few hundred MB).
/// </summary>
[TestClass]
[TestCategory("Fixtures")]
[TestCategory("SkipCI")]
public sealed class RealEtlTrimGenerator
{
    public TestContext TestContext { get; set; } = null!;

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "findneedle.sln"))) d = d.Parent;
        return d?.FullName ?? AppContext.BaseDirectory;
    }

    [TestMethod]
    [Timeout(1_200_000)] // up to 20 min for a large source
    public void Generate_TrimmedRealEtl()
    {
        var input = Environment.GetEnvironmentVariable("FINDNEEDLE_ETL");
        if (string.IsNullOrEmpty(input) || !File.Exists(input))
        {
            Assert.Inconclusive("Set FINDNEEDLE_ETL to a real .etl to trim a portable fixture from it.");
            return;
        }

        long target = long.TryParse(Environment.GetEnvironmentVariable("FINDNEEDLE_RELOG_EVENTS"), out var t) && t > 0
            ? t : 5_000_000;

        var outDir = Path.Combine(RepoRoot(), "LargeSamples");
        Directory.CreateDirectory(outDir);
        var output = Path.Combine(outDir, "real-trim.etl");
        try { if (File.Exists(output)) File.Delete(output); } catch { }

        TestContext.WriteLine($"Relogging first {target:N0} events of {input} → {output}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long seen = 0, kept = 0, failed = 0;

        using (var relogger = new ETWReloggerTraceEventSource(input, output))
        {
            relogger.AllEvents += e =>
            {
                seen++;
                if (kept >= target) { relogger.StopProcessing(); return; }
                // Some event types can't be re-emitted faithfully by the relogger — skip those rather
                // than abort, so the output is "real enough" without failing on a handful of oddballs.
                try { relogger.WriteEvent(e); kept++; }
                catch { failed++; }
            };
            relogger.Process();
        }
        sw.Stop();

        var outInfo = new FileInfo(output);
        TestContext.WriteLine($"Done in {sw.Elapsed.TotalSeconds:N1}s — saw {seen:N0}, kept {kept:N0}, skipped {failed:N0}");
        TestContext.WriteLine($"Output: {output}  ({outInfo.Length / 1024.0 / 1024.0:N1} MB, {(kept > 0 ? outInfo.Length / kept : 0)} bytes/event)");

        Assert.IsTrue(File.Exists(output) && outInfo.Length > 0, "relogged fixture should exist and be non-empty");
        Assert.IsTrue(kept > 0, "should have copied at least some real events");
    }
}
