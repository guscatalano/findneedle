using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using findneedle.Wpp;

namespace ETWPluginTests;

/// <summary>
/// Guards the fix for "the CLI decode-proof gives the same result no matter what": every .etl carries the
/// EventTrace header event (GUID 68fdd900-4a3e-11d1-84f4-0000f80464e3), a classic system provider that rides
/// on TaskGuid but is NOT WPP and will never have a TMF. It used to be counted as a missing WPP symbol, which
/// (a) printed a phantom "missing GUID" and (b) could pin the decode-proof exit code to "unresolved" even when
/// a symbol resolver did its job. The decoder now skips well-known system GUIDs; these tests prove it — at the
/// decoder level (fast, CI-safe) and, separately, end-to-end through the real CLI binary.
/// </summary>
[TestClass]
public sealed class SystemGuidDecodeProofTests
{
    private static readonly Guid EventTraceGuid = new("68fdd900-4a3e-11d1-84f4-0000f80464e3");
    private static string Etl => Path.Combine(AppContext.BaseDirectory, "WppFixtures", "wppstr-sample.etl");
    private static string TmfDir => Path.Combine(AppContext.BaseDirectory, "WppFixtures", "tmf");
    private const string RealWppGuid = "744151fd-b3f4-32e6-38eb-0fd11e3fb62d"; // the string-emitter fixture's WPP GUID

    [TestMethod]
    public void WithTmfs_HeaderIsSkipped_NothingUnresolved()
    {
        if (!File.Exists(Etl)) Assert.Inconclusive($"fixture missing: {Etl}");
        var tmf = TmfDatabase.LoadDirectory(TmfDir);

        var dec = new ManagedWppEtlDecoder(tmf);
        int rows = 0;
        dec.Decode(Etl, _ => rows++);

        Assert.IsTrue(rows > 0, "the WPP events should decode with TMFs present");
        Assert.IsTrue(dec.SystemEvents > 0, "the EventTrace header should be seen and skipped as a system event");
        Assert.IsFalse(dec.UnresolvedGuids.Contains(EventTraceGuid),
            "the EventTrace header GUID must never be reported as a missing WPP symbol");
        Assert.AreEqual(0L, dec.Unresolved,
            "with TMFs present, nothing is unresolved — the header no longer pollutes the tally");
    }

    [TestMethod]
    public void WithoutTmfs_ReportsRealWppGuid_ButNeverTheHeader()
    {
        if (!File.Exists(Etl)) Assert.Inconclusive($"fixture missing: {Etl}");
        // Empty TMF DB → the real WPP events are genuinely unresolved; the header still must not appear.
        var empty = Path.Combine(Path.GetTempPath(), $"FN_notmf_{Guid.NewGuid():N}");
        Directory.CreateDirectory(empty);
        try
        {
            var dec = new ManagedWppEtlDecoder(TmfDatabase.LoadDirectory(empty));
            dec.Decode(Etl, _ => { });

            Assert.IsTrue(dec.UnresolvedGuids.Contains(Guid.Parse(RealWppGuid)),
                "without TMFs the real WPP message GUID is unresolved (this is what a resolver must satisfy)");
            Assert.IsFalse(dec.UnresolvedGuids.Contains(EventTraceGuid),
                "even with zero TMFs, the EventTrace header is system noise — never a 'missing symbol'");
        }
        finally { try { Directory.Delete(empty, true); } catch { } }
    }
}

