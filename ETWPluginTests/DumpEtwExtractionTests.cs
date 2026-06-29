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

        // Decode EVERY produced .etl through the real ETL pipeline (a logger's buffers may be a big but idle
        // pre-allocated buffer, so "biggest" isn't "most events") and report per-file; assert the total > 0.
        long totalRows = 0;
        foreach (var etl in res.EtlFiles.OrderBy(f => f))
        {
            int rows = 0;
            try
            {
                var proc = new ETLProcessor();
                proc.OpenFile(etl);
                proc.DoPreProcessing();   // detect modern/WPP + arm the decode (the FolderLocation pipeline does this)
                proc.GetResultsWithCallback(b => System.Threading.Interlocked.Add(ref rows, b.Count)).Wait();
            }
            catch (Exception ex) { TestContext.WriteLine($"  {Path.GetFileName(etl)}: decode error {ex.GetType().Name}: {ex.Message}"); }
            if (rows > 0)
                TestContext.WriteLine($"  {Path.GetFileName(etl),-18} {new FileInfo(etl).Length / 1024.0,8:N0} KB → {rows:N0} rows");
            totalRows += rows;
        }
        TestContext.WriteLine($"TOTAL decoded events across {res.EtlFiles.Count} logger .etl(s): {totalRows:N0}");

        if (totalRows == 0 && res.LikelyToolVersionMismatch)
        {
            // The mechanism worked (loggers enumerated + buffers saved) but cdb's wmitrace extension is older
            // than this OS build, so it misparses the buffer layout ("Unrecognized EtwpDebuggerData Version /
            // Invalid State / Update your debugger"). Not a product bug — needs a matched/newer debugger.
            Assert.Inconclusive(
                $"Extraction works ({res.EtlFiles.Count} loggers enumerated + saved to .etl), but the installed " +
                "wmitrace extension is older than this OS build and misparses the trace buffers (it reports " +
                "'Unrecognized EtwpDebuggerData Version / Update your debugger'). Update Debugging Tools for " +
                "Windows to a build >= the dump's OS to decode events. cdb tail:\n" + Tail(res.CdbOutput));
            return;
        }
        Assert.IsTrue(totalRows > 0, "the saved logger buffers should decode to some events");
    }

    private static string Tail(string s) => s.Length <= 4000 ? s : s.Substring(s.Length - 4000);
}
