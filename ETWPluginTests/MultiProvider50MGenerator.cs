using System;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ETWPluginTests;

/// <summary>
/// Generates a large, MULTI-PROVIDER .etl fixture (≈50M events) that resembles a real capture's diversity
/// far better than the single-provider large-5M.etl: it holds manifest/TraceLogging (LogETWApp), WPP
/// (WppEmitter, decodes via the committed TMF), the .NET Runtime provider (the .NET emitter's own GC/JIT),
/// and — when xperf (Windows Performance Toolkit) is present and the run is elevated — real KERNEL events.
///
/// What's emitted vs captured:
///   • manifest + WPP  → emitted directly by our two emitter exes (the bulk; we control the count).
///   • .NET Runtime    → enabled on the session; LogETWApp (managed) produces real CLR events incidentally.
///   • kernel          → only via a real kernel session. Captured with xperf's combined kernel+user capture
///                       and merged into one .etl. If xperf isn't found / not elevated, the kernel slice is
///                       SKIPPED and the file is produced from the user providers via logman (still multi-
///                       provider, just no kernel) — clearly logged either way.
///
/// REQUIREMENTS: elevated (admin) shell for ETW capture (and kernel); the WppEmitter native build
/// (tools/WppEmitter/build.ps1, needs WDK+MSVC) for the WPP slice; LogETWApp built to the test output.
/// SkipCI / Fixtures / local-only. Output ≈5–9 GB at LargeSamples\large-50M.etl (gitignored; regenerate).
/// Run elevated, e.g.:  vstest.console ETWPluginTests.dll /Tests:Generate_50M_MultiProvider
/// Tune with FINDNEEDLE_50M_EVENTS (default 50,000,000).
/// </summary>
[TestClass]
[TestCategory("Fixtures")]
[TestCategory("SkipCI")]
public sealed class MultiProvider50MGenerator
{
    // WppEmitter's WPP control GUID (matches MixedFilterFixtureGenerator) + the CLR runtime provider GUID
    // (Microsoft-Windows-DotNETRuntime — the one that showed up in the real test.etl capture).
    private const string WppControlGuid = "{A7C8B3D2-1E4F-4A6B-9C2D-3F5E6A7B8C9D}";
    private const string DotNetRuntimeGuid = "{e13c0d23-ccbc-4e12-931b-d9cc2eee27e4}";

