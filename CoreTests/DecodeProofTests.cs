using FindPluginCore.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

/// <summary>The 0/1/2 exit-code boundaries an ISymbolResolver author asserts on. Fast + CI-safe, unlike the
/// subprocess CLI test — this is the guard against the exit code silently going wrong again.</summary>
[TestClass]
public sealed class DecodeProofTests
{
    [TestMethod]
    public void NoRows_IsAlwaysExit2_RegardlessOfProvisioning()
    {
        Assert.AreEqual(2, DecodeProof.ComputeExitCode(rowsDecoded: 0, provisionInvocations: 0, provisionSucceeded: 0));
        Assert.AreEqual(2, DecodeProof.ComputeExitCode(0, 3, 3), "0 rows wins even if provisioning 'succeeded'");
        Assert.AreEqual(2, DecodeProof.ComputeExitCode(-1, 0, 0));
    }

    [TestMethod]
    public void RowsDecoded_NoProvisioning_IsFullyDecoded()
        => Assert.AreEqual(0, DecodeProof.ComputeExitCode(rowsDecoded: 42, provisionInvocations: 0, provisionSucceeded: 0));

    [TestMethod]
    public void RowsDecoded_AllProvisioningSucceeded_IsFullyDecoded()
        => Assert.AreEqual(0, DecodeProof.ComputeExitCode(42, provisionInvocations: 2, provisionSucceeded: 2));

    [TestMethod]
    public void RowsDecoded_ProvisioningPartlyFailed_IsUnresolved()
    {
        Assert.AreEqual(1, DecodeProof.ComputeExitCode(42, provisionInvocations: 2, provisionSucceeded: 1));
        Assert.AreEqual(1, DecodeProof.ComputeExitCode(42, provisionInvocations: 1, provisionSucceeded: 0));
    }

    [TestMethod]
    public void RowsDecoded_ButEventsLeftUnformatted_IsUnresolved()
    {
        // The partial-decode case: some rows rendered, but WPP events remain unformatted because their TMFs
        // never showed up (and it never tripped the all-unknown provisioning fail-fast). This must NOT report
        // "fully decoded" — it's the "0 missing / lots unformatted / exit 0" contradiction the CLI had.
        Assert.AreEqual(1, DecodeProof.ComputeExitCode(42, provisionInvocations: 0, provisionSucceeded: 0, unresolvedEvents: 7));
        Assert.AreEqual(0, DecodeProof.ComputeExitCode(42, 0, 0, unresolvedEvents: 0), "no leftovers stays fully decoded");
        Assert.AreEqual(2, DecodeProof.ComputeExitCode(0, 0, 0, unresolvedEvents: 7), "still nothing decoded wins");
    }

    [TestMethod]
    public void Describe_MatchesEachCode()
    {
        Assert.AreEqual("fully decoded", DecodeProof.Describe(0));
        Assert.AreEqual("decoded WITH UNRESOLVED symbols", DecodeProof.Describe(1));
        Assert.AreEqual("no rows decoded", DecodeProof.Describe(2));
    }
}
