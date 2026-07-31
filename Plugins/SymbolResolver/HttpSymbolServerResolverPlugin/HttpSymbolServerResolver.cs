using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using FindNeedlePluginLib;

namespace HttpSymbolServerResolverPlugin;

/// <summary>
/// Reference <see cref="ISymbolResolver"/> for a REAL network source: an HTTP(S) symbol server (the same
/// protocol shape as msdl / a corporate symsrv HTTP store). Configured via
/// <c>FINDNEEDLE_SYMBOL_SERVERS</c> — a ';'-separated list of base URLs. For each it fetches the standard
/// symbol-server (SSQP) path <c>&lt;base&gt;/&lt;pdb&gt;/&lt;GUID+age&gt;/&lt;pdb&gt;</c>, downloads the
/// matching PDB into a local cache, and returns the CACHED LOCAL path (tracepdb extracts the TMFs from it).
///
/// This is the counterpart to <c>SmbSymbolResolver</c>, which is stateless because the SMB redirector owns
/// the connection. An HTTP resolver owns its own network state, so it exists to show the three things a
/// stateful resolver MUST get right given the plugin lifetime (a fresh instance is constructed each symbol-
/// resolution pass — see the docs on <see cref="ISymbolResolver"/>):
///   1. LIFETIME — the <see cref="HttpClient"/> is <c>static</c>, so it lives for the process and is reused
///      across the throwaway instances. A per-instance (or per-call) HttpClient would leak sockets.
///   2. CACHING — two layers, so a resolver plugin never re-hits the network needlessly:
///        • filesystem: a prior run's download in the local cache short-circuits with no request at all;
///        • in-memory: a <see cref="Lazy{T}"/> in a <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed by
///          the SSQP <see cref="SymbolLookupRequest.Key"/> makes the download run at most ONCE per identity
///          even under concurrent passes, and remembers a miss (negative cache) so an absent PDB isn't
///          re-fetched for every binary that references it.
///   3. FAIL-FAST — a dead/slow server must never hang provisioning. <see cref="HttpClient.Timeout"/>
///      (default 15s, override with <c>FINDNEEDLE_SYMBOL_HTTP_TIMEOUT_MS</c>) bounds every probe, and a
///      timeout on one server falls through to the next instead of stalling the decode.
///
/// Auto-loaded via the registry seam (<c>HKCU\Software\FindNeedle\Plugins</c> → this DLL's absolute path),
/// exactly like the SMB reference plugin. Implements <see cref="IPluginDescription"/> so the plugin
/// subsystem discovers it. Consulted only on the build/extract path, never the cheap diagnostic banner.
/// </summary>
public sealed class HttpSymbolServerResolver : ISymbolResolver, IPluginDescription
{
    /// <summary>';'-separated symbol-server base URLs, e.g.
    /// <c>https://msdl.microsoft.com/download/symbols;http://symbols.corp/store</c>.</summary>
    public const string ServersEnv = "FINDNEEDLE_SYMBOL_SERVERS";

    /// <summary>Optional override for the local download cache (defaults under %LocalAppData%).</summary>
    public const string CacheDirEnv = "FINDNEEDLE_SYMBOL_HTTP_CACHE";

    /// <summary>Optional per-probe timeout in ms (default 15000). Bounds a dead/slow server.</summary>
    public const string TimeoutEnv = "FINDNEEDLE_SYMBOL_HTTP_TIMEOUT_MS";

    private const int DefaultTimeoutMs = 15000;

    // --- Static process-lifetime state (survives the per-pass `new HttpSymbolServerResolver()`) ---

    // One HttpClient for the whole process. NEVER new one up per instance/call — that exhausts sockets.
    private static readonly object _clientLock = new();
    private static HttpClient _client = BuildClient(null);

    // In-memory cache: SSQP key -> resolved local path, or "" for a known miss (negative cache). Lazy so
    // concurrent lookups for the same PDB share a single download. (A negative entry lives for the process;
    // if a server might PUBLISH a PDB mid-session, this is where you'd add a short TTL.)
    private static readonly ConcurrentDictionary<string, Lazy<string>> _inFlight =
        new(StringComparer.OrdinalIgnoreCase);

    // Test-only override, set by ResetForTests; null in production.
    private static string? _cacheDirOverride;

