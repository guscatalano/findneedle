using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ETWPluginTests;

/// <summary>
/// On-demand generator for the "big mixed bundle" fixture the load/filter perf tests consume
/// (<see cref="FixtureFilterPerfTests"/>). NOT run in normal passes — it needs admin (ETW capture),
/// the WppEmitter native build, and the WDK on PATH, and it produces hundreds of MB.
///
/// Produces &lt;repo&gt;/LargeSamples/mixed-filter-fixture.zip, gitignored — reproduce by re-running.
/// The zip contains 10 members, each ≳ <see cref="TargetMBPerFile"/> MB:
///   • mixed.etl                — ONE ETW capture holding BOTH WPP (WppEmitter.exe, decodes via the
///                                committed TMF) AND TraceLogging (LogETWApp.exe) events, so the load
///                                test can exercise "with vs without WPP symbols" on a realistic trace.
///   • events.evtx              — a real Windows Event Log export (best-effort size; machine-dependent).
///   • fixture-N.log / .txt     — synthetic logs in the tracefmt text format (varied Source / Message /
///                                timestamp) so every viewer filter has something to bite on at scale.
///
/// Run explicitly (elevated), e.g.:
///   vstest.console ETWPluginTests.dll /Tests:Generate_MixedFilterFixtureZip
/// </summary>
[TestClass]
public sealed class MixedFilterFixtureGenerator
{
    // The user's ask: "at least 50 MB each", 10 files. Dial these down for a quick local fixture.
    private const int TargetMBPerFile = 50;
    private const int TextFileCount = 7;   // + 1 etl + 1 evtx + 1 spare text = 10 members

    // WppEmitter's WPP control GUID (tools/WppEmitter/WppEmitter.cpp) and LogETWApp's TraceLogging GUID.
    private const string WppControlGuid = "{A7C8B3D2-1E4F-4A6B-9C2D-3F5E6A7B8C9D}";

    public TestContext TestContext { get; set; } = null!;

