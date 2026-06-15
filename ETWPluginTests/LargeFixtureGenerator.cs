using System;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.Text;
using System.Threading;
using findneedle.ETWPlugin;
using Microsoft.Diagnostics.Tracing.Session;

namespace ETWPluginTests;

/// <summary>
/// On-demand generator for the large, consistent test fixtures (NOT run in normal test passes).
/// Produces two files under &lt;repo&gt;/LargeSamples/ (gitignored — reproduce by re-running this):
///   large-5M.log  — 5,000,000 deterministic "[time] INFO: needle line N" rows (~300 MB, byte-identical each run)
///   large-5M.etl  — a file-mode ETW capture of 5,000,000 EventSource events (content-deterministic;
///                   ETW capture timestamps/session metadata vary, and ETW may drop some events under
///                   load, so the captured count is reported and may be slightly under 5M).
///
/// Run it explicitly, e.g.:
///   vstest.console ETWPluginTests.dll /Tests:Generate_LargeFixtures
/// Requires admin (ETW session) — run elevated.
/// </summary>
[TestClass]
public sealed class LargeFixtureGenerator
{
    private const int Events = 5_000_000;

    [EventSource(Name = "FindNeedle-LargeFixture")]
    private sealed class Src : EventSource
    {
        public static readonly Src Log = new();
        public void Tick(int id, string message) => WriteEvent(1, id, message);
    }

    public TestContext TestContext { get; set; } = null!;

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "findneedle.sln"))) d = d.Parent;
        return d?.FullName ?? AppContext.BaseDirectory;
    }

    [TestMethod]
    [TestCategory("Fixtures")]
    [TestCategory("SkipCI")]
    [Timeout(1200000)] // up to 20 min
    public void Generate_LargeFixtures()
    {
        var outDir = Path.Combine(RepoRoot(), "LargeSamples");
        Directory.CreateDirectory(outDir);
        TestContext.WriteLine($"Output dir: {outDir}");

        GenerateLog(Path.Combine(outDir, "large-5M.log"));
        GenerateEtl(Path.Combine(outDir, "large-5M.etl"));
    }

    private void GenerateLog(string path)
    {
        var sw = Stopwatch.StartNew();
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        using (var w = new StreamWriter(path, append: false, Encoding.ASCII, 1 << 20))
        {
            for (int i = 0; i < Events; i++)
            {
                w.Write('[');
                w.Write(start.AddSeconds(i).ToString("yyyy-MM-dd HH:mm:ss"));
                w.Write("] INFO: needle line ");
                w.Write(i);
                w.Write('\n');
            }
        }
        sw.Stop();
        TestContext.WriteLine($".log: {Events:N0} lines, {new FileInfo(path).Length / (1024 * 1024)} MB, {sw.Elapsed.TotalSeconds:F1}s  -> {path}");
    }

    private void GenerateEtl(string path)
    {
        var sw = Stopwatch.StartNew();
        // File-mode session writes the trace straight to the file. Large buffers reduce ETW's
        // under-load event drops; a brief pause every 100k events lets buffers flush to disk.
        using (var session = new TraceEventSession("FindNeedle_LargeFixture_Session", path))
        {
            session.BufferSizeMB = 256;
            session.EnableProvider(Src.Log.Guid);
            Thread.Sleep(500); // let the session start before emitting
            for (int i = 0; i < Events; i++)
            {
                Src.Log.Tick(i, $"needle event {i} payload");
                if ((i & 0x1FFFF) == 0x1FFFF) Thread.Sleep(1); // ~every 128k events, yield for flush
            }
            Thread.Sleep(3000); // let the last buffers flush before Dispose finalizes the file
        }
        sw.Stop();

        long captured = -1;
        try { captured = EtlInfoExtractor.Inspect(path).EventCount; } catch (Exception ex) { TestContext.WriteLine($"inspect failed: {ex.Message}"); }
        TestContext.WriteLine($".etl: emitted {Events:N0}, captured {captured:N0} events, " +
                              $"{new FileInfo(path).Length / (1024 * 1024)} MB, {sw.Elapsed.TotalSeconds:F1}s  -> {path}");
    }
}
