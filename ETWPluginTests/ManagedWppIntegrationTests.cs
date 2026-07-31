using System;
using System.IO;
using System.Linq;
using findneedle.Implementations.FileExtensions;
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
