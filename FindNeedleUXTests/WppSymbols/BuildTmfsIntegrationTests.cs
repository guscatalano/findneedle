using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FindNeedleUX.Services;
using FindNeedleUX.Services.WppSymbols;

namespace FindNeedleUXTests.WppSymbols;

/// <summary>
/// End-to-end checks of the rebuilt <see cref="WppSymbolResolver.BuildTmfs"/>: managed discovery
/// (identity → verified loose PDB) followed by real <c>tracepdb -f</c> extraction, and the
/// failure-report shape when nothing resolves. Manual lane (SkipCI): needs the WDK's tracepdb and
/// the locally built <c>tools\WppEmitter</c> fixture (a real WPP binary + matching PDB).
/// </summary>
[TestClass]
[TestCategory("WppSymbols")]
[TestCategory("SkipCI")]
[DoNotParallelize] // runs the real tracepdb against WppSymbolResolver's static (non-overridden) seams
public class BuildTmfsIntegrationTests
{
    private static string SolutionDir =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static string FixtureDir => Path.Combine(SolutionDir, "tools", "WppEmitter", "build");

    [TestMethod]
    [Timeout(120_000)]
    public void BuildTmfs_WppEmitterFixture_VerifiesLoosePdb_AndExtractsTmf()
    {
        if (string.IsNullOrEmpty(WppSymbolResolver.FindTracePdb()))
            Assert.Inconclusive("tracepdb.exe not installed");
        if (!File.Exists(Path.Combine(FixtureDir, "WppEmitter.exe")) ||
            !File.Exists(Path.Combine(FixtureDir, "WppEmitter.pdb")))
            Assert.Inconclusive(@"tools\WppEmitter fixture not built (run tools\WppEmitter\build.ps1)");

        // Copy the fixture pair into a scratch folder so the test never touches the repo tree.
        var work = Path.Combine(Path.GetTempPath(), $"buildtmfs_{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        try
        {
            File.Copy(Path.Combine(FixtureDir, "WppEmitter.exe"), Path.Combine(work, "WppEmitter.exe"));
            File.Copy(Path.Combine(FixtureDir, "WppEmitter.pdb"), Path.Combine(work, "WppEmitter.pdb"));

            var (count, log) = WppSymbolResolver.BuildTmfs(work, symbolPath: null);

            // Discovery: the exe's identity was read and the loose PDB accepted by GUID+age.
            StringAssert.Contains(log, "WppEmitter.exe needs:", $"identity line missing. Log:\n{log}");
            Assert.IsTrue(log.IndexOf("wppemitter.pdb", StringComparison.OrdinalIgnoreCase) >= 0,
                $"expected PDB name missing from identity line. Log:\n{log}");
            StringAssert.Contains(log, "GUID+age verified", $"loose verification missing. Log:\n{log}");
            // Extraction: tracepdb ran against the RESOLVED pdb (extract-only mode), TMFs landed.
            StringAssert.Contains(log, "-f", "tracepdb must run in -f (extract-only) mode");
            Assert.IsFalse(log.Contains(" -i "), "the opaque tracepdb -i mode must not be used");
            Assert.IsTrue(count > 0, $"expected TMFs in the cache. Log:\n{log}");
            // The zero-TMF ("public/stripped symbols") note must NOT fire on a successful
            // extraction — including RE-extraction into an already-populated cache, which leaves
            // the file count unchanged (the note keys on write timestamps, not count).
            Assert.IsFalse(log.Contains("produced no TMFs"),
                $"zero-TMF note misfired on a successful extraction. Log:\n{log}");
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { }
        }
    }

    [TestMethod]
    [Timeout(120_000)]
    public void BuildTmfs_UnresolvableBinary_ReportsIdentityAndProbes()
    {
        if (string.IsNullOrEmpty(WppSymbolResolver.FindTracePdb()))
            Assert.Inconclusive("tracepdb.exe not installed");

        // ntdll's PDB is nowhere near a bare temp folder and no symbol path is supplied → the
        // report must say exactly what was needed and that resolution failed.
        var work = Path.Combine(Path.GetTempPath(), $"buildtmfs_miss_{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        try
        {
            File.Copy(Path.Combine(Environment.SystemDirectory, "ntdll.dll"), Path.Combine(work, "ntdll.dll"));
            var id = PdbIdentity.TryReadFromBinary(Path.Combine(work, "ntdll.dll"), out _);
            Assert.IsNotNull(id);

            var (_, log) = WppSymbolResolver.BuildTmfs(work, symbolPath: null);

            StringAssert.Contains(log, id.Guid.ToString(), $"needed GUID missing from report. Log:\n{log}");
            StringAssert.Contains(log, $"age {id.Age}", "needed age missing from report");
            StringAssert.Contains(log, "NOT RESOLVED", "per-identity probe summary missing");
            StringAssert.Contains(log, "FAILED to resolve", "per-binary failure line missing");
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { }
        }
    }
}
