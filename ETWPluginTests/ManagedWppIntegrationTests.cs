using System;
using System.IO;
using System.Linq;
using findneedle.Implementations.FileExtensions;
using findneedle.WDK;
using FindNeedlePluginLib;

namespace ETWPluginTests;

/// <summary>
/// Integration: the managed WPP decoder wired into <see cref="ETLProcessor"/> via
/// <see cref="DecodeOptions.WppDecoder"/> = Managed. Decodes a real WPP .etl through the full
/// DoPreProcessing → ParseFormattedOutput path (no WDK / tracefmt.exe), and proves the managed decoder
/// shares the SAME on-demand ISymbolResolver provisioning seam as the tracefmt path.
/// </summary>
[TestClass]
public sealed class ManagedWppIntegrationTests
{
    private string _prevTmf;

    private static string Etl => Path.Combine(AppContext.BaseDirectory, "WppFixtures", "wppstr-sample.etl");
    private static string TmfDir => Path.Combine(AppContext.BaseDirectory, "WppFixtures", "tmf");

    [TestInitialize]
    public void Init()
    {
        _prevTmf = Environment.GetEnvironmentVariable("TRACE_FORMAT_SEARCH_PATH");
        DecodeOptions.WppDecoder = WppDecoder.Managed;
        DecodeOptions.ForceFullDecode = false;
        WppSymbolProvisioning.Handler = null;
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("TRACE_FORMAT_SEARCH_PATH", _prevTmf);
        DecodeOptions.WppDecoder = WppDecoder.Auto; // don't leak Managed mode into other test classes
        WppSymbolProvisioning.Handler = null;
        DecodeOptions.ForceFullDecode = false;
        TraceFmt.ResetTestOverrides();
    }

    // A fake tracefmt result: a text file with `lineCount` valid tracefmt-format lines + the matching counts.
    private static TraceFmtResult FakeTracefmt(int lineCount)
    {
        var f = Path.Combine(Path.GetTempPath(), $"cmp_tf_{Guid.NewGuid():N}.txt");
        using (var w = new StreamWriter(f))
            for (int i = 0; i < lineCount; i++)
                w.WriteLine($"[0]0ABC.0DEF::06/21/2026-12:00:00.000 [P]tracefmt line {i}");
        return new TraceFmtResult { outputfile = f, TotalEventsProcessed = lineCount, TotalFormatsUnknown = 0 };
    }

    // Parity: decode the SAME real fixture through BOTH live decoders (tracefmt + managed) via ETLProcessor
    // and assert the decoded messages match. Only STABLE fixtures are used — those whose rendering doesn't
    // vary with the tracefmt/CRT version. Excluded on purpose:
    //   • deliberate managed divergences: SID → S-1-5-18, timestamps → UTC, hexdump → hex bytes (wpptypes2/
    //     wpptime/wppbin);
    //   • version/alias-sensitive rendering: doubles (%g half-rounding differs by msvcrt version — wppmisc)
    //     and NDIS OIDs (some values have two equally-valid header names, e.g. OID_GEN_SUPPORTED_LIST vs
    //     OID_GEN_CO_SUPPORTED_LIST — wppndis). Those are still covered byte-for-byte by the ManagedDecode_*
    //     tests against captured expected strings.
    // Needs a real/WDK tracefmt; skips (Inconclusive) where it isn't available.
    [DataTestMethod]
    [DataRow("wppstr-sample.etl")]
    [DataRow("wpptypes-sample.etl")]
    [DataRow("wppenum-sample.etl")]
    [DataRow("wppenum2-sample.etl")]
    [DataRow("wppemitter-sample.etl")]
    public void Parity_ManagedMatchesTracefmt_ThroughEtlProcessor(string fixture)
    {
        if (!TraceFmt.IsAvailable()) Assert.Inconclusive("tracefmt (WDK) not available — parity needs both decoders");
        var etl = Path.Combine(AppContext.BaseDirectory, "WppFixtures", fixture);
        if (!File.Exists(etl)) Assert.Inconclusive($"fixture missing: {etl}");
        // Both TMF locations on the search path (WppEmitter's TMF lives under tools/, the rest under WppFixtures/tmf).
        Environment.SetEnvironmentVariable("TRACE_FORMAT_SEARCH_PATH", TmfDir + ";" + MixedFilterFixtureGenerator.WppEmitterTmfDir());

        var viaTracefmt = DecodeMessages(etl, WppDecoder.Tracefmt);
        var viaManaged = DecodeMessages(etl, WppDecoder.Managed);

        Assert.IsTrue(viaManaged.Count > 0, $"managed decoded nothing for {fixture}");
        CollectionAssert.AreEquivalent(viaTracefmt, viaManaged,
            $"managed vs tracefmt message parity for {fixture} (tracefmt={viaTracefmt.Count}, managed={viaManaged.Count})");
    }

