using System;
using System.IO;
using FindNeedlePluginLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SmbSymbolResolverPlugin;

namespace CoreTests;

/// <summary>
/// Covers the reference <see cref="SmbSymbolResolver"/> plugin: it resolves a PDB laid out in the standard
/// symbol-server (SSQP) structure under a share in FINDNEEDLE_SYMBOL_SHARES, and passes (returns null) on
/// a miss / when no shares are configured. The plugin-loading + WppSymbolResolver-invoke integration is
/// covered elsewhere (BuildTmfsOrchestrationTests) and verified end-to-end against a packaged build.
/// </summary>
[TestClass]
[DoNotParallelize] // mutates the process-wide FINDNEEDLE_SYMBOL_SHARES env var
public class SymbolResolverPluginTests
{
    private const string Env = "FINDNEEDLE_SYMBOL_SHARES";

    [TestMethod]
    public void SmbSymbolResolver_ResolvesPdb_InSymbolServerLayout_AndMisses()
    {
        var root = Path.Combine(Path.GetTempPath(), "symshare_" + Guid.NewGuid().ToString("N"));
        var req = new SymbolLookupRequest("mydriver.pdb", Guid.NewGuid(), age: 3, binaryPath: @"C:\bins\mydriver.sys");
        // Lay the PDB out symbol-server style: <root>\<pdb>\<Key>\<pdb>
        var dir = Path.Combine(root, req.PdbFileName, req.Key);
        Directory.CreateDirectory(dir);
        var pdbPath = Path.Combine(dir, req.PdbFileName);
        File.WriteAllText(pdbPath, "pdb");

        var prior = Environment.GetEnvironmentVariable(Env);
        try
        {
            var resolver = new SmbSymbolResolver();

            // No shares configured → pass.
            Environment.SetEnvironmentVariable(Env, null);
            Assert.IsNull(resolver.TryResolvePdb(req), "no shares configured → null");

            // Share configured, matching identity → the SSQP-layout PDB.
            Environment.SetEnvironmentVariable(Env, root);
            Assert.AreEqual(pdbPath, resolver.TryResolvePdb(req), "resolves the SSQP-layout PDB for the exact identity");

            // Same name, different GUID/age → miss (SSQP key doesn't match, so no stale PDB returned).
            var wrong = new SymbolLookupRequest("mydriver.pdb", Guid.NewGuid(), age: 3, binaryPath: "x");
            Assert.IsNull(resolver.TryResolvePdb(wrong), "a different GUID/age is not resolved");

            // A second share in the list is also searched.
            var otherRoot = Path.Combine(Path.GetTempPath(), "symshare2_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(otherRoot);
            Environment.SetEnvironmentVariable(Env, otherRoot + ";" + root);
            Assert.AreEqual(pdbPath, resolver.TryResolvePdb(req), "later shares in the ';' list are searched too");
            try { Directory.Delete(otherRoot, true); } catch { }
        }
        finally
        {
            Environment.SetEnvironmentVariable(Env, prior);
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [TestMethod]
    public void SmbSymbolResolver_ImplementsPluginContract()
    {
        // The two interfaces plugin discovery matches on (both must be present for auto-load).
        Assert.IsTrue(typeof(ISymbolResolver).IsAssignableFrom(typeof(SmbSymbolResolver)), "is an ISymbolResolver");
        Assert.IsTrue(typeof(IPluginDescription).IsAssignableFrom(typeof(SmbSymbolResolver)), "is an IPluginDescription");
        var p = new SmbSymbolResolver();
        Assert.IsFalse(string.IsNullOrWhiteSpace(p.GetPluginFriendlyName()));
        Assert.IsFalse(string.IsNullOrWhiteSpace(p.GetPluginClassName()));
    }
}
