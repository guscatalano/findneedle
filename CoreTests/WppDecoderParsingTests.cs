using FindNeedlePluginLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

/// <summary>The --wpp-decoder arg mapping. Guards that the CLI honors tracefmt (the reference decoder a
/// resolver author validates against) and defaults to Auto — never hard-pinned to the prototype managed one.</summary>
[TestClass]
public sealed class WppDecoderParsingTests
{
    [DataTestMethod]
    [DataRow("tracefmt", WppDecoder.Tracefmt)]
    [DataRow("TRACEFMT", WppDecoder.Tracefmt)]   // case-insensitive
    [DataRow(" managed ", WppDecoder.Managed)]   // trimmed
    [DataRow("compare", WppDecoder.Compare)]
    [DataRow("auto", WppDecoder.Auto)]
    public void FromArg_MapsKnownValues(string input, WppDecoder expected)
        => Assert.AreEqual(expected, WppDecoderParsing.FromArg(input));

    [DataTestMethod]
    [DataRow("")]
    [DataRow(null)]
    [DataRow("bogus")]
    public void FromArg_UnknownOrEmpty_DefaultsToAuto(string input)
        => Assert.AreEqual(WppDecoder.Auto, WppDecoderParsing.FromArg(input));

    [TestMethod]
    public void IsKnown_DistinguishesValidNames()
    {
        Assert.IsTrue(WppDecoderParsing.IsKnown("tracefmt"));
        Assert.IsTrue(WppDecoderParsing.IsKnown("Auto"));
        Assert.IsFalse(WppDecoderParsing.IsKnown("bogus"));
        Assert.IsFalse(WppDecoderParsing.IsKnown(""));
    }
}