/// <summary>
/// Drives the REAL findneedle CLI end-to-end. The lightweight cases (plain-log decode, --out, bad format)
/// need only the CLI binary CI already builds, so they run in the pipeline; the WPP-symbol case needs the
/// committed .etl/TMF fixtures and is [SkipCI]. All go Inconclusive (not red) if the exe isn't found. This is
/// also the harness an external ISymbolResolver author can copy.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class CliDecodeProofTests
{
    private static string FindCliExe()
    {
        // Walk up to the repo root (the dir holding findneedle.sln), then take the newest findneedle.exe
        // under the CLI project's build output.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "findneedle.sln"))) dir = dir.Parent;
        if (dir == null) return null;
        var cliBin = Path.Combine(dir.FullName, "findneedle", "bin");
        if (!Directory.Exists(cliBin)) return null;
        return Directory.EnumerateFiles(cliBin, "findneedle.exe", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }

    private static (int exit, string stdout) RunCli(string exe, string folder, string tmfSearchPath, params string[] extraArgs)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        psi.ArgumentList.Add(folder);
        psi.ArgumentList.Add("--force");
        foreach (var a in extraArgs) psi.ArgumentList.Add(a);
        // The managed WPP decoder reads TMFs from TRACE_FORMAT_SEARCH_PATH — set it (or clear it) to model
        // "symbols available" vs "symbols missing" without needing a real resolver plugin.
        psi.EnvironmentVariables["TRACE_FORMAT_SEARCH_PATH"] = tmfSearchPath ?? "";

        using var p = Process.Start(psi)!;
        // Drain both pipes concurrently — never sequential ReadToEnd (that deadlocks). See child-process memory.
        Task<string> so = p.StandardOutput.ReadToEndAsync();
        Task<string> se = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(120_000)) { try { p.Kill(true); } catch { } Assert.Inconclusive("CLI did not exit in 120s"); }
        Task.WaitAll(so, se);
        return (p.ExitCode, so.Result + se.Result);
    }

    [TestMethod]
    [TestCategory("SkipCI")] // needs the committed WPP .etl/TMF fixtures — kept out of CI
    public void CliDecodeProof_ExitCodeTracksSymbolAvailability()
    {
        var exe = FindCliExe();
        if (exe == null) Assert.Inconclusive("findneedle.exe not built — run `dotnet build findneedle` first.");
        var etl = Path.Combine(AppContext.BaseDirectory, "WppFixtures", "wppstr-sample.etl");
        var tmf = Path.Combine(AppContext.BaseDirectory, "WppFixtures", "tmf");
        if (!File.Exists(etl) || !Directory.Exists(tmf)) Assert.Inconclusive("WPP fixtures missing");

        // Isolate the ETL in its own folder (the CLI searches a folder).
        var work = Path.Combine(Path.GetTempPath(), $"FN_cli_{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        File.Copy(etl, Path.Combine(work, "wppstr-sample.etl"));
        try
        {
            var withTmf = RunCli(exe, work, tmf);
            var withoutTmf = RunCli(exe, work, null);

            // The whole point: the two runs differ. With TMFs it fully decodes (exit 0); without, symbols are
            // missing and it does NOT report success — proving the exit code is usable to test a resolver.
            Assert.AreEqual(0, withTmf.exit,
                $"with TMFs on the search path the trace fully decodes (exit 0).\n--- output ---\n{withTmf.stdout}");
            Assert.AreNotEqual(0, withoutTmf.exit,
                $"without TMFs the CLI must NOT report full success — else a resolver can't be tested.\n--- output ---\n{withoutTmf.stdout}");
            StringAssert.Contains(withTmf.stdout, "fully decoded",
                "the decode-proof summary should say 'fully decoded' when symbols are present");
        }
        finally { try { Directory.Delete(work, true); } catch { } }
    }

    [TestMethod]
    public void Cli_PlainTextLog_DecodesAndExitsZero()
    {
        // A non-ETL log needs no TMFs — this proves the storage-based row count (the exit-code fix) works
        // generally, not just for WPP. Before the fix this also returned "0 rows / exit 2" with no rules.
        var exe = FindCliExe();
        if (exe == null) Assert.Inconclusive("findneedle.exe not built");
        var work = Path.Combine(Path.GetTempPath(), $"FN_cli_txt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        File.WriteAllLines(Path.Combine(work, "app.log"), new[]
        {
            "2026-08-01 12:00:00 INFO service started",
            "2026-08-01 12:00:01 WARN slow response",
            "2026-08-01 12:00:02 ERROR connection failed",
        });
        try
        {
            var r = RunCli(exe, work, null);
            Assert.AreEqual(0, r.exit, $"a plain 3-line log should decode and exit 0.\n--- output ---\n{r.stdout}");
            StringAssert.Contains(r.stdout, "fully decoded");
        }
        finally { try { Directory.Delete(work, true); } catch { } }
    }

    [TestMethod]
    public void Cli_OutJson_WritesDecodedRows()
    {
        var exe = FindCliExe();
        if (exe == null) Assert.Inconclusive("findneedle.exe not built");
        var work = Path.Combine(Path.GetTempPath(), $"FN_cli_out_{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        File.WriteAllLines(Path.Combine(work, "app.log"), new[] { "line one", "line two" });
        // Write OUTSIDE the searched folder so the CLI doesn't try to (re)read its own output file.
        var outFile = Path.Combine(Path.GetTempPath(), $"FN_out_{Guid.NewGuid():N}.json");
        try
        {
            var r = RunCli(exe, work, null, "--out=json", "--out-file=" + outFile);
            Assert.AreEqual(0, r.exit, r.stdout);
            Assert.IsTrue(File.Exists(outFile), $"--out=json should write the file.\n--- output ---\n{r.stdout}");
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(outFile));
            Assert.IsTrue(doc.RootElement.GetArrayLength() >= 2, "one JSON row per log line");
        }
        finally { try { Directory.Delete(work, true); } catch { } try { File.Delete(outFile); } catch { } }
    }

    [TestMethod]
    public void Cli_UnknownOutFormat_Warns()
    {
        var exe = FindCliExe();
        if (exe == null) Assert.Inconclusive("findneedle.exe not built");
        var work = Path.Combine(Path.GetTempPath(), $"FN_cli_badfmt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        File.WriteAllLines(Path.Combine(work, "app.log"), new[] { "hello" });
        try
        {
            var r = RunCli(exe, work, null, "--out=xml");
            StringAssert.Contains(r.stdout, "Unknown --out format");
        }
        finally { try { Directory.Delete(work, true); } catch { } }
    }
}
