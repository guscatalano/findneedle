using System;
using System.IO;
using System.Linq;
using findneedle.Implementations.FileExtensions;
using findneedle.WDK;
using FindNeedlePluginLib;

namespace ETWPluginTests;

/// <summary>
/// End-to-end test of the ON-DEMAND WPP symbol provisioning wired into the ETL decode path: a trace that
/// tracefmt can't format (≈all events unknown = missing TMFs) stays undecoded UNLESS a custom resolver
/// (registered via <see cref="WppSymbolProvisioning"/>) supplies the symbols, at which point ETLProcessor
/// retries the decode and it succeeds. This is the "doesn't resolve unless it's a custom decode" case.
///
/// Real WppEmitter traces are self-describing (their format info rides along in the .etl), so they can't
/// reproduce the missing-symbols case (see [[wpp-fixture-capture-gotchas]]); crafting a real non-self-
/// describing WPP capture needs admin + the WDK. So the tracefmt decode OUTCOME is driven through
/// <see cref="TraceFmt"/>'s public test hooks — all-unknown until the (fake) resolver "provisions" symbols,
/// then formatted on the retry — which exercises ETLProcessor's DecodeEtlOnce → provision → retry
/// orchestration deterministically in CI, with no admin / WDK / real capture.
/// </summary>
[TestClass]
public sealed class DecodePathSymbolProvisioningTests
{
    private const string MissingGuid = "11112222-3333-4444-5555-666677778888";

    private string _work = null!;
    private string _etl = null!;

    [TestInitialize]
    public void Setup()
    {
        _work = Path.Combine(Path.GetTempPath(), $"FN_decsym_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_work);
        // A dummy .etl so decode takes the tracefmt (non-.txt/.log) branch. Its bytes are never really
        // decoded — TraceFmt is overridden, and the modern-trace probe throws on the garbage bytes and
        // falls back to the tracefmt path (which our hooks then own).
        _etl = Path.Combine(_work, "trace.etl");
        File.WriteAllBytes(_etl, new byte[] { 0x10, 0x00, 0x00, 0x00 });

        // Start from clean ambient decode state (all process-global statics).
        DecodeOptions.ForceFullDecode = false;
        DecodeScope.Current = null;
        TraceFmt.ResetTestOverrides();
        WppSymbolProvisioning.Handler = null;
    }

    [TestCleanup]
    public void Cleanup()
    {
        // Clear the process-global statics so this test never leaks into the real ETL-decode tests.
        TraceFmt.ResetTestOverrides();
        WppSymbolProvisioning.Handler = null;
        DecodeOptions.ForceFullDecode = false;
        DecodeScope.Current = null;
        try { if (Directory.Exists(_work)) Directory.Delete(_work, true); } catch { }
    }

    // Stands in for "tracefmt processed N events, ALL unformattable (no TMF)". outputfile holds the literal
    // Unknown(...) lines ETLProcessor samples for the missing GUID and writes to the resolution log.
    private TraceFmtResult AllUnknown(string tag)
    {
        var outFile = Path.Combine(_work, $"fmt_unknown_{tag}.txt");
        using (var w = new StreamWriter(outFile))
            for (int i = 0; i < 50; i++)
                w.WriteLine($"Unknown( {i}): GUID={MissingGuid} (No Format Information found).");
        return new TraceFmtResult
        {
            outputfile = outFile,
            TotalEventsProcessed = 1000,
            TotalFormatsUnknown = 1000, // 100% unknown → missing-symbols fail-fast
            ConsoleOutput = "Searching for TMF files… 0 found",
        };
    }

    // Stands in for "tracefmt processed N events, all formatted". outputfile holds valid ETLLogLine-format
    // lines so ParseFormattedOutput turns them into rows.
    private TraceFmtResult Formatted(string tag, int rows)
    {
        var outFile = Path.Combine(_work, $"fmt_ok_{tag}.txt");
        using (var w = new StreamWriter(outFile))
            for (int i = 0; i < rows; i++)
                w.WriteLine($"[0]0ABC.0DEF::06/21/2026-12:00:00.000 [ResolvedProvider]decoded line {i}");
        return new TraceFmtResult
        {
            outputfile = outFile,
            TotalEventsProcessed = rows,
            TotalFormatsUnknown = 0, // fully formatted → normal parse
            ConsoleOutput = "Searching for TMF files… 1 found",
        };
    }

    [TestMethod]
    public void MissingSymbols_WithNoResolver_DoesNotDecode()
    {
        // No custom resolver registered → provisioning is a no-op → the all-unknown trace stays undecoded.
        WppSymbolProvisioning.Handler = null;
        TraceFmt.TEST_PreScanOverride = (_, __) => AllUnknown("pre");
        TraceFmt.TEST_ParseSimpleOverride = (_, __) => AllUnknown("full");

        using var p = new ETLProcessor();
        p.OpenFile(_etl);
        p.DoPreProcessing();
        var results = p.GetResults();

        Assert.AreEqual(0, results.Count, "with no custom resolver, a missing-symbols trace must not decode");
        var info = p.GetDecodeInfo();
        Assert.IsTrue(info.TryGetValue("missingTmfs", out var missing) && missing.Contains(MissingGuid),
            "the missing TMF GUID should still be reported so the user knows what's needed");
    }

    [TestMethod]
    public void MissingSymbols_WithCustomResolver_ProvisionsThenDecodes()
    {
        // A custom resolver that "finds" the symbols on demand: flip the decode to formatted + report success.
        int invoked = 0;
        bool provisioned = false;
        WppSymbolProvisioning.Handler = req =>
        {
            invoked++;
            Assert.AreEqual(_etl, req.EtlPath, "the failing ETL path should reach the resolver");
            Assert.IsTrue(req.MissingMessageGuids.Contains(MissingGuid),
                "the missing message GUID should reach the resolver");
            provisioned = true;
            return true; // new symbols available → ETLProcessor retries the decode
        };
        TraceFmt.TEST_PreScanOverride = (_, __) => provisioned ? Formatted("pre", 12) : AllUnknown("pre");
        TraceFmt.TEST_ParseSimpleOverride = (_, __) => provisioned ? Formatted("full", 12) : AllUnknown("full");

        using var p = new ETLProcessor();
        p.OpenFile(_etl);
        p.DoPreProcessing();
        var results = p.GetResults();

        Assert.AreEqual(1, invoked, "the custom resolver should be consulted exactly once");
        Assert.AreEqual(12, results.Count,
            "after the custom resolver provisions symbols, the retried decode should produce rows");
    }

    [TestMethod]
    public void MissingSymbols_ResolverCantFind_TriesOnceThenGivesUp()
    {
        // A resolver that's present but can't find the symbols (returns false) must be tried exactly once
        // (no retry loop, thanks to _provisionAttempted) and the trace stays undecoded.
        int invoked = 0;
        WppSymbolProvisioning.Handler = _ => { invoked++; return false; };
        TraceFmt.TEST_PreScanOverride = (_, __) => AllUnknown("pre");
        TraceFmt.TEST_ParseSimpleOverride = (_, __) => AllUnknown("full");

        using var p = new ETLProcessor();
        p.OpenFile(_etl);
        p.DoPreProcessing();
        var results = p.GetResults();

        Assert.AreEqual(1, invoked, "a resolver that returns false must be tried once, not in a loop");
        Assert.AreEqual(0, results.Count, "if the resolver can't provision symbols, the trace stays undecoded");
    }
}
