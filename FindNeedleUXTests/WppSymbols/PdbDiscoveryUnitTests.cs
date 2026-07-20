using System;
using System.IO;
using System.Linq;
using System.Text;
using FindNeedleUX.Services.WppSymbols;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.WppSymbols;

/// <summary>
/// Unit tests for the managed WPP PDB discovery building blocks (issue #4): PE identity read,
/// MSF (native PDB) GUID+age read, and symbol path parsing.
/// </summary>
[TestClass]
[TestCategory("WppSymbols")]
public class PdbDiscoveryUnitTests
{
    // ----- PdbIdentity (PE debug directory) -----

    [TestMethod]
    public void PdbIdentity_ReadsCodeViewRecord_FromNativeBinary()
    {
        // ntdll is on every Windows box and always carries an RSDS CodeView record.
        var ntdll = Path.Combine(Environment.SystemDirectory, "ntdll.dll");
        var id = PdbIdentity.TryReadFromBinary(ntdll, out var err);

        Assert.IsNotNull(id, $"ntdll identity read failed: {err}");
        Assert.AreEqual("ntdll.pdb", id.PdbFileName, ignoreCase: true);
        Assert.AreNotEqual(Guid.Empty, id.Guid);
        Assert.IsTrue(id.Age >= 1, "age should be at least 1");
        StringAssert.Contains(id.Key, id.Guid.ToString("N").ToUpperInvariant());
    }

    [TestMethod]
    public void PdbIdentity_ReadsCodeViewRecord_FromManagedAssembly()
    {
        // Managed PEs carry a (portable) CodeView entry too — source folders often mix managed and
        // native binaries, so identity reading must not choke on them.
        var self = typeof(PdbDiscoveryUnitTests).Assembly.Location;
        var id = PdbIdentity.TryReadFromBinary(self, out var err);

        Assert.IsNotNull(id, $"managed identity read failed: {err}");
        StringAssert.EndsWith(id.PdbFileName, ".pdb");
        Assert.AreNotEqual(Guid.Empty, id.Guid);
    }

    [TestMethod]
    public void PdbIdentity_NonPeFile_ReturnsNullWithReason()
    {
        var path = Path.Combine(Path.GetTempPath(), $"notape_{Guid.NewGuid():N}.dll");
        File.WriteAllText(path, "this is not a PE image");
        try
        {
            var id = PdbIdentity.TryReadFromBinary(path, out var err);
            Assert.IsNull(id);
            Assert.IsFalse(string.IsNullOrEmpty(err), "a reason must accompany the failure");
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void PdbIdentity_KeyFormats_UpperAndLowerVariants()
    {
        var id = new PdbIdentity("foo.pdb", Guid.Parse("497B72F6-390A-44FC-878E-5A2D63B6CC4B"), 10);
        Assert.AreEqual("497B72F6390A44FC878E5A2D63B6CC4BA", id.Key, "upper GUID hex + age in upper hex");
        Assert.AreEqual("497b72f6390a44fc878e5a2d63b6cc4ba", id.KeyLower);
    }

    // ----- MsfPdbInfo (native PDB identity) -----

    [TestMethod]
    public void MsfPdbInfo_RoundTrips_GuidAndAge()
    {
        var path = Path.Combine(Path.GetTempPath(), $"msf_{Guid.NewGuid():N}.pdb");
        var guid = Guid.NewGuid();
        TestPdbFactory.WriteMsfPdb(path, guid, age: 7);
        try
        {
            var info = MsfPdbInfo.TryRead(path, out var err);
            Assert.IsNotNull(info, $"synthetic MSF read failed: {err}");
            Assert.AreEqual(guid, info.Value.guid);
            Assert.AreEqual(7, info.Value.age);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void MsfPdbInfo_GarbageFile_ReturnsNullNotThrow()
    {
        var path = Path.Combine(Path.GetTempPath(), $"garbage_{Guid.NewGuid():N}.pdb");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
        try
        {
            Assert.IsNull(MsfPdbInfo.TryRead(path, out var err));
            StringAssert.Contains(err, "MSF");
        }
        finally { File.Delete(path); }
    }

    // ----- SymbolPathParser -----

    [TestMethod]
    public void SymbolPath_PlainDir_IsSingleElementChain()
    {
        var chains = SymbolPathParser.Parse(@"C:\symbols");
        Assert.AreEqual(1, chains.Count);
        Assert.AreEqual(1, chains[0].Count);
        Assert.AreEqual(@"C:\symbols", chains[0][0].Location);
        Assert.IsFalse(chains[0][0].IsHttp);
    }

    [TestMethod]
    public void SymbolPath_SrvChain_CachePlusServer()
    {
        var chains = SymbolPathParser.Parse(@"srv*C:\symcache*https://msdl.microsoft.com/download/symbols");
        Assert.AreEqual(1, chains.Count);
        Assert.AreEqual(2, chains[0].Count);
        Assert.AreEqual(@"C:\symcache", chains[0][0].Location);
        Assert.IsFalse(chains[0][0].IsHttp);
        Assert.IsTrue(chains[0][1].IsHttp);
    }

    [TestMethod]
    public void SymbolPath_MultipleElements_KeepOrder()
    {
        var chains = SymbolPathParser.Parse(@"C:\a;srv*C:\c*https://x.example/sym;cache*C:\k");
        Assert.AreEqual(3, chains.Count);
        Assert.AreEqual(@"C:\a", chains[0][0].Location);
        Assert.AreEqual(2, chains[1].Count);
        Assert.AreEqual(@"C:\k", chains[2][0].Location);
    }

    [TestMethod]
    public void SymbolPath_BareSrv_IsRejected_NoDefaultServers()
    {
        var log = new StringBuilder();
        var chains = SymbolPathParser.Parse("srv*", log);
        Assert.AreEqual(0, chains.Count, "no hardcoded default symbol servers");
        StringAssert.Contains(log.ToString(), "ignored");
    }

    [TestMethod]
    public void SymbolPath_SymSrvElement_DllComponentSkipped()
    {
        var chains = SymbolPathParser.Parse(@"symsrv*symsrv.dll*https://sym.example/store");
        Assert.AreEqual(1, chains.Count);
        Assert.AreEqual(1, chains[0].Count);
        Assert.IsTrue(chains[0][0].IsHttp);
    }
}
