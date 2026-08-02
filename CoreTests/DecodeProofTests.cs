using FindPluginCore.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

/// <summary>The 0/1/2 exit-code boundaries an ISymbolResolver author asserts on. Fast + CI-safe, unlike the
/// subprocess CLI test — this is the guard against the exit code silently going wrong again.</summary>
[TestClass]
public sealed class DecodeProofTests
{
    [TestMethod]
    public void NoRows_IsAlwaysExit2()
    {
        Assert.AreEqual(2, DecodeProof.ComputeExitCode(rowsDecoded: 0));
        Assert.AreEqual(2, DecodeProof.ComputeExitCode(0, unresolvedEvents: 9), "0 rows wins over leftover-unformatted");
        Assert.AreEqual(2, DecodeProof.ComputeExitCode(-1));
    }

    [TestMethod]
    public void RowsDecoded_NothingUnformatted_IsFullyDecoded()
    {
        Assert.AreEqual(0, DecodeProof.ComputeExitCode(rowsDecoded: 42));
        // The self-resolve case that regressed: the resolver was asked and produced nothing, yet the decoder
        // rendered every event (0 unformatted). That is FULLY decoded — provisioning bookkeeping is irrelevant.
        Assert.AreEqual(0, DecodeProof.ComputeExitCode(42, unresolvedEvents: 0));
    }

    [TestMethod]
    public void RowsDecoded_ButEventsLeftUnformatted_IsUnresolved()
    {
        // Partial decode: some rows rendered, but WPP events remain unformatted because their TMFs never
        // showed up. Must NOT report "fully decoded" — the "0 missing / lots unformatted / exit 0" bug.
        Assert.AreEqual(1, DecodeProof.ComputeExitCode(42, unresolvedEvents: 7));
        Assert.AreEqual(1, DecodeProof.ComputeExitCode(1, unresolvedEvents: 1));
    }

    [TestMethod]
    public void Describe_MatchesEachCode()
    {
        Assert.AreEqual("fully decoded", DecodeProof.Describe(0));
        Assert.AreEqual("decoded WITH UNRESOLVED symbols", DecodeProof.Describe(1));
        Assert.AreEqual("no rows decoded", DecodeProof.Describe(2));
    }
}
