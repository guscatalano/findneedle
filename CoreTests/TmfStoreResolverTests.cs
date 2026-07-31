using System;
using System.IO;
using FindNeedlePluginLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TmfStoreResolverPlugin;

namespace CoreTests;

/// <summary>
/// Covers the reference <see cref="TmfStoreResolver"/> — the ETL-only resolver that finds a WPP <c>.tmf</c>
/// by its trace GUID in a TMF store (FINDNEEDLE_TMF_STORES), with no binary involved. Verifies both store
/// layouts (flat + SSQP-style subfolder), both GUID namings, and that a miss / no-store passes (null).
/// </summary>
[TestClass]
[DoNotParallelize] // mutates the process-wide FINDNEEDLE_TMF_STORES env var
public class TmfStoreResolverTests
{
    private const string Env = "FINDNEEDLE_TMF_STORES";
    private string? _prior;
    private string _root = "";

    [TestInitialize]
    public void Init()
    {
        _prior = Environment.GetEnvironmentVariable(Env);
        _root = Path.Combine(Path.GetTempPath(), "tmfstore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable(Env, _prior);
        try { Directory.Delete(_root, true); } catch { }
    }

    private static WppTmfResolveRequest Req(Guid g) => new(g, @"C:\caps\trace.etl");

    [TestMethod]
    public void ResolvesFlatLayout_HyphenatedName()
    {
        var g = Guid.NewGuid();
        var tmf = Path.Combine(_root, g.ToString("D") + ".tmf"); // <root>\<guid>.tmf
        File.WriteAllText(tmf, $"{g:D} MyComponent");
        Environment.SetEnvironmentVariable(Env, _root);

        Assert.AreEqual(tmf, new TmfStoreResolver().TryResolveTmf(Req(g)), "flat <guid>.tmf resolves");
    }

    [TestMethod]
    public void ResolvesSsqpLayout_SubfolderPerGuid()
    {
        var g = Guid.NewGuid();
        var dir = Path.Combine(_root, g.ToString("D"));
        Directory.CreateDirectory(dir);
        var tmf = Path.Combine(dir, g.ToString("D") + ".tmf"); // <root>\<guid>\<guid>.tmf
        File.WriteAllText(tmf, $"{g:D} MyComponent");
        Environment.SetEnvironmentVariable(Env, _root);

        Assert.AreEqual(tmf, new TmfStoreResolver().TryResolveTmf(Req(g)), "SSQP-style subfolder layout resolves");
    }

    [TestMethod]
    public void Resolves32HexName()
    {
        var g = Guid.NewGuid();
        var tmf = Path.Combine(_root, g.ToString("N") + ".tmf"); // 32-hex, no hyphens
        File.WriteAllText(tmf, $"{g:D} MyComponent");
        Environment.SetEnvironmentVariable(Env, _root);

        Assert.AreEqual(tmf, new TmfStoreResolver().TryResolveTmf(Req(g)), "32-hex <guid>.tmf resolves");
    }

    [TestMethod]
    public void Misses_And_NoStore_ReturnNull()
    {
        var g = Guid.NewGuid();

        Environment.SetEnvironmentVariable(Env, null);
        Assert.IsNull(new TmfStoreResolver().TryResolveTmf(Req(g)), "no store configured → null");

        Environment.SetEnvironmentVariable(Env, _root); // configured but the GUID isn't present
        Assert.IsNull(new TmfStoreResolver().TryResolveTmf(Req(g)), "GUID not in the store → null");
    }

    [TestMethod]
    public void ImplementsPluginContract()
    {
        Assert.IsTrue(typeof(IWppTmfResolver).IsAssignableFrom(typeof(TmfStoreResolver)), "is an IWppTmfResolver");
        Assert.IsTrue(typeof(IPluginDescription).IsAssignableFrom(typeof(TmfStoreResolver)), "is an IPluginDescription");
        var p = new TmfStoreResolver();
        Assert.IsFalse(string.IsNullOrWhiteSpace(p.GetPluginFriendlyName()));
        Assert.IsFalse(string.IsNullOrWhiteSpace(p.GetPluginClassName()));
    }
}