    private static System.Collections.Generic.List<string> DecodeMessages(string etl, WppDecoder mode)
    {
        DecodeOptions.WppDecoder = mode;
        using var p = new ETLProcessor();
        p.OpenFile(etl);
        p.DoPreProcessing();
        return p.GetResults().Select(r => r.GetMessage()).OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    [TestMethod]
    public void CompareMode_KeepsManaged_WhenItDecodesMore()
    {
        // Compare runs both; tracefmt is stubbed to format just 1 event, the managed decoder formats the
        // fixture's 4 → Compare keeps managed's output.
        if (!File.Exists(Etl)) Assert.Inconclusive($"fixture missing: {Etl}");
        Environment.SetEnvironmentVariable("TRACE_FORMAT_SEARCH_PATH", TmfDir);
        DecodeOptions.WppDecoder = WppDecoder.Compare;
        TraceFmt.TEST_ParseSimpleOverride = (_, __) => FakeTracefmt(1);

        using var p = new ETLProcessor();
        p.OpenFile(Etl);
        p.DoPreProcessing();
        var results = p.GetResults();

        Assert.AreEqual(4, results.Count, "managed(4) > tracefmt(1) → keep managed's 4 rows");
        StringAssert.Contains(p.GetDecodeInfo()["method"], "managed WPP (compare)");
    }

    [TestMethod]
    public void CompareMode_KeepsTracefmt_WhenItDecodesMore()
    {
        // Same, but tracefmt is stubbed to format 10 (> the managed 4) → Compare keeps tracefmt's output.
        if (!File.Exists(Etl)) Assert.Inconclusive($"fixture missing: {Etl}");
        Environment.SetEnvironmentVariable("TRACE_FORMAT_SEARCH_PATH", TmfDir);
        DecodeOptions.WppDecoder = WppDecoder.Compare;
        TraceFmt.TEST_ParseSimpleOverride = (_, __) => FakeTracefmt(10);

        using var p = new ETLProcessor();
        p.OpenFile(Etl);
        p.DoPreProcessing();
        var results = p.GetResults();

        Assert.AreEqual(10, results.Count, "tracefmt(10) > managed(4) → keep tracefmt's 10 rows");
        StringAssert.Contains(p.GetDecodeInfo()["method"], "tracefmt (WPP) (compare)");
    }

    [TestMethod]
    public void ManagedMode_DecodesRealWppEtl_ThroughEtlProcessor()
    {
        if (!File.Exists(Etl)) Assert.Inconclusive($"fixture missing: {Etl}");
        Environment.SetEnvironmentVariable("TRACE_FORMAT_SEARCH_PATH", TmfDir);

        using var p = new ETLProcessor();
        p.OpenFile(Etl);
        p.DoPreProcessing();
        var results = p.GetResults();

        // 3 strtrace + 1 widetrace, decoded with no WDK.
        Assert.AreEqual(4, results.Count, "managed decode should produce the 4 WPP rows");
        var messages = results.Select(r => r.GetMessage()).ToList();
        CollectionAssert.Contains(messages, "strtrace name=alpha id=0 tag=END");
        CollectionAssert.Contains(messages, "widetrace user=root role=admin");
        StringAssert.Contains(p.GetDecodeInfo()["method"], "managed WPP");
    }

    [TestMethod]
    public void ManagedMode_MissingTmfs_UsesProvisioningSeam_ThenDecodes()
    {
        // Proves the managed decoder goes through the SAME ISymbolResolver provisioning path as tracefmt:
        // start with NO TMFs (→ managed reports missing symbols), and a provisioning handler "resolves" them
        // (points TRACE_FORMAT_SEARCH_PATH at the TMF dir + returns true). DoPreProcessing then retries the
        // managed decode once, which now finds the TMFs and produces rows.
        if (!File.Exists(Etl)) Assert.Inconclusive($"fixture missing: {Etl}");
        Environment.SetEnvironmentVariable("TRACE_FORMAT_SEARCH_PATH", null); // no TMFs on the first attempt

        int invoked = 0;
        WppSymbolProvisioning.Handler = req =>
        {
            invoked++;
            Assert.IsTrue(req.MissingMessageGuids.Any(), "the managed decoder should report the unresolved message GUID(s)");
            Environment.SetEnvironmentVariable("TRACE_FORMAT_SEARCH_PATH", TmfDir); // "provision" the TMFs
            return true;
        };

        using var p = new ETLProcessor();
        p.OpenFile(Etl);
        p.DoPreProcessing();
        var results = p.GetResults();

        Assert.AreEqual(1, invoked, "the ISymbolResolver provisioning seam must be consulted for the managed decoder too");
        Assert.AreEqual(4, results.Count, "after provisioning the TMFs, the retried managed decode should produce rows");
    }
}
