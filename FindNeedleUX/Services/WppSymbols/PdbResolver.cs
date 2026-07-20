using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;

namespace FindNeedleUX.Services.WppSymbols;

/// <summary>HTTP GET seam so the resolver's server probing is unit-testable without a network.</summary>
public interface ISymbolFetcher
{
    /// <summary>Fetch a URL; null on any failure (404, timeout, DNS), with the reason in
    /// <paramref name="error"/>.</summary>
    byte[] TryGet(string url, out string error);
}

/// <summary>Default fetcher. Sends the symbol-server User-Agent — msdl rejects UA-less requests.</summary>
public sealed class HttpSymbolFetcher : ISymbolFetcher
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Microsoft-Symbol-Server/10.0.0.0");
        return c;
    }

    public byte[] TryGet(string url, out string error)
    {
        error = null;
        try
        {
            using var resp = Http.Send(new HttpRequestMessage(HttpMethod.Get, url),
                                       HttpCompletionOption.ResponseContentRead);
            if (!resp.IsSuccessStatusCode)
            {
                error = $"HTTP {(int)resp.StatusCode}";
                return null;
            }
            using var ms = new MemoryStream();
            resp.Content.ReadAsStream().CopyTo(ms);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            error = ex.InnerException?.Message ?? ex.Message;
            return null;
        }
    }
}

public sealed class PdbResolveResult
{
    /// <summary>Full path of the exact-match PDB, or null when nothing resolved.</summary>
    public string ResolvedPath { get; internal set; }
    public bool Found => ResolvedPath != null;

    /// <summary>Loose candidate files that were found but REJECTED (GUID/age mismatch or
    /// unverifiable) — callers must not extract TMFs from these.</summary>
    public List<string> RejectedLooseCandidates { get; } = new();
}

/// <summary>
/// Managed PDB discovery (issue #4): given a binary's <see cref="PdbIdentity"/>, probe loose
/// folders and the user's symbol path per the documented symstore/SSQP conventions — two-tier
/// stores, <c>file.ptr</c> redirects, compressed <c>.pd_</c> entries (expand.exe), HTTP servers —
/// logging every probe and its outcome. Exact-match: loose PDBs are verified by GUID+age
/// (<see cref="MsfPdbInfo"/>); store hits are exact by construction (the GUID+age is the path key).
/// Server hits are written through to the chain's first local cache (symsrv behavior), or to
/// <c>fallbackCacheDir</c> when the chain has none.
/// </summary>
public sealed class PdbResolver
{
    private readonly ISymbolFetcher _fetcher;

    public PdbResolver(ISymbolFetcher fetcher = null) => _fetcher = fetcher ?? new HttpSymbolFetcher();

    public PdbResolveResult Resolve(
        PdbIdentity id,
        IReadOnlyList<string> looseFolders,
        string symbolPath,
        string fallbackCacheDir,
        StringBuilder log)
    {
        var result = new PdbResolveResult();
        log ??= new StringBuilder();

        // 1) Loose candidates: <folder>\<pdbname>, verified by GUID+age so a stale PDB is never
        //    silently accepted. Each folder is also probed as a store (it may be one).
        foreach (var folder in looseFolders ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) continue;

            var loose = Path.Combine(folder, id.PdbFileName);
            if (File.Exists(loose))
            {
                if (VerifyLoose(loose, id, log, result)) { result.ResolvedPath = loose; return result; }
            }
            else
            {
                log.AppendLine($"  miss (loose): {loose}");
            }

            var storeHit = ProbeDirStore(folder, id, log);
            if (storeHit != null) { result.ResolvedPath = storeHit; return result; }
        }

        // 2) The symbol path: ordered chains; a hit later in a chain backfills the chain's first
        //    local element (write-through cache).
        var chains = SymbolPathParser.Parse(symbolPath, log);
        foreach (var chain in chains)
        {
            string firstLocalCache = null;
            for (int i = 0; i < chain.Count; i++)
            {
                var store = chain[i];
                if (!store.IsHttp)
                {
                    var hit = ProbeDirStore(store.Location, id, log);
                    if (hit != null)
                    {
                        result.ResolvedPath = Backfill(hit, firstLocalCache, id, log);
                        return result;
                    }
                    firstLocalCache ??= store.Location;
                }
                else
                {
                    var target = firstLocalCache ?? fallbackCacheDir;
                    var hit = ProbeHttpStore(store.Location, id, target, log);
                    if (hit != null) { result.ResolvedPath = hit; return result; }
                }
            }
        }