    public TestContext TestContext { get; set; } = null!;

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "findneedle.sln"))) d = d.Parent;
        return d?.FullName ?? AppContext.BaseDirectory;
    }

    [TestMethod]
    [Timeout(2_400_000)] // up to 40 min — 50M events + a multi-GB merge
    public void Generate_50M_MultiProvider()
    {
        long targetEvents = long.TryParse(Environment.GetEnvironmentVariable("FINDNEEDLE_50M_EVENTS"), out var t) && t > 0
            ? t : 50_000_000;

        var outDir = Path.Combine(RepoRoot(), "LargeSamples");
        Directory.CreateDirectory(outDir);
        var outEtl = Path.Combine(outDir, "large-50M.etl");

        var wpp = FindWppEmitter();      // Inconclusive if absent (needs the WDK build)
        var tl = FindLogEtwApp();        // Inconclusive if absent (build LogETWApp)
        var tlGuid = "{" + LogETWApp.LogSomeStuff.ProviderGuid.ToString() + "}";

        // ~60% TraceLogging (2 events/iter), ~40% WPP (~1.5 events/iter). The .NET-runtime + kernel events
        // ride along on top of these from enabling those providers during the same capture window.
        long tlEvents = (long)(targetEvents * 0.60);
        long wppEvents = (long)(targetEvents * 0.40);
        int tlIters = (int)Math.Min(int.MaxValue, tlEvents / 2);
        int wppIters = (int)Math.Min(int.MaxValue, (long)(wppEvents / 1.5));

        var xperf = FindXperf();
        TestContext.WriteLine($"Target ≈{targetEvents:N0} events → LogETWApp many {tlIters:N0} + WppEmitter {wppIters:N0}; " +
            $"providers: TraceLogging + WPP + .NET Runtime{(xperf != null ? " + KERNEL (xperf)" : " (no kernel — xperf not found)")}");

        var sw = Stopwatch.StartNew();
        try { if (File.Exists(outEtl)) File.Delete(outEtl); } catch { }

        if (xperf != null)
            CaptureCombinedWithKernel(xperf, outEtl, tlGuid, wpp, wppIters, tl, tlIters);
        else
            CaptureUserProvidersViaLogman(outEtl, tlGuid, wpp, wppIters, tl, tlIters);

        sw.Stop();
        var fi = new FileInfo(outEtl);
        Assert.IsTrue(fi.Exists && fi.Length > 0, "the multi-provider .etl should have been produced");
        TestContext.WriteLine($"Done in {sw.Elapsed.TotalMinutes:N1} min — {outEtl} ({fi.Length / 1024.0 / 1024.0:N0} MB)");
        TestContext.WriteLine("Verify composition with Inspect_EnvEtl (set FINDNEEDLE_ETL to the output).");
    }

    /// <summary>Combined kernel + user capture via xperf, merged into one .etl. Real kernel events.</summary>
    private void CaptureCombinedWithKernel(string xperf, string outEtl, string tlGuid, string wpp, int wppIters, string tl, int tlIters)
    {
        const string userLogger = "FN_50M_User";
        // Kernel flags: scheduling + CPU sampling + image/process — the high-volume kernel staples.
        var kernelFlags = "PROC_THREAD+LOADER+CSWITCH+PROFILE+DISK_IO";
        var userGuids = $"{WppControlGuid}+{tlGuid}+{DotNetRuntimeGuid}";

        Run(xperf, "-stop " + userLogger); Run(xperf, "-stop"); // clear any stale sessions (ignore failure)
        RunOrThrow(xperf, $"-on {kernelFlags} -start {userLogger} -on {userGuids}");
        try
        {
            TestContext.WriteLine("Capturing (kernel + WPP + TraceLogging + .NET Runtime)…");
            RunEmitters(wpp, wppIters, tl, tlIters);
        }
        finally
        {
            // -stop <user> stops the user logger; -stop stops the kernel logger; -d merges both into outEtl.
            RunOrThrow(xperf, $"-stop {userLogger} -stop -d \"{outEtl}\"");
        }
    }

    /// <summary>User-providers-only capture via logman (no kernel) — the fallback when xperf is unavailable.
    /// Still multi-provider: WPP + TraceLogging + .NET Runtime.</summary>
    private void CaptureUserProvidersViaLogman(string outEtl, string tlGuid, string wpp, int wppIters, string tl, int tlIters)
    {
        const string session = "FN_50M_Session";
        Run("logman", $"stop {session} -ets"); Run("logman", $"delete {session}"); // ignore failures
        RunOrThrow("logman", $"create trace {session} -p {WppControlGuid} 0xffffffff 0xff -o \"{outEtl}\" -ow -ft 1");
        RunOrThrow("logman", $"update trace {session} -p {tlGuid} 0xffffffffffffffff 0xff");
        RunOrThrow("logman", $"update trace {session} -p {DotNetRuntimeGuid} 0xffffffffffffffff 0xff");
        RunOrThrow("logman", $"start {session}");
        try
        {
            TestContext.WriteLine("Capturing (WPP + TraceLogging + .NET Runtime; no kernel)…");
            RunEmitters(wpp, wppIters, tl, tlIters);
        }
        finally
        {
            Run("logman", $"stop {session}");
            Run("logman", $"delete {session}");
        }
        // logman suffixes the name (e.g. large-50M_000001.etl) — move it to the exact path.
        if (!File.Exists(outEtl))
        {
            var dir = Path.GetDirectoryName(outEtl)!;
            var stem = Path.GetFileNameWithoutExtension(outEtl);
            var hits = Directory.GetFiles(dir, $"{stem}*{Path.GetExtension(outEtl)}");
            Array.Sort(hits);
            if (hits.Length > 0) File.Move(hits[^1], outEtl);
        }
    }

    private void RunEmitters(string wpp, int wppIters, string tl, int tlIters)
    {
        RunOrThrow(tl, $"many {tlIters}");
        RunOrThrow(wpp, wppIters.ToString());
    }

    private static string FindXperf()
    {
        foreach (var c in new[]
        {
            @"C:\Program Files (x86)\Windows Kits\10\Windows Performance Toolkit\xperf.exe",
            @"C:\Program Files\Windows Kits\10\Windows Performance Toolkit\xperf.exe",
        })
            if (File.Exists(c)) return c;
        // On PATH?
        try
        {
            var psi = new ProcessStartInfo("where", "xperf") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi); var o = p!.StandardOutput.ReadToEnd(); p.WaitForExit();
            var first = o.Split('\n')[0].Trim();
            if (p.ExitCode == 0 && !string.IsNullOrEmpty(first) && File.Exists(first)) return first;
        }
        catch { }
        return null;
    }

    private string FindWppEmitter()
    {
        var exe = Path.Combine(RepoRoot(), "tools", "WppEmitter", "build", "WppEmitter.exe");
        if (File.Exists(exe)) return exe;
        throw new AssertInconclusiveException(
            $"WppEmitter.exe not found at {exe}. Build it: pwsh tools/WppEmitter/build.ps1 (needs the WDK + MSVC).");
    }

    private static string FindLogEtwApp()
    {
        foreach (var c in new[]
        {
            Path.Combine(AppContext.BaseDirectory, "LogETWApp.exe"),
            Path.Combine(AppContext.BaseDirectory, "TestDependencies", "LogETWApp.exe"),
        })
            if (File.Exists(c)) return c;
        throw new AssertInconclusiveException("LogETWApp.exe not found in the test output — build LogETWApp first.");
    }

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