    internal static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "findneedle.sln"))) d = d.Parent;
        return d?.FullName ?? AppContext.BaseDirectory;
    }

    /// <summary>The committed TMF folder that lets tracefmt decode WppEmitter's WPP events.</summary>
    internal static string WppEmitterTmfDir() => Path.Combine(RepoRoot(), "tools", "WppEmitter", "tmf");

    internal static string FixtureZipPath() => Path.Combine(RepoRoot(), "LargeSamples", "mixed-filter-fixture.zip");

    [TestMethod]
    [TestCategory("Fixtures")]
    [TestCategory("SkipCI")]
    [Timeout(1800000)] // up to 30 min
    public void Generate_MixedFilterFixtureZip()
    {
        var outDir = Path.Combine(RepoRoot(), "LargeSamples");
        Directory.CreateDirectory(outDir);
        var staging = Path.Combine(Path.GetTempPath(), $"FN_mixfix_{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        TestContext.WriteLine($"Staging: {staging}");

        try
        {
            var members = new List<string>();

            // 1. The mixed WPP + TraceLogging ETL.
            members.Add(GenerateMixedEtl(Path.Combine(staging, "mixed.etl")));

            // 2. A real .evtx (best-effort size).
            var evtx = GenerateEvtx(Path.Combine(staging, "events.evtx"));
            if (evtx != null) members.Add(evtx);

            // 3. The synthetic text logs.
            for (int i = 0; i < TextFileCount; i++)
            {
                var ext = (i % 2 == 0) ? ".log" : ".txt";
                members.Add(GenerateTextLog(Path.Combine(staging, $"fixture-{i}{ext}"), seed: i));
            }
            // One extra so the bundle has the requested 10 members even if the evtx export failed.
            if (members.Count < 10)
                members.Add(GenerateTextLog(Path.Combine(staging, "fixture-extra.log"), seed: 99));

            // 4. Zip it.
            var zip = FixtureZipPath();
            if (File.Exists(zip)) File.Delete(zip);
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
                foreach (var m in members)
                    z.CreateEntryFromFile(m, Path.GetFileName(m), CompressionLevel.Fastest);

            TestContext.WriteLine($"Fixture: {zip}  ({new FileInfo(zip).Length / (1024.0 * 1024):F1} MB zipped, {members.Count} members)");
            Assert.IsTrue(File.Exists(zip), "fixture zip should have been produced");
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); } catch { }
        }
    }

    // ── ETL: one logman session enabling BOTH providers, fed by the two emitter exes ──────────────

    private string GenerateMixedEtl(string etlPath)
    {
        var wpp = FindWppEmitter();
        var tl = FindLogEtwApp();
        var tlGuid = "{" + LogETWApp.LogSomeStuff.ProviderGuid.ToString() + "}";

        // ~143 bytes/event observed; overshoot the target so the .etl clears TargetMB after ETW's own
        // header/loss overhead. WppEmitter emits ~1.5 events/iter (general + every-other detail);
        // LogETWApp "many" emits 2 events/iter.
        long targetEvents = (long)(TargetMBPerFile * 1024L * 1024L / 143) * 15 / 10; // +50%
        int wppCount = (int)(targetEvents * 0.40 / 1.5);
        int tlCount = (int)(targetEvents * 0.60 / 2.0);

        var session = "FN_MixFixture_Session";
        var basePath = etlPath; // logman appends _NNNNNN before the extension
        Logman("stop", session, "-ets"); // ignore failure (no stale session)
        Logman("delete", session);
        // A single `logman start` rejects two -p; create the collector, then add the 2nd provider.
        RunOrThrow("logman", $"create trace {session} -p {WppControlGuid} 0xffffffff 0xff -o \"{basePath}\" -ow -ft 1");
        RunOrThrow("logman", $"update trace {session} -p {tlGuid} 0xffffffffffffffff 0xff");
        RunOrThrow("logman", $"start {session}");
        TestContext.WriteLine($"Capturing mixed ETL: WppEmitter x{wppCount:N0} + LogETWApp many {tlCount:N0}");
        RunOrThrow(wpp, wppCount.ToString());
        RunOrThrow(tl, $"many {tlCount}");
        RunOrThrow("logman", $"stop {session}");
        Logman("delete", session);

        // Resolve the actual file logman wrote (it suffixes the name, e.g. mixed_000001.etl).
        var produced = ResolveLogmanOutput(basePath);
        if (!string.Equals(produced, etlPath, StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(etlPath)) File.Delete(etlPath);
            File.Move(produced, etlPath);
        }
        TestContext.WriteLine($"mixed.etl: {new FileInfo(etlPath).Length / (1024.0 * 1024):F1} MB");
        return etlPath;
    }

    // logman writes "<base>_NNNNNN.etl" (a sequence suffix) rather than the exact -o name.
    private static string ResolveLogmanOutput(string basePath)
    {
        if (File.Exists(basePath)) return basePath;
        var dir = Path.GetDirectoryName(basePath)!;
        var stem = Path.GetFileNameWithoutExtension(basePath);
        var ext = Path.GetExtension(basePath);
        var hits = Directory.GetFiles(dir, $"{stem}*{ext}");
        if (hits.Length == 0) throw new FileNotFoundException($"logman produced no .etl for {basePath}");
        Array.Sort(hits);
        return hits[^1];
    }

    // ── EVTX: export a real Windows log (best-effort; size is machine-dependent) ───────────────────

    private string? GenerateEvtx(string path)
    {
        foreach (var log in new[] { "Application", "System" })
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                var rc = Run("wevtutil", $"epl {log} \"{path}\"");
                if (rc == 0 && File.Exists(path) && new FileInfo(path).Length > 0)
                {
                    TestContext.WriteLine($"events.evtx: exported '{log}' -> {new FileInfo(path).Length / (1024.0 * 1024):F1} MB");
                    return path;
                }
            }
            catch (Exception ex) { TestContext.WriteLine($"evtx export of {log} failed: {ex.Message}"); }
        }
        TestContext.WriteLine("evtx export unavailable — skipping (bundle falls back to an extra text log).");
        return null;
    }

    // ── Synthetic text logs in tracefmt's text format, varied for every filter ─────────────────────

    private string GenerateTextLog(string path, int seed)
    {
        // tracefmt text line: "[cpu]PID.TID::MM/dd/yyyy-HH:mm:ss.fff [Source]message"
        // (ETLLogLine replaces '-' with ' ' before DateTime.TryParse, so this date format parses.)
        string[] sources = { "WorkerA", "WorkerB", "Scheduler", "NetIO", "DiskIO", "Auth", "Cache" };
        string[] verbs = { "request", "response", "retry", "cache-hit", "cache-miss", "timeout", "commit" };
        string[] routes = { "/api/v1/users", "/api/v1/orders", "/health", "/login", "/sync", "/metrics" };
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(seed);

        long target = (long)TargetMBPerFile * 1024 * 1024;
        var sw = Stopwatch.StartNew();
        long written = 0;
        int i = 0;
        using (var w = new StreamWriter(path, append: false, Encoding.ASCII, 1 << 20))
        {
            while (written < target)
            {
                var src = sources[(i + seed) % sources.Length];
                var ts = start.AddMilliseconds(i * 7L).ToString("MM/dd/yyyy-HH:mm:ss.fff", CultureInfo.InvariantCulture);
                // One unique needle per file so a global-search latency test has an exact-1 target.
                var needle = (i == 0) ? " UNIQUE-NEEDLE-" + seed : string.Empty;
                var line =
                    $"[0]0ABC.{(0x1000 + (i & 0xFFF)):X4}::{ts} [{src}]" +
                    $"{verbs[i % verbs.Length]} id={i} status={(i % 5 == 0 ? "error" : "ok")} " +
                    $"route={routes[i % routes.Length]} latency={i % 250}ms note=fixture-line{needle}\n";
                w.Write(line);
                written += line.Length;
                i++;
            }
        }
        sw.Stop();
        TestContext.WriteLine($"{Path.GetFileName(path)}: {i:N0} lines, {new FileInfo(path).Length / (1024.0 * 1024):F1} MB, {sw.Elapsed.TotalSeconds:F1}s");
        return path;
    }

    // ── process helpers ────────────────────────────────────────────────────────────────────────

    private string FindWppEmitter()
    {
        var exe = Path.Combine(RepoRoot(), "tools", "WppEmitter", "build", "WppEmitter.exe");
        if (File.Exists(exe)) return exe;

        // Try to build it once.
        var build = Path.Combine(RepoRoot(), "tools", "WppEmitter", "build.ps1");
        if (File.Exists(build))
        {
            TestContext.WriteLine("WppEmitter.exe missing — running build.ps1…");
            Run("powershell", $"-NoProfile -ExecutionPolicy Bypass -File \"{build}\"");
        }
        if (File.Exists(exe)) return exe;
        throw new AssertInconclusiveException(
            $"WppEmitter.exe not found at {exe}. Build it: pwsh tools/WppEmitter/build.ps1 (needs the WDK + MSVC).");
    }

    private static string FindLogEtwApp()
    {
        // ETWPluginTests.csproj copies LogETWApp.exe to the test output (and TestDependencies/).
        foreach (var c in new[]
        {
            Path.Combine(AppContext.BaseDirectory, "LogETWApp.exe"),
            Path.Combine(AppContext.BaseDirectory, "TestDependencies", "LogETWApp.exe"),
        })
            if (File.Exists(c)) return c;
        throw new AssertInconclusiveException("LogETWApp.exe not found in the test output — build LogETWApp first.");
    }

    private void Logman(params string[] args) => Run("logman", string.Join(' ', args));

    private void RunOrThrow(string exe, string args)
    {
        var rc = Run(exe, args);
        if (rc != 0) throw new Exception($"`{Path.GetFileName(exe)} {args}` exited {rc}");
    }

    private int Run(string exe, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi);
        if (p == null) return -1;
        var so = p.StandardOutput.ReadToEnd();
        var se = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (!string.IsNullOrWhiteSpace(se)) TestContext.WriteLine($"[{Path.GetFileName(exe)} stderr] {se.Trim()}");
        return p.ExitCode;
    }
}
