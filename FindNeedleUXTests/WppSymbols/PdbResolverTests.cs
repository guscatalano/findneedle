using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using FindNeedleUX.Services.WppSymbols;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.WppSymbols;

/// <summary>
/// Tests for the managed symbol-store prober (issue #4): loose-PDB exact-match (stale rejection),
/// store layouts (flat + two-tier), file.ptr redirects, compressed .pd_ entries, HTTP server
/// resolution with cache write-through, and the "every probe logged" failure report.
/// </summary>
[TestClass]
[TestCategory("WppSymbols")]
public class PdbResolverTests
{
    private string _root;
    private PdbIdentity _id;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pdbresolver_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _id = new PdbIdentity("wpptest.pdb", Guid.NewGuid(), 2);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string NewDir(string name)
    {
        var d = Path.Combine(_root, name);
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>Write a matching (or mismatched) synthetic PDB for _id at the given path.</summary>
    private void WritePdb(string path, bool matching = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        TestPdbFactory.WriteMsfPdb(path, matching ? _id.Guid : Guid.NewGuid(), matching ? _id.Age : _id.Age + 1);
    }

    // ----- loose candidates -----

    [TestMethod]
    public void LooseMatch_IsVerified_AndResolved()
    {
        var folder = NewDir("bin");
        WritePdb(Path.Combine(folder, _id.PdbFileName));

        var log = new StringBuilder();
        var res = new PdbResolver().Resolve(_id, new[] { folder }, null, NewDir("cache"), log);

        Assert.IsTrue(res.Found, log.ToString());
        Assert.AreEqual(Path.Combine(folder, _id.PdbFileName), res.ResolvedPath);
        StringAssert.Contains(log.ToString(), "GUID+age verified");
    }

    [TestMethod]
    public void StaleLoosePdb_IsRejected_NeverAccepted()
    {
        var folder = NewDir("bin");
        var stale = Path.Combine(folder, _id.PdbFileName);
        WritePdb(stale, matching: false);

        var log = new StringBuilder();
        var res = new PdbResolver().Resolve(_id, new[] { folder }, null, NewDir("cache"), log);

        Assert.IsFalse(res.Found, "a GUID/age mismatch must never resolve");
        CollectionAssert.Contains(res.RejectedLooseCandidates, stale);
        StringAssert.Contains(log.ToString(), "STALE");
        StringAssert.Contains(log.ToString(), _id.Guid.ToString(), "log must show the needed GUID");
    }

    [TestMethod]
    public void UnverifiableLoosePdb_IsRejected_NotSilentlyUsed()
    {
        var folder = NewDir("bin");
        var bogus = Path.Combine(folder, _id.PdbFileName);
        File.WriteAllText(bogus, "not a pdb at all");

        var log = new StringBuilder();
        var res = new PdbResolver().Resolve(_id, new[] { folder }, null, NewDir("cache"), log);

        Assert.IsFalse(res.Found);
        CollectionAssert.Contains(res.RejectedLooseCandidates, bogus);
        StringAssert.Contains(log.ToString(), "can't verify");
    }

    // ----- directory stores -----

    [TestMethod]
    public void FlatStore_KeyedLayout_Resolves()
    {
        var store = NewDir("store");
        WritePdb(Path.Combine(store, _id.PdbFileName, _id.Key, _id.PdbFileName));

        var log = new StringBuilder();
        var res = new PdbResolver().Resolve(_id, null, store, NewDir("cache"), log);

        Assert.IsTrue(res.Found, log.ToString());
        StringAssert.Contains(res.ResolvedPath, _id.Key);
    }

    [TestMethod]
    public void TwoTierStore_Index2Marker_Resolves()
    {
        var store = NewDir("store2");
        File.WriteAllText(Path.Combine(store, "index2.txt"), "");
        WritePdb(Path.Combine(store, _id.PdbFileName.Substring(0, 2), _id.PdbFileName, _id.Key, _id.PdbFileName));

        var log = new StringBuilder();
        var res = new PdbResolver().Resolve(_id, null, store, NewDir("cache"), log);

        Assert.IsTrue(res.Found, log.ToString());
        StringAssert.Contains(res.ResolvedPath, Path.Combine(store, "wp"));
    }

    [TestMethod]
    public void FilePtrRedirect_IsFollowed()
    {
        var store = NewDir("storeptr");
        var actual = Path.Combine(NewDir("elsewhere"), "real.pdb");
        WritePdb(actual);
        var keyDir = Path.Combine(store, _id.PdbFileName, _id.Key);
        Directory.CreateDirectory(keyDir);
        File.WriteAllText(Path.Combine(keyDir, "file.ptr"), $"PATH:{actual}");

        var log = new StringBuilder();
        var res = new PdbResolver().Resolve(_id, null, store, NewDir("cache"), log);

        Assert.IsTrue(res.Found, log.ToString());
        Assert.AreEqual(actual, res.ResolvedPath);
    }

    [TestMethod]
    public void FilePtrMsg_IsAMissWithReason()
    {
        var store = NewDir("storemsg");
        var keyDir = Path.Combine(store, _id.PdbFileName, _id.Key);
        Directory.CreateDirectory(keyDir);
        File.WriteAllText(Path.Combine(keyDir, "file.ptr"), "MSG: file was deleted by retention");

        var log = new StringBuilder();
        var res = new PdbResolver().Resolve(_id, null, store, NewDir("cache"), log);

        Assert.IsFalse(res.Found);
        StringAssert.Contains(log.ToString(), "retention");
    }

    [TestMethod]
    public void CompressedStoreEntry_IsExpanded()
    {
        // Build a real single-file CAB with in-box makecab, the same way symstore /compress does.
        var makecab = Path.Combine(Environment.SystemDirectory, "makecab.exe");
        if (!File.Exists(makecab)) Assert.Inconclusive("makecab.exe not available");

        var srcPdb = Path.Combine(NewDir("src"), _id.PdbFileName);
        WritePdb(srcPdb);
        var store = NewDir("storecab");
        var keyDir = Path.Combine(store, _id.PdbFileName, _id.Key);
        Directory.CreateDirectory(keyDir);
        var packed = Path.Combine(keyDir, PdbResolver.CompressedName(_id.PdbFileName));
        var psi = new ProcessStartInfo(makecab, $"\"{srcPdb}\" \"{packed}\"")
        { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
        using (var p = Process.Start(psi)) { p.StandardOutput.ReadToEnd(); p.WaitForExit(); }
        Assert.IsTrue(File.Exists(packed), "makecab should have produced the .pd_");

        var log = new StringBuilder();
        var res = new PdbResolver().Resolve(_id, null, store, NewDir("cache"), log);

        Assert.IsTrue(res.Found, log.ToString());
        Assert.AreEqual(Path.Combine(keyDir, _id.PdbFileName), res.ResolvedPath, "expanded next to the .pd_");
        var check = MsfPdbInfo.TryRead(res.ResolvedPath, out _);
        Assert.IsNotNull(check, "expanded file must be the original PDB");
        Assert.AreEqual(_id.Guid, check.Value.guid);
    }

    [TestMethod]
    public void LocalChainHit_IsBackfilledIntoEarlierCache()
    {
        // srv*<cache>*<store>: a hit in the downstream local store must be copied into the cache
        // (symsrv write-through), and the CACHED copy is what resolution returns.
        var cacheDir = NewDir("chaincache");
        var store = NewDir("chainstore");
        WritePdb(Path.Combine(store, _id.PdbFileName, _id.Key, _id.PdbFileName));

        var log = new StringBuilder();
        var res = new PdbResolver().Resolve(_id, null, $"srv*{cacheDir}*{store}", NewDir("fb"), log);

        Assert.IsTrue(res.Found, log.ToString());
        var cached = Path.Combine(cacheDir, _id.PdbFileName, _id.Key, _id.PdbFileName);
        Assert.AreEqual(cached, res.ResolvedPath);
        Assert.IsTrue(File.Exists(cached), "write-through copy must exist in the cache store");
    }

    [TestMethod]
    public void FilePtr_PointingAtCompressedTarget_IsExpanded()
    {
        var makecab = Path.Combine(Environment.SystemDirectory, "makecab.exe");
        if (!File.Exists(makecab)) Assert.Inconclusive("makecab.exe not available");

        // The redirect target itself is a .pd_ — FollowFilePtr must expand it.
        var srcPdb = Path.Combine(NewDir("ptrsrc"), _id.PdbFileName);
        WritePdb(srcPdb);
        var packed = Path.Combine(NewDir("ptrblob"), PdbResolver.CompressedName(_id.PdbFileName));
        var psi = new ProcessStartInfo(makecab, $"\"{srcPdb}\" \"{packed}\"")
        { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
        using (var p = Process.Start(psi)) { p.StandardOutput.ReadToEnd(); p.WaitForExit(); }

        var store = NewDir("ptrstore");
        var keyDir = Path.Combine(store, _id.PdbFileName, _id.Key);
        Directory.CreateDirectory(keyDir);
        File.WriteAllText(Path.Combine(keyDir, "file.ptr"), $"PATH:{packed}");

        var log = new StringBuilder();
        var res = new PdbResolver().Resolve(_id, null, store, NewDir("cacheptr"), log);

        Assert.IsTrue(res.Found, log.ToString());
        var check = MsfPdbInfo.TryRead(res.ResolvedPath, out _);
        Assert.IsNotNull(check, "expanded redirect target must be a readable PDB");
        Assert.AreEqual(_id.Guid, check.Value.guid);
    }

    // ----- HTTP stores -----

    private sealed class FakeFetcher : ISymbolFetcher
    {
        // Ordinal (case-SENSITIVE) on purpose: models a strict SSQP server, which is exactly what
        // the lowercase-key fallback exists for.
        public Dictionary<string, byte[]> Responses { get; } = new(StringComparer.Ordinal);
        public List<string> Requested { get; } = new();
        public byte[] TryGet(string url, out string error)
        {
            Requested.Add(url);
            error = "HTTP 404";
            return Responses.TryGetValue(url, out var b) ? b : null;
        }
    }

    [TestMethod]
    public void HttpHit_IsCachedIntoChainCache_StoreLayout()
    {
        var cacheDir = NewDir("symcache");
        var pdbBytes = MakePdbBytes();
        var fake = new FakeFetcher();
        fake.Responses[$"https://sym.example/store/{_id.PdbFileName}/{_id.Key}/{_id.PdbFileName}"] = pdbBytes;

        var log = new StringBuilder();
        var res = new PdbResolver(fake).Resolve(
            _id, null, $"srv*{cacheDir}*https://sym.example/store", NewDir("fallback"), log);

        Assert.IsTrue(res.Found, log.ToString());
        var expected = Path.Combine(cacheDir, _id.PdbFileName, _id.Key, _id.PdbFileName);
        Assert.AreEqual(expected, res.ResolvedPath, "server hit must be written through to the chain's cache");
        Assert.IsTrue(File.Exists(expected));
        var check = MsfPdbInfo.TryRead(expected, out _);
        Assert.AreEqual(_id.Guid, check.Value.guid);
    }

    [TestMethod]
    public void HttpMissThenLowercaseKey_IsTried()
    {
        var fake = new FakeFetcher();
        fake.Responses[$"https://sym.example/s/{_id.PdbFileName}/{_id.KeyLower}/{_id.PdbFileName}"] = MakePdbBytes();

        var log = new StringBuilder();
        var res = new PdbResolver(fake).Resolve(_id, null, "srv*https://sym.example/s", NewDir("fallback2"), log);

        Assert.IsTrue(res.Found, log.ToString());
        Assert.IsTrue(fake.Requested.Any(u => u.Contains(_id.Key)), "uppercase key tried first");
        Assert.IsTrue(fake.Requested.Any(u => u.Contains(_id.KeyLower)), "lowercase key tried as fallback");
    }

    [TestMethod]
    public void HttpOnlyChain_UsesFallbackCache()
    {
        var fallback = NewDir("fallback3");
        var fake = new FakeFetcher();
        fake.Responses[$"https://sym.example/s/{_id.PdbFileName}/{_id.Key}/{_id.PdbFileName}"] = MakePdbBytes();

        var res = new PdbResolver(fake).Resolve(_id, null, "srv*https://sym.example/s", fallback, new StringBuilder());

        Assert.IsTrue(res.Found);
        StringAssert.StartsWith(res.ResolvedPath, fallback, "no local cache in the chain → managed pdb-cache");
    }

    // ----- failure report -----

    [TestMethod]
    public void MissEverywhere_ReportShowsIdentityAndEveryProbe()
    {
        var emptyBin = NewDir("emptybin");
        var emptyStore = NewDir("emptystore");
        var fake = new FakeFetcher(); // no responses — every URL 404s

        var log = new StringBuilder();
        var res = new PdbResolver(fake).Resolve(
            _id, new[] { emptyBin }, $"srv*{emptyStore}*https://sym.example/s", NewDir("cache9"), log);

        var text = log.ToString();
        Assert.IsFalse(res.Found);
        StringAssert.Contains(text, "NOT RESOLVED");
        StringAssert.Contains(text, _id.Guid.ToString(), "report must name the GUID we needed");
        StringAssert.Contains(text, _id.Key, "report must include the store key");
        StringAssert.Contains(text, emptyBin, "loose probe must be listed");
        StringAssert.Contains(text, emptyStore, "store probe must be listed");
        StringAssert.Contains(text, "https://sym.example/s", "server probe must be listed");
        Assert.IsTrue(fake.Requested.Count >= 2, "server probed with both key casings / compressed form");
    }

    private byte[] MakePdbBytes()
    {
        var tmp = Path.Combine(_root, $"tmp_{Guid.NewGuid():N}.pdb");
        TestPdbFactory.WriteMsfPdb(tmp, _id.Guid, _id.Age);
        var bytes = File.ReadAllBytes(tmp);
        File.Delete(tmp);
        return bytes;
    }
}