        log.AppendLine($"  NOT RESOLVED: {id} (key {id.Key})");
        return result;
    }

    // ----- loose verification -----

    private static bool VerifyLoose(string pdbPath, PdbIdentity id, StringBuilder log, PdbResolveResult result)
    {
        var info = MsfPdbInfo.TryRead(pdbPath, out var err);
        if (info == null)
        {
            log.AppendLine($"  rejected (can't verify GUID/age — {err}): {pdbPath}");
            result.RejectedLooseCandidates.Add(pdbPath);
            return false;
        }
        if (info.Value.guid != id.Guid || info.Value.age != id.Age)
        {
            log.AppendLine($"  rejected (STALE — has {{{info.Value.guid}}} age {info.Value.age}, " +
                           $"need {{{id.Guid}}} age {id.Age}): {pdbPath}");
            result.RejectedLooseCandidates.Add(pdbPath);
            return false;
        }
        log.AppendLine($"  HIT (loose, GUID+age verified): {pdbPath}");
        return true;
    }

    // ----- directory stores -----

    /// <summary>Probe one local store root: [two-tier prefix]\pdb\KEY\{pdb | pd_ | file.ptr}.</summary>
    private static string ProbeDirStore(string storeRoot, PdbIdentity id, StringBuilder log)
    {
        if (string.IsNullOrWhiteSpace(storeRoot) || !Directory.Exists(storeRoot))
        {
            if (!string.IsNullOrWhiteSpace(storeRoot))
                log.AppendLine($"  miss (store root not found): {storeRoot}");
            return null;
        }

        var root = storeRoot;
        if (File.Exists(Path.Combine(storeRoot, "index2.txt")) && id.PdbFileName.Length >= 2)
            root = Path.Combine(storeRoot, id.PdbFileName.Substring(0, 2)); // two-tier layout

        var keyDir = Path.Combine(root, id.PdbFileName, id.Key);

        var exact = Path.Combine(keyDir, id.PdbFileName);
        if (File.Exists(exact))
        {
            log.AppendLine($"  HIT (store): {exact}");
            return exact;
        }

        var compressed = Path.Combine(keyDir, CompressedName(id.PdbFileName));
        if (File.Exists(compressed))
        {
            var expanded = ExpandInPlace(compressed, exact, log);
            if (expanded != null) { log.AppendLine($"  HIT (store, expanded): {expanded}"); return expanded; }
        }

        var ptr = Path.Combine(keyDir, "file.ptr");
        if (File.Exists(ptr))
        {
            var followed = FollowFilePtr(ptr, exact, log);
            if (followed != null) return followed;
        }

        log.AppendLine($"  miss (store): {keyDir}");
        return null;
    }

    /// <summary>file.ptr content: "PATH:&lt;target&gt;" (or a bare path), or "MSG:&lt;reason&gt;".</summary>
    private static string FollowFilePtr(string ptrFile, string expandTarget, StringBuilder log)
    {
        try
        {
            var content = File.ReadAllText(ptrFile).Trim();
            if (content.StartsWith("MSG:", StringComparison.OrdinalIgnoreCase))
            {
                log.AppendLine($"  miss (file.ptr says: {content.Substring(4).Trim()}): {ptrFile}");
                return null;
            }
            var target = content.StartsWith("PATH:", StringComparison.OrdinalIgnoreCase)
                ? content.Substring(5).Trim() : content;
            if (!File.Exists(target))
            {
                log.AppendLine($"  miss (file.ptr target not found: {target}): {ptrFile}");
                return null;
            }
            if (target.EndsWith("_", StringComparison.Ordinal))
            {
                var expanded = ExpandInPlace(target, expandTarget, log);
                if (expanded != null) { log.AppendLine($"  HIT (file.ptr, expanded): {expanded}"); return expanded; }
                return null;
            }
            log.AppendLine($"  HIT (file.ptr): {target}");
            return target;
        }
        catch (Exception ex)
        {
            log.AppendLine($"  miss (file.ptr unreadable — {ex.Message}): {ptrFile}");
            return null;
        }
    }

    // ----- HTTP stores -----

    private string ProbeHttpStore(string baseUrl, PdbIdentity id, string targetStoreRoot, StringBuilder log)
    {
        var baseTrimmed = baseUrl.TrimEnd('/');
        var name = Uri.EscapeDataString(id.PdbFileName);
        var compressedName = Uri.EscapeDataString(CompressedName(id.PdbFileName));

        // Uppercase key first (symsrv convention), lowercase second (strict SSQP servers).
        foreach (var key in new[] { id.Key, id.KeyLower })
        {
            foreach (var (fileName, isCompressed) in new[] { (name, false), (compressedName, true) })
            {
                var url = $"{baseTrimmed}/{name}/{key}/{fileName}";
                var bytes = _fetcher.TryGet(url, out var err);
                if (bytes == null)
                {
                    log.AppendLine($"  miss ({err ?? "no data"}): {url}");
                    continue;
                }
                log.AppendLine($"  HIT (server, {bytes.Length:N0} bytes): {url}");
                return Materialize(bytes, isCompressed, targetStoreRoot, id, log);
            }
            if (id.Key == id.KeyLower) break; // age had no letters and guid was digit-only — same key
        }
        return null;
    }

    /// <summary>Write downloaded bytes into <paramref name="storeRoot"/> in store layout
    /// (pdb\KEY\pdb), expanding compressed payloads, and return the final PDB path.</summary>
    private static string Materialize(byte[] bytes, bool isCompressed, string storeRoot, PdbIdentity id, StringBuilder log)
    {
        try
        {
            var keyDir = Path.Combine(storeRoot, id.PdbFileName, id.Key);
            Directory.CreateDirectory(keyDir);
            var final = Path.Combine(keyDir, id.PdbFileName);
            if (!isCompressed)
            {
                File.WriteAllBytes(final, bytes);
                return final;
            }
            var packed = Path.Combine(keyDir, CompressedName(id.PdbFileName));
            File.WriteAllBytes(packed, bytes);
            return ExpandInPlace(packed, final, log);
        }
        catch (Exception ex)
        {
            log.AppendLine($"  download could not be cached ({ex.Message}) under {storeRoot}");
            return null;
        }
    }

    /// <summary>Copy a store hit into the chain's cache store (symsrv write-through); on any
    /// failure the original hit is still returned.</summary>
    private static string Backfill(string hitPath, string cacheStoreRoot, PdbIdentity id, StringBuilder log)
    {
        if (string.IsNullOrWhiteSpace(cacheStoreRoot)) return hitPath;
        try
        {
            var keyDir = Path.Combine(cacheStoreRoot, id.PdbFileName, id.Key);
            Directory.CreateDirectory(keyDir);
            var cached = Path.Combine(keyDir, id.PdbFileName);
            if (!File.Exists(cached)) File.Copy(hitPath, cached);
            log.AppendLine($"  cached to: {cached}");
            return cached;
        }
        catch (Exception ex)
        {
            log.AppendLine($"  cache write-through failed ({ex.Message}); using {hitPath}");
            return hitPath;
        }
    }

    // ----- helpers -----

    /// <summary>Store convention for compressed entries: last character becomes '_' ("foo.pdb" → "foo.pd_").</summary>
    internal static string CompressedName(string fileName)
        => fileName.Length == 0 ? fileName : fileName.Substring(0, fileName.Length - 1) + "_";

    /// <summary>Single-file expand.exe (CAB) decompression; in-box on every Windows install —
    /// same choice as ArchiveExtractor's .cab support.</summary>
    private static string ExpandInPlace(string compressedPath, string targetPath, StringBuilder log)
    {
        try
        {
            var expand = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "expand.exe");
            if (!File.Exists(expand)) expand = "expand.exe";
            var psi = new ProcessStartInfo
            {
                FileName = expand,
                // Single-file form: expand <source> <destination>. (-R renames into a directory —
                // wrong for an explicit target file; see the .cab arg quirks in ArchiveExtractor.)
                Arguments = $"\"{compressedPath}\" \"{targetPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            p.StandardOutput.ReadToEnd();
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit(60_000);
            if (p.ExitCode == 0 && File.Exists(targetPath)) return targetPath;
            log.AppendLine($"  expand.exe failed (exit {p.ExitCode}{(string.IsNullOrWhiteSpace(err) ? "" : ": " + err.Trim())}): {compressedPath}");
            return null;
        }
        catch (Exception ex)
        {
            log.AppendLine($"  expand.exe failed ({ex.Message}): {compressedPath}");
            return null;
        }
    }
}
