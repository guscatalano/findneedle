using System;
using System.Linq;
using findneedle.ETWPlugin;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ETWPluginTests;

[TestClass]
public class EtwNativeProviderScannerTests
{
    // The standard ETW name→GUID algorithm (EventSource / TraceLogging). The "System.Runtime" EventSource
    // has no explicit GUID attribute, so its published id is purely name-derived — the canonical reference
    // pair (also used by dotnet-counters). This pins the hash so a regression in the algorithm (byte order,
    // uppercasing, version nibble) is caught. (Note: many Microsoft-Windows-* providers use a manifest-
    // assigned GUID that is NOT name-derived, so they are not valid anchors here.)
    [TestMethod]
    [TestCategory("Performance")]
    public void GuidFromProviderName_MatchesKnownNameDerivedProvider()
    {
        var g = EtwNativeProviderScanner.GuidFromProviderName("System.Runtime");
        Assert.AreEqual(Guid.Parse("49592c0f-5a05-516d-aa4b-a64e02026c89"), g);
    }

    [TestMethod]
    public void GuidFromProviderName_IsCaseInsensitive()
    {
        // EventSource upper-cases the name before hashing, so case must not change the GUID.
        Assert.AreEqual(
            EtwNativeProviderScanner.GuidFromProviderName("System.Runtime"),
            EtwNativeProviderScanner.GuidFromProviderName("SYSTEM.runtime"));
    }

    [TestMethod]
    public void Scan_OnRealBinary_DoesNotThrowAndResultsAreSelfConsistent()
    {
        // Scan a real Windows binary that declares ETW providers (ntdll has a manifest resource). The exact
        // set is OS-version dependent, so we only assert the scan is robust and every result is well-formed.
        var path = Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\ntdll.dll");
        if (!System.IO.File.Exists(path)) Assert.Inconclusive("ntdll.dll not present");

        var providers = EtwNativeProviderScanner.Scan(path);
        Assert.IsNotNull(providers);
        foreach (var p in providers)
        {
            Assert.AreNotEqual(Guid.Empty, p.Guid);
            // A verified TraceLogging result must actually hash from its name.
            if (p.Source == EtwProviderSource.TraceLoggingVerified && !string.IsNullOrEmpty(p.Name))
                Assert.AreEqual(EtwNativeProviderScanner.GuidFromProviderName(p.Name), p.Guid);
        }
        // Results are ordered most-reliable first.
        var ranks = providers.Select(p => (int)p.Source).ToList();
        // (Manifest=0 .. Heuristic=3) → ascending enum value means descending reliability is NOT guaranteed
        // by enum order alone; just assert authoritative ones are not after heuristic ones.
        int lastAuth = providers.FindLastIndex(p => p.IsAuthoritative);
        int firstHeur = providers.FindIndex(p => p.Source == EtwProviderSource.Heuristic);
        if (lastAuth >= 0 && firstHeur >= 0) Assert.IsTrue(lastAuth < firstHeur);
    }
}
