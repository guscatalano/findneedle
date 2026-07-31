using System;
using System.Collections.Generic;
using System.Text;
using FindNeedlePluginLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RawEventDecoderPlugin;

namespace CoreTests;

/// <summary>
/// Covers the reference <see cref="RawHexEventDecoder"/> — the last-resort raw-event decoder. It claims only
/// the provider GUIDs in FINDNEEDLE_RAWDECODE_GUIDS and formats a claimed event's payload as hex.
/// </summary>
[TestClass]
[DoNotParallelize] // mutates the process-wide FINDNEEDLE_RAWDECODE_GUIDS env var
public class RawHexEventDecoderTests
{
    private const string Env = "FINDNEEDLE_RAWDECODE_GUIDS";
    private string? _prior;

    [TestInitialize]
    public void Init() => _prior = Environment.GetEnvironmentVariable(Env);

    [TestCleanup]
    public void Cleanup() => Environment.SetEnvironmentVariable(Env, _prior);

    [TestMethod]
    public void ClaimsOnlyConfiguredGuids()
    {
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        Environment.SetEnvironmentVariable(Env, mine.ToString("D"));
        var d = new RawHexEventDecoder();

        Assert.IsTrue(d.CanDecode(mine), "claims a configured GUID");
        Assert.IsFalse(d.CanDecode(other), "does not claim an unconfigured GUID");

        Environment.SetEnvironmentVariable(Env, null);
        Assert.IsFalse(d.CanDecode(mine), "claims nothing when unconfigured");
    }

    [TestMethod]
    public void DecodesPayloadAsHex_AndLogs()
    {
        var g = Guid.NewGuid();
        Environment.SetEnvironmentVariable(Env, g.ToString("D"));
        var logged = new List<string>();
        var e = new WppRawEvent
        {
            ProviderGuid = g,
            MessageNumber = 42,
            Data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF },
            Log = logged.Add,
        };

        var msg = new RawHexEventDecoder().TryDecode(e);

        Assert.AreEqual("[raw msg 42] DEADBEEF", msg, "renders the payload as uppercase hex");
        Assert.IsTrue(logged.Count > 0, "the decoder wrote to the request log sink");
    }

    [TestMethod]
    public void EmptyPayload_RendersPlaceholder()
    {
        var e = new WppRawEvent { MessageNumber = 1, Data = Array.Empty<byte>() };
        StringAssert.Contains(new RawHexEventDecoder().TryDecode(e), "(no data)");
    }

    [TestMethod]
    public void ImplementsPluginContract()
    {
        Assert.IsTrue(typeof(IWppEventDecoder).IsAssignableFrom(typeof(RawHexEventDecoder)), "is an IWppEventDecoder");
        Assert.IsTrue(typeof(IPluginDescription).IsAssignableFrom(typeof(RawHexEventDecoder)), "is an IPluginDescription");
        var p = new RawHexEventDecoder();
        Assert.IsFalse(string.IsNullOrWhiteSpace(p.GetPluginFriendlyName()));
        Assert.IsFalse(string.IsNullOrWhiteSpace(p.GetPluginClassName()));
    }
}
