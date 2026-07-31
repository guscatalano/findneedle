using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FindNeedlePluginLib;
using HttpSymbolServerResolverPlugin;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

/// <summary>
/// Covers the reference <see cref="HttpSymbolServerResolver"/> — a STATEFUL network resolver. Unlike the
/// SMB plugin (stateless; the OS owns the connection), this one owns an <see cref="HttpClient"/> and a
/// download cache, so these tests pin the three things a network resolver must get right: single-fetch +
/// filesystem caching, negative caching of misses, and fail-fast fallthrough when a server hangs. All run
/// against a fake <see cref="HttpMessageHandler"/> — no real network.
/// </summary>
[TestClass]
[DoNotParallelize] // mutates process-wide env vars + the plugin's static client/cache
public class HttpSymbolServerResolverTests
{
    private const string ServersEnv = "FINDNEEDLE_SYMBOL_SERVERS";
    private const string TimeoutEnv = "FINDNEEDLE_SYMBOL_HTTP_TIMEOUT_MS";

    private string? _priorServers;
    private string? _priorTimeout;
    private string _cacheDir = "";

    [TestInitialize]
    public void Init()
    {
        _priorServers = Environment.GetEnvironmentVariable(ServersEnv);
        _priorTimeout = Environment.GetEnvironmentVariable(TimeoutEnv);
        _cacheDir = Path.Combine(Path.GetTempPath(), "httpsym_" + Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable(ServersEnv, _priorServers);
        Environment.SetEnvironmentVariable(TimeoutEnv, _priorTimeout);
        HttpSymbolServerResolver.ResetForTests(null, null); // drop the fake client + cache override
        try { Directory.Delete(_cacheDir, true); } catch { }
    }

    private static SymbolLookupRequest Req() =>
        new("mydriver.pdb", Guid.Parse("11111111-2222-3333-4444-555555555555"), age: 7,
            binaryPath: @"C:\bins\mydriver.sys");

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    [TestMethod]
    public void Resolves_DownloadsToCache_AndSecondCallIsAFilesystemHit()
    {
        var req = Req();
        // 200 for the exact SSQP path (contains the key), 404 otherwise.
        var handler = new FakeHandler((m, _) =>
            m.RequestUri!.AbsolutePath.Contains(req.Key, StringComparison.OrdinalIgnoreCase)
                ? Ok("PDBDATA") : new HttpResponseMessage(HttpStatusCode.NotFound));

        Environment.SetEnvironmentVariable(ServersEnv, "http://sym.example/store");
        HttpSymbolServerResolver.ResetForTests(handler, _cacheDir);
        var r = new HttpSymbolServerResolver();

        var p1 = r.TryResolvePdb(req);
        Assert.IsNotNull(p1, "a served PDB resolves");
        Assert.IsTrue(File.Exists(p1), "the PDB was downloaded to the local cache");
        Assert.AreEqual("PDBDATA", File.ReadAllText(p1!), "the cached file is the downloaded body");
        Assert.AreEqual(1, handler.Calls, "one network request for the first resolve");

        // Second resolve of the SAME identity: served from the filesystem cache, no network at all.
        var p2 = r.TryResolvePdb(req);
        Assert.AreEqual(p1, p2, "same cached path");
        Assert.AreEqual(1, handler.Calls, "filesystem cache hit — no second request");
    }

    [TestMethod]
    public void Miss_ReturnsNull_AndIsNegativeCached()
    {
        var handler = new FakeHandler((_, __) => new HttpResponseMessage(HttpStatusCode.NotFound));
        Environment.SetEnvironmentVariable(ServersEnv, "http://sym.example/store");
        HttpSymbolServerResolver.ResetForTests(handler, _cacheDir);
        var r = new HttpSymbolServerResolver();
        var req = Req();

        Assert.IsNull(r.TryResolvePdb(req), "not on the server → null (pass to next resolver)");
        Assert.AreEqual(1, handler.Calls, "one probe");
        Assert.IsNull(r.TryResolvePdb(req), "still null");
        Assert.AreEqual(1, handler.Calls, "the miss is negative-cached — not re-fetched");
    }

    [TestMethod]
    public void NoServersConfigured_ReturnsNull_WithoutTouchingTheNetwork()
    {
        var handler = new FakeHandler((_, __) => Ok("PDBDATA"));
        Environment.SetEnvironmentVariable(ServersEnv, null);
        HttpSymbolServerResolver.ResetForTests(handler, _cacheDir);

        Assert.IsNull(new HttpSymbolServerResolver().TryResolvePdb(Req()), "no servers → pass");
        Assert.AreEqual(0, handler.Calls, "nothing configured → no request at all");
    }

    [TestMethod]
    public void DeadServer_FailsFast_AndFallsThroughToAHealthyServer()
    {
        var req = Req();
        // "dead" host hangs until the client's timeout cancels it; "good" host serves the PDB.
        var handler = new FakeHandler((m, ct) =>
        {
            if (m.RequestUri!.Host == "dead.example")
            {
                ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)); // released early when HttpClient.Timeout cancels
                ct.ThrowIfCancellationRequested();
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }
            return Ok("PDBDATA");
        });

        Environment.SetEnvironmentVariable(ServersEnv, "http://dead.example/store;http://good.example/store");
        Environment.SetEnvironmentVariable(TimeoutEnv, "300"); // fail-fast bound
        HttpSymbolServerResolver.ResetForTests(handler, _cacheDir);

        var sw = Stopwatch.StartNew();
        var p = new HttpSymbolServerResolver().TryResolvePdb(req);
        sw.Stop();

        Assert.IsNotNull(p, "the healthy second server resolves after the dead one times out");
        Assert.AreEqual("PDBDATA", File.ReadAllText(p!));
        Assert.IsTrue(sw.Elapsed < TimeSpan.FromSeconds(3),
            $"a dead server must not hang provisioning — resolved in {sw.ElapsedMilliseconds}ms");
    }

    [TestMethod]
    public void ImplementsPluginContract()
    {
        Assert.IsTrue(typeof(ISymbolResolver).IsAssignableFrom(typeof(HttpSymbolServerResolver)), "is an ISymbolResolver");
        Assert.IsTrue(typeof(IPluginDescription).IsAssignableFrom(typeof(HttpSymbolServerResolver)), "is an IPluginDescription");
        var p = new HttpSymbolServerResolver();
        Assert.IsFalse(string.IsNullOrWhiteSpace(p.GetPluginFriendlyName()));
        Assert.IsFalse(string.IsNullOrWhiteSpace(p.GetPluginClassName()));
    }

    /// <summary>Fake transport: counts calls and returns whatever the supplied function decides. Overrides
    /// both the sync path (HttpClient.Send, which the resolver uses) and the async path, for completeness.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _fn;
        private int _calls;
        public int Calls => _calls;
        public FakeHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> fn) => _fn = fn;

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            return _fn(request, ct);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(_fn(request, ct));
        }
    }
}
