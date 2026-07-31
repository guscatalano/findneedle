using System;
using System.IO;
using System.Linq;
using findneedle.Wpp;
using FindNeedlePluginLib;

namespace ETWPluginTests;

/// <summary>
/// The last-resort DECODE tier: when the TMF lookup misses, <see cref="ManagedWppEtlDecoder"/> offers the raw
/// event to a registered <see cref="IWppEventDecoder"/> plugin (via the <see cref="WppEventDecoding"/> seam)
/// before counting it unresolved. Driven with an EMPTY TMF database so every event misses, proving the plugin
/// is what turns "unresolved" into a decoded row.
/// </summary>
[TestClass]
[DoNotParallelize] // sets the process-global WppEventDecoding.Provider
public sealed class ManagedWppPluginDecodeTests
{
    private static string Etl => Path.Combine(AppContext.BaseDirectory, "WppFixtures", "wppstr-sample.etl");
    private string _emptyTmfDir = "";

    [TestInitialize]
    public void Init()
    {
        _emptyTmfDir = Path.Combine(Path.GetTempPath(), "emptytmf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_emptyTmfDir); // no .tmf files → every event misses
    }

    [TestCleanup]
    public void Cleanup()
    {
        WppEventDecoding.Provider = null; // never leak the fake into other managed-decode tests
        try { Directory.Delete(_emptyTmfDir, true); } catch { }
    }

    [TestMethod]
    public void PluginDecodesEventsWhoseTmfIsMissing()
    {
        if (!File.Exists(Etl)) Assert.Inconclusive($"fixture missing: {Etl}");

        int claims = 0;
        WppEventDecoding.Provider = () => new IWppEventDecoder[]
        {
            new FakeDecoder(_ => { claims++; return true; }, e => $"PLUGIN[{e.MessageNumber}] {e.Data.Length}B"),
        };

        var tmf = TmfDatabase.LoadDirectory(_emptyTmfDir); // empty → nothing resolves via TMF
        var decoder = new ManagedWppEtlDecoder(tmf);
        var events = decoder.DecodeToList(Etl);

        Assert.IsTrue(events.Count > 0, "the plugin should have produced rows the empty TMF couldn't");
        Assert.AreEqual(events.Count, decoder.PluginDecoded, "every emitted row came from the plugin (empty TMF)");
        Assert.AreEqual(0L, decoder.Unresolved, "nothing is unresolved when the plugin claims and decodes every miss");
        Assert.IsTrue(events.All(e => e.Message.StartsWith("PLUGIN[")), "rows carry the plugin's message");
        Assert.IsTrue(claims > 0, "CanDecode was consulted");
    }

    [TestMethod]
    public void NoDecoderRegistered_LeavesEventsUnresolved()
    {
        if (!File.Exists(Etl)) Assert.Inconclusive($"fixture missing: {Etl}");
        WppEventDecoding.Provider = null; // no manual decoders

        var decoder = new ManagedWppEtlDecoder(TmfDatabase.LoadDirectory(_emptyTmfDir));
        var events = decoder.DecodeToList(Etl);

        Assert.AreEqual(0, events.Count, "with no TMF and no decoder plugin, nothing decodes");
        Assert.IsTrue(decoder.Unresolved > 0, "the WPP events are counted unresolved");
    }

    [TestMethod]
    public void UnclaimedGuid_IsNotDecoded_ButAskedOnlyOnce()
    {
        if (!File.Exists(Etl)) Assert.Inconclusive($"fixture missing: {Etl}");

        int claims = 0;
        WppEventDecoding.Provider = () => new IWppEventDecoder[]
        {
            new FakeDecoder(_ => { claims++; return false; }, _ => "should-not-be-called"),
        };

        var decoder = new ManagedWppEtlDecoder(TmfDatabase.LoadDirectory(_emptyTmfDir));
        var events = decoder.DecodeToList(Etl);

        Assert.AreEqual(0, events.Count, "a decoder that claims nothing changes nothing");
        Assert.IsTrue(decoder.Unresolved > 0, "events stay unresolved");
        // CanDecode is cached per GUID: asked once per DISTINCT provider GUID, strictly fewer than the number
        // of events (wppstr has 2 provider GUIDs across its 4 events → 2 claims, not 4).
        Assert.IsTrue(claims >= 1 && claims < decoder.Unresolved,
            $"CanDecode should be cached per GUID (claims={claims}, unresolved events={decoder.Unresolved})");
    }

    private sealed class FakeDecoder : IWppEventDecoder
    {
        private readonly Func<Guid, bool> _claim;
        private readonly Func<WppRawEvent, string> _decode;
        public FakeDecoder(Func<Guid, bool> claim, Func<WppRawEvent, string> decode) { _claim = claim; _decode = decode; }
        public bool CanDecode(Guid providerGuid) => _claim(providerGuid);
        public string TryDecode(WppRawEvent rawEvent) => _decode(rawEvent);
    }
}
