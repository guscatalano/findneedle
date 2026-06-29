using System;
using System.IO;
using System.Linq;
using findneedle.ETWPlugin;
using findneedle.Implementations.FileExtensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ETWPluginTests;

/// <summary>
/// PROTOTYPE feasibility: extract ETW logs from a crash dump and decode them through the normal ETL pipeline.
/// Drives <see cref="DumpEtwExtractor"/> (cdb.exe + !wmitrace.strdump/logsave) on a real dump, then decodes
/// the produced .etl with <see cref="ETLProcessor"/> and asserts it yields events.
///
/// Local-only (SkipCI): needs Debugging Tools for Windows (cdb.exe) and a KERNEL/complete .dmp that actually
/// contains live ETW logger buffers. Point it at one with FINDNEEDLE_DMP (and optionally FINDNEEDLE_CDB).
/// </summary>
[TestClass]
[TestCategory("Performance")]
[TestCategory("SkipCI")]
[DoNotParallelize]
public sealed class DumpEtwExtractionTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void FindCdb_ReportsWhetherDebuggerIsAvailable()
    {
        var cdb = DumpEtwExtractor.FindCdb();
        TestContext.WriteLine(cdb == null
            ? "cdb.exe NOT found (set FINDNEEDLE_CDB or install Debugging Tools for Windows)."
            : $"cdb.exe: {cdb}");
        // Informational — never fails; the extraction test below is the real gate.
    }

    [TestMethod]
    [Timeout(900_000)] // symbol download on first run can be slow
    public void Extract_FromDump_ProducesDecodableEtl()
    {
        var dump = Environment.GetEnvironmentVariable("FINDNEEDLE_DMP");
        if (string.IsNullOrEmpty(dump) || !File.Exists(dump))
        { Assert.Inconclusive("Set FINDNEEDLE_DMP to a kernel/complete .dmp with live ETW loggers."); return; }
        if (DumpEtwExtractor.FindCdb() == null)
        { Assert.Inconclusive("cdb.exe not found — install Debugging Tools for Windows or set FINDNEEDLE_CDB."); return; }

        var res = DumpEtwExtractor.ExtractToEtls(dump);
        TestContext.WriteLine($"cdb: {res.CdbPath}");
        TestContext.WriteLine($"produced {res.EtlFiles.Count} .etl(s): {string.Join(", ", res.EtlFiles.Select(Path.GetFileName))}");
        Assert.IsTrue(res.EtlFiles.Count > 0,
            "expected at least one logger's buffers saved to .etl — needs a kernel/complete dump with live ETW " +
            "loggers. Tail of cdb output:\n" + Tail(res.CdbOutput));

        // Decode the largest produced .etl through the real ETL pipeline and assert it yields events.
        var biggest = res.EtlFiles.OrderByDescending(f => new FileInfo(f).Length).First();
        var proc = new ETLProcessor();
        proc.OpenFile(biggest);
        proc.LoadInMemory();
        int rows = proc.GetResults().Count;
        TestContext.WriteLine($"decoded {Path.GetFileName(biggest)} ({new FileInfo(biggest).Length / 1024.0:N0} KB) → {rows:N0} rows");
        Assert.IsTrue(rows > 0, "the saved logger buffers should decode to some events");
    }

    private static string Tail(string s) => s.Length <= 4000 ? s : s.Substring(s.Length - 4000);
}