    public string TryResolvePdb(SymbolLookupRequest request)
    {
        if (request == null || string.IsNullOrEmpty(request.PdbFileName)) return null;
        if (!HasServers) return null; // nothing configured → pass immediately, no network

        var finalPath = Path.Combine(CacheDir, request.PdbFileName, request.Key, request.PdbFileName);

        // Layer 2 — the filesystem IS the cache: a prior run already downloaded it → return with no request.
        if (SafeExists(finalPath)) return finalPath;

        // Layer 1 — single-fetch + negative cache. GetOrAdd(Lazy) guarantees DownloadFirstMatch runs at most
        // once per identity even if two provision passes race on the same PDB.
        var lazy = _inFlight.GetOrAdd(request.Key, _ => new Lazy<string>(
            () => DownloadFirstMatch(request, finalPath), LazyThreadSafetyMode.ExecutionAndPublication));

        string path;
        try { path = lazy.Value; }
        catch { _inFlight.TryRemove(request.Key, out _); return null; } // never surface an exception to the host

        // Validate-on-read: a cached hit could have been deleted since (temp cleanup, cache wipe). Evict and
        // re-fetch once rather than hand back a stale path the framework's File.Exists check would reject anyway.
        if (path.Length > 0 && !SafeExists(path))
        {
            _inFlight.TryRemove(request.Key, out _);
            return TryResolvePdb(request);
        }
        return path.Length == 0 ? null : path;
    }

    /// <summary>Try each configured server in order; download the first SSQP-layout hit to the local cache
    /// and return that path. Returns "" (a negative-cache marker) if no server has it. Never throws — a
    /// dead/slow server times out and falls through to the next.</summary>
    private static string DownloadFirstMatch(SymbolLookupRequest req, string finalPath)
    {
        foreach (var server in Servers())
        {
            var url = $"{server.TrimEnd('/')}/{req.PdbFileName}/{req.Key}/{req.PdbFileName}";
            req.Log($"GET {url}");
            try
            {
                using var msg = new HttpRequestMessage(HttpMethod.Get, url);
                // ResponseHeadersRead: decide on the status line before streaming a (possibly large) PDB.
                using var resp = Client.Send(msg, HttpCompletionOption.ResponseHeadersRead);
                if (!resp.IsSuccessStatusCode) { req.Log($"  {(int)resp.StatusCode} — not here"); continue; } // 404 etc → try next

                Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
                // Download to a temp file then move: a concurrent reader (or a crash) never sees a
                // half-written PDB at the cache path — it's either absent or complete.
                var tmp = finalPath + "." + Guid.NewGuid().ToString("N") + ".part";
                using (var net = resp.Content.ReadAsStream())
                using (var file = File.Create(tmp))
                    net.CopyTo(file);
                File.Move(tmp, finalPath, overwrite: true);
                return finalPath;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                          or OperationCanceledException or IOException)
            {
                // Timeout (TaskCanceledException from HttpClient.Timeout), transient network error, or a write
                // race — log quietly and move to the next server. A bad server must not hang or break the decode.
                req.Log($"  failed: {ex.Message}");
                LogQuiet($"http symbol probe failed for {url}: {ex.Message}");
            }
        }
        return ""; // miss across all servers → negative-cache
    }

    // --- config helpers ---

    private static bool HasServers => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ServersEnv));

    private static IEnumerable<string> Servers()
    {
        var v = Environment.GetEnvironmentVariable(ServersEnv);
        if (string.IsNullOrWhiteSpace(v)) yield break;
        foreach (var s in v.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return s;
    }

    /// <summary>Local folder downloads land in (symbol-server layout, so it doubles as a symsrv cache).</summary>
    public static string CacheDir =>
        _cacheDirOverride
        ?? Environment.GetEnvironmentVariable(CacheDirEnv)
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "FindNeedle", "http-symbol-cache");

    private static int TimeoutMs
    {
        get
        {
            var v = Environment.GetEnvironmentVariable(TimeoutEnv);
            return int.TryParse(v, out var ms) && ms > 0 ? ms : DefaultTimeoutMs;
        }
    }

    private static HttpClient Client { get { lock (_clientLock) return _client; } }

    private static HttpClient BuildClient(HttpMessageHandler? handler)
    {
        var c = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        c.Timeout = TimeSpan.FromMilliseconds(TimeoutMs); // the fail-fast bound — a dead server can't hang provisioning
        return c;
    }

    private static bool SafeExists(string path)
    {
        try { return File.Exists(path); } catch { return false; }
    }

    private static void LogQuiet(string message)
    {
        try { Logger.Instance.Log(message); } catch { /* logging must never break resolution */ }
    }

    // --- test seam (CoreTests only, via InternalsVisibleTo) ---
    // Rebuild the static client around a fake handler, point the cache at a temp dir, and clear the memo —
    // so the cache/timeout/fallthrough behavior can be exercised with no real network.
    internal static void ResetForTests(HttpMessageHandler? handler, string? cacheDir)
    {
        lock (_clientLock) { _client.Dispose(); _client = BuildClient(handler); }
        _cacheDirOverride = cacheDir;
        _inFlight.Clear();
    }

    public string GetPluginTextDescription()
        => "Downloads PDBs from the HTTP(S) symbol servers listed in FINDNEEDLE_SYMBOL_SERVERS "
         + "(symbol-server/SSQP layout) into a local cache, so WPP TMFs resolve automatically from a symbol store.";
    public string GetPluginFriendlyName() => "HTTP Symbol Server Resolver";
    public string GetPluginClassName() => IPluginDescription.GetPluginClassNameBase(this);
}
