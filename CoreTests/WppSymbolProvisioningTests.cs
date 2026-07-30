using System;
using System.Linq;
using FindNeedlePluginLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

/// <summary>
/// Contract for the <see cref="WppSymbolProvisioning"/> cross-layer seam — the hook the ETL decode path
/// uses to ask the host to resolve missing WPP symbols and retry. The real host handler (which sweeps
/// folders + runs the resolver plugins) lives in FindNeedleUX and is exercised end-to-end in the manual
/// lane; here we pin the plumbing that ETWPlugin depends on: no-handler is a safe no-op, the request is
/// passed through, and a throwing handler can never break a decode.
/// </summary>
[TestClass]
public sealed class WppSymbolProvisioningTests
{
    [TestCleanup]
    public void Cleanup() => WppSymbolProvisioning.Handler = null; // don't leak the hook across tests

    [TestMethod]
    public void NoHandler_TryProvision_IsFalseNoOp()
    {
        WppSymbolProvisioning.Handler = null;
        Assert.IsFalse(WppSymbolProvisioning.HasHandler);
        Assert.IsFalse(WppSymbolProvisioning.TryProvision(
            new WppProvisionRequest { EtlPath = @"C:\x\trace.etl" }),
            "with no host registered, provisioning must be a no-op (decode falls back to 'symbols missing')");
    }

    [TestMethod]
    public void Handler_ReceivesRequest_AndReturnValueIsPropagated()
    {
        WppProvisionRequest seen = null;
        WppSymbolProvisioning.Handler = req => { seen = req; return true; };

        Assert.IsTrue(WppSymbolProvisioning.HasHandler);
        var request = new WppProvisionRequest
        {
            EtlPath = @"C:\drop\capture.etl",
            MissingMessageGuids = new[] { "11112222-3333-4444-5555-666677778888" },
        };
        bool result = WppSymbolProvisioning.TryProvision(request);

        Assert.IsTrue(result, "the handler's true return (new symbols available) must propagate → caller retries");
        Assert.AreSame(request, seen, "the exact request must reach the handler");
        Assert.AreEqual(@"C:\drop\capture.etl", seen.EtlPath);
        CollectionAssert.AreEqual(request.MissingMessageGuids.ToArray(), seen.MissingMessageGuids.ToArray());
    }

    [TestMethod]
    public void Handler_ReturningFalse_MeansNoRetry()
    {
        WppSymbolProvisioning.Handler = _ => false;
        Assert.IsFalse(WppSymbolProvisioning.TryProvision(new WppProvisionRequest { EtlPath = "a.etl" }),
            "false (nothing resolved) must propagate so the caller does NOT retry the decode");
    }

    [TestMethod]
    public void Handler_ThatThrows_IsSwallowed_AsFalse()
    {
        WppSymbolProvisioning.Handler = _ => throw new InvalidOperationException("resolver blew up");
        // A provisioning failure must never break the decode — it degrades to "symbols missing".
        Assert.IsFalse(WppSymbolProvisioning.TryProvision(new WppProvisionRequest { EtlPath = "a.etl" }));
    }

    [TestMethod]
    public void NullRequest_IsFalse_EvenWithHandler()
    {
        WppSymbolProvisioning.Handler = _ => true;
        Assert.IsFalse(WppSymbolProvisioning.TryProvision(null));
    }

    [TestMethod]
    public void MissingMessageGuids_DefaultsToEmpty_NotNull()
    {
        // The ETL processor always passes a collection, but defend the contract so a handler can enumerate
        // without a null check.
        Assert.IsNotNull(new WppProvisionRequest { EtlPath = "a.etl" }.MissingMessageGuids);
        Assert.AreEqual(0, new WppProvisionRequest { EtlPath = "a.etl" }.MissingMessageGuids.Count);
    }
}
