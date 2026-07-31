using System;
using System.IO;
using System.Text;
using FindNeedleUX.Services;
using FindNeedlePluginLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.WppSymbols;

/// <summary>
/// The ETL-only resolution path: <see cref="WppSymbolResolver.ProvisionTmfsByGuid"/> asks
/// <see cref="IWppTmfResolver"/> plugins for a TMF per missing WPP message GUID — no binary, no tracepdb —
/// and copies hits into the TMF cache keyed by GUID. Uses the injectable resolver seam so no plugin DLLs or
/// real stores are needed.
/// </summary>
[TestClass]
[TestCategory("WppSymbols")]
[DoNotParallelize] // mutates WppSymbolResolver's static resolver overrides
public class TmfGuidProvisioningTests
{
    private string _cache = "";
    private string _store = "";

    [TestInitialize]
    public void Setup()
    {
        _cache = Path.Combine(Path.GetTempPath(), $"tmfcache_{Guid.NewGuid():N}");
        _store = Path.Combine(Path.GetTempPath(), $"tmfstore_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_store);
    }

    [TestCleanup]
    public void Cleanup()
    {
        WppSymbolResolver.ResetOverridesForTests();
        try { Directory.Delete(_cache, true); } catch { }
        try { Directory.Delete(_store, true); } catch { }
    }

    // Write a fake .tmf into the store and return its path.
    private string StoreTmf(Guid g)
    {
        var path = Path.Combine(_store, g.ToString("D") + ".tmf");
        File.WriteAllText(path, $"{g:D} FakeComponent");
        return path;
    }

    [TestMethod]
    public void MissingGuid_ResolvedByPlugin_IsCopiedIntoCache()
    {
        var g = Guid.NewGuid();
        var src = StoreTmf(g);
        Guid seen = Guid.Empty;
        WppSymbolResolver.TmfResolversOverride = new IWppTmfResolver[]
        {
            new FakeTmfResolver(req => { seen = req.MessageGuid; return req.MessageGuid == g ? src : null; }),
        };

        var sb = new StringBuilder();
        int written = WppSymbolResolver.ProvisionTmfsByGuid(new[] { g.ToString() }, @"C:\caps\t.etl", _cache, sb);

        Assert.AreEqual(g, seen, "the resolver is consulted with the missing message GUID");
        Assert.AreEqual(1, written, "one TMF resolved and cached");
        var cached = Path.Combine(_cache, g.ToString("D") + ".tmf");
        Assert.IsTrue(File.Exists(cached), $"the TMF is cached as <guid>.tmf. Log:\n{sb}");
        Assert.AreEqual(File.ReadAllText(src), File.ReadAllText(cached), "the store's TMF content was copied");
        StringAssert.Contains(sb.ToString(), "resolved TMF", "resolution is logged");
    }

    [TestMethod]
    public void AlreadyCachedGuid_IsNotReResolved()
    {
        var g = Guid.NewGuid();
        Directory.CreateDirectory(_cache);
        File.WriteAllText(Path.Combine(_cache, g.ToString("D") + ".tmf"), "already here");
        int calls = 0;
        WppSymbolResolver.TmfResolversOverride = new IWppTmfResolver[]
        {
            new FakeTmfResolver(_ => { calls++; return null; }),
        };

        int written = WppSymbolResolver.ProvisionTmfsByGuid(new[] { g.ToString() }, null, _cache, new StringBuilder());

        Assert.AreEqual(0, written, "nothing new written");
        Assert.AreEqual(0, calls, "a GUID already in the cache is not re-resolved (GUID-level dedup)");
    }

    [TestMethod]
    public void NoResolverHasIt_WritesNothing_AndDoesNotThrow()
    {
        var g = Guid.NewGuid();
        WppSymbolResolver.TmfResolversOverride = new IWppTmfResolver[] { new FakeTmfResolver(_ => null) };

        int written = WppSymbolResolver.ProvisionTmfsByGuid(new[] { g.ToString() }, null, _cache, new StringBuilder());

        Assert.AreEqual(0, written, "a miss leaves the cache empty");
    }

    [TestMethod]
    public void HangingTmfResolver_IsAbandoned_ByTheTimeout()
    {
        var g = Guid.NewGuid();
        var entered = new System.Threading.ManualResetEventSlim(false);
        WppSymbolResolver.TmfResolversOverride = new IWppTmfResolver[]
        {
            new FakeTmfResolver(_ => { entered.Set(); System.Threading.Thread.Sleep(10_000); return null; }),
        };
        WppSymbolResolver.ResolverTimeoutMsForTests = 300;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        int written = WppSymbolResolver.ProvisionTmfsByGuid(new[] { g.ToString() }, null, _cache, new StringBuilder());
        sw.Stop();

        Assert.IsTrue(entered.IsSet, "the resolver was invoked");
        Assert.AreEqual(0, written, "a hung resolver yields nothing");
        Assert.IsTrue(sw.Elapsed < TimeSpan.FromSeconds(5),
            $"a hung TMF resolver must not stall provisioning (took {sw.ElapsedMilliseconds}ms)");
    }

    private sealed class FakeTmfResolver : IWppTmfResolver
    {
        private readonly Func<WppTmfResolveRequest, string> _fn;
        public FakeTmfResolver(Func<WppTmfResolveRequest, string> fn) { _fn = fn; }
        public string TryResolveTmf(WppTmfResolveRequest request) => _fn(request);
    }
}
