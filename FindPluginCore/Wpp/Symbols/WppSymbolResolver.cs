using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using FindNeedleCoreUtils;
using FindNeedlePluginLib;
using findneedle.PluginSubsystem;
using Microsoft.Win32;

namespace FindPluginCore.Wpp.Symbols;

/// <summary>
/// Builds WPP TMF files from symbols into a managed cache (<see cref="TmfCacheDir"/>) that provisioning
/// puts on <c>TRACE_FORMAT_SEARCH_PATH</c>. PDB DISCOVERY is done in managed code (issue #4): each
/// binary's expected PDB name + GUID + age is read from its PE debug directory
/// (<see cref="WppSymbols.PdbIdentity"/>) and resolved through loose folders and the user's symbol
/// path by <see cref="WppSymbols.PdbResolver"/> (symstore/SSQP conventions: two-tier stores,
/// file.ptr, compressed .pd_, HTTP servers), with every probe logged. The WDK's <c>tracepdb</c> is
/// then used ONLY as the TMF extractor (<c>-f &lt;resolved.pdb&gt;</c>) — its opaque <c>-i</c>
/// resolution mode is no longer used. Loose PDBs that mismatch a binary's GUID/age are rejected
/// loudly, never silently extracted.
/// </summary>
public static class WppSymbolResolver
{
    /// <summary>Managed folder where built TMFs land (always added to TRACE_FORMAT_SEARCH_PATH).</summary>
    public static string TmfCacheDir => Path.Combine(FileIO.GetAppDataFindNeedlePluginFolder(), "tmf-cache");

    /// <summary>Managed store (symstore layout) where server-resolved PDBs land when the user's
    /// symbol path has no local cache element of its own.</summary>
    public static string PdbCacheDir => Path.Combine(FileIO.GetAppDataFindNeedlePluginFolder(), "pdb-cache");

    // --- Test seams (only the unit-test assembly sees these, via InternalsVisibleTo) ---
    // Let orchestration tests run BuildTmfs's discovery/skip/extract decisions on a machine (CI)
    // with no WDK: fake the tracepdb location and capture invocations instead of spawning it.
    internal static Func<string> FindTracePdbOverride;
    internal static Action<string, string, StringBuilder> RunTracePdbOverride;
    // Inject fake ISymbolResolver plugins without going through PluginManager (which loads DLLs).
    internal static IReadOnlyList<ISymbolResolver> ResolversOverride;
    internal static void ResetOverridesForTests() { FindTracePdbOverride = null; RunTracePdbOverride = null; ResolversOverride = null; TmfResolversOverride = null; ResolverTimeoutMsForTests = 0; }

    /// <summary>The registered symbol-resolver plugins (SMB share / symbol server / …), consulted when the
    /// built-in local + symbol-path lookup misses. Best-effort: an unavailable plugin subsystem = none.</summary>
    private static IReadOnlyList<ISymbolResolver> GetSymbolResolvers()
    {
        if (ResolversOverride != null) return ResolversOverride;
        try { return PluginManager.GetSingleton().GetAllPluginsInstancesOfAType<ISymbolResolver>(); }
        catch { return Array.Empty<ISymbolResolver>(); }
    }

    /// <summary>Env override for the per-resolver hang backstop, in ms. Generous by default (see
    /// <see cref="ResolverTimeoutMs"/>).</summary>
    public const string ResolverTimeoutEnv = "FINDNEEDLE_SYMBOL_RESOLVER_TIMEOUT_MS";
    // 5 minutes. Real resolvers legitimately take >2 min (large PDB over a slow link / symbol-server round
    // trips), so the old 2-minute default cut them off and silently discarded the symbols they DID fetch.
    // This bounds a genuine HANG, not a slow-but-progressing fetch; raise further via ResolverTimeoutEnv or
    // the CLI's --resolver-timeout=<seconds>.
    private const int DefaultResolverTimeoutMs = 300_000;
    // Test hook: >0 overrides env/default so a hang test doesn't have to wait 2 minutes.
    internal static int ResolverTimeoutMsForTests;

    /// <summary>Per-resolver hang backstop. A third-party <see cref="ISymbolResolver"/> is untrusted code
    /// doing network I/O; if one never returns (dead socket, deadlock) it must not stall the whole decode.
    /// Deliberately GENEROUS — this bounds HANGS, not slow-but-progressing downloads (a resolver pulling a
    /// large PDB over a slow link should still get room; raise it via <see cref="ResolverTimeoutEnv"/>).</summary>
    private static int ResolverTimeoutMs
    {
        get
        {
            if (ResolverTimeoutMsForTests > 0) return ResolverTimeoutMsForTests;
            var v = Environment.GetEnvironmentVariable(ResolverTimeoutEnv);
            return int.TryParse(v, out var ms) && ms > 0 ? ms : DefaultResolverTimeoutMs;
        }
    }

    /// <summary>Ask each resolver plugin, in order, to find the PDB for this identity. Returns the first
    /// non-null path that exists on disk (local or UNC), or null. Plugin exceptions AND hangs are logged
    /// and skipped — no single resolver can stall or crash the build.</summary>
    private static string TryResolverPlugins(IReadOnlyList<ISymbolResolver> resolvers,
        WppSymbols.PdbIdentity id, string binary, StringBuilder sb)
    {
        if (resolvers == null || resolvers.Count == 0) return null;
        int timeoutMs = ResolverTimeoutMs;
        foreach (var r in resolvers)
        {
            // The plugin's own diagnostics go to a per-call queue (the resolver may run on a pool thread under
            // the timeout, so it can't touch sb directly); we drain it into sb on THIS thread, attributed.
            var pluginLog = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var request = new SymbolLookupRequest(id.PdbFileName, id.Guid, id.Age, binary) { Log = pluginLog.Enqueue };
            bool ok = InvokeResolverBounded(r, request, timeoutMs, out var path, out var error);
            while (pluginLog.TryDequeue(out var line)) sb.AppendLine($"  [{r.GetType().Name}] {line}");
            if (!ok) { sb.AppendLine($"symbol resolver {r.GetType().Name} skipped: {error}"); continue; }
            if (string.IsNullOrEmpty(path)) continue;
            if (File.Exists(path)) { sb.AppendLine($"resolved via plugin {r.GetType().Name}: {path}"); return path; }
            sb.AppendLine($"symbol resolver {r.GetType().Name} returned a missing path: {path}");
        }
        return null;
    }

    private static bool InvokeResolverBounded(ISymbolResolver r, SymbolLookupRequest request, int timeoutMs,
        out string path, out string error)
        => RunBounded(() => r.TryResolvePdb(request), timeoutMs, out path, out error);

    /// <summary>Run a resolver call with a bounded wait. A resolver is synchronous and carries no
    /// cancellation, so a hung one can't be truly cancelled — but we can stop WAITING for it: run it on a
    /// pool thread and abandon it if it blows the budget. The abandoned task's eventual fault is observed so
    /// it can never resurface as an unobserved-exception crash. Returns true (with <paramref name="path"/>)
    /// only when the call returned within the budget without throwing.</summary>
    private static bool RunBounded(Func<string> call, int timeoutMs, out string path, out string error)
    {
        path = null; error = null;
        var task = System.Threading.Tasks.Task.Run(call);
        bool completed;
        try { completed = task.Wait(timeoutMs); }
        catch (Exception ex) { error = $"threw: {(ex is AggregateException ae ? ae.GetBaseException() : ex).Message}"; return false; }
        if (!completed)
        {
            error = $"timed out after {timeoutMs} ms (abandoned)";
            task.ContinueWith(t => { _ = t.Exception; },
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted |
                System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously);
            return false;
        }
        path = task.Result;
        return true;
    }

    // --- GUID-driven TMF resolution (the ETL-only path — no binaries to read a PDB identity from) ---

    /// <summary>Inject fake IWppTmfResolver plugins in tests without going through PluginManager.</summary>
    internal static IReadOnlyList<IWppTmfResolver> TmfResolversOverride;

    /// <summary>The registered TMF-store resolver plugins, consulted with a missing message GUID when there's
    /// no binary to drive the PDB path. Best-effort: an unavailable plugin subsystem = none.</summary>
    private static IReadOnlyList<IWppTmfResolver> GetTmfResolvers()
    {
        if (TmfResolversOverride != null) return TmfResolversOverride;
        try { return PluginManager.GetSingleton().GetAllPluginsInstancesOfAType<IWppTmfResolver>(); }
        catch { return Array.Empty<IWppTmfResolver>(); }
    }

    /// <summary>For each missing WPP message GUID, ask the <see cref="IWppTmfResolver"/> plugins for a TMF and
    /// copy the first hit into <paramref name="cacheDir"/> as <c>&lt;guid&gt;.tmf</c>. Keyed purely by GUID —
    /// no binary, no tracepdb — so it works for captures that ship only ETLs. GUIDs whose TMF is already
    /// cached are skipped. Each resolver call is bounded by the same hang backstop as the PDB path. Returns
    /// the number of TMFs newly written.</summary>
    internal static int ProvisionTmfsByGuid(IReadOnlyCollection<string> missingGuids, string etlPath,
        string cacheDir, StringBuilder sb)
    {
        var resolvers = GetTmfResolvers();
        if (resolvers.Count == 0 || missingGuids == null || missingGuids.Count == 0) return 0;

        int timeoutMs = ResolverTimeoutMs;
        int written = 0;
        try { Directory.CreateDirectory(cacheDir); } catch { return 0; }

        foreach (var gs in missingGuids)
        {
            if (!Guid.TryParse(gs, out var guid)) continue;
            var dest = Path.Combine(cacheDir, guid.ToString("D") + ".tmf");
            if (File.Exists(dest)) continue; // already provisioned this GUID (GUID-level dedup via the cache)

            foreach (var r in resolvers)
            {
                // Per-call log queue drained into sb on THIS thread (the resolver may run on a pool thread
                // under the timeout). Attributed by resolver name. Drained in finally so every exit path flushes.
                var pluginLog = new System.Collections.Concurrent.ConcurrentQueue<string>();
                var request = new WppTmfResolveRequest(guid, etlPath) { Log = pluginLog.Enqueue };
                bool hit = false;
                try
                {
                    // (a) resolver points at an existing .tmf FILE
                    if (!RunBounded(() => r.TryResolveTmf(request), timeoutMs, out var path, out var error))
                    {
                        sb.AppendLine($"tmf resolver {r.GetType().Name} skipped for {guid:D}: {error}");
                        continue;
                    }
                    if (!string.IsNullOrEmpty(path))
                    {
                        if (!File.Exists(path)) { sb.AppendLine($"tmf resolver {r.GetType().Name} returned a missing path: {path}"); continue; }
                        try
                        {
                            File.Copy(path, dest, overwrite: true);
                            written++; hit = true;
                            sb.AppendLine($"resolved TMF for {guid:D} via plugin {r.GetType().Name}: {path}");
                        }
                        catch (Exception ex) { sb.AppendLine($"failed to cache TMF {path}: {ex.Message}"); continue; }
                    }
                    else
                    {
                        // (b) resolver GENERATES the .tmf text itself (no file on disk)
                        if (!RunBounded(() => r.TryResolveTmfText(request), timeoutMs, out var text, out var terror))
                        {
                            sb.AppendLine($"tmf resolver {r.GetType().Name} (text) skipped for {guid:D}: {terror}");
                            continue;
                        }
                        if (!string.IsNullOrEmpty(text))
                        {
                            try
                            {
                                File.WriteAllText(dest, text);
                                written++; hit = true;
                                sb.AppendLine($"resolved TMF text for {guid:D} via plugin {r.GetType().Name}");
                            }
                            catch (Exception ex) { sb.AppendLine($"failed to write TMF text for {guid:D}: {ex.Message}"); }
                        }
                    }
                }
                finally
                {
                    while (pluginLog.TryDequeue(out var line)) sb.AppendLine($"  [{r.GetType().Name}] {line}");
                }
                if (hit) break; // first resolver with a hit wins
            }
        }
        return written;
    }

    public static string FindTracePdb()
    {
        if (FindTracePdbOverride != null) return FindTracePdbOverride();
        try
        {
            var kits = (string)Registry.LocalMachine
                .OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots")?
                .GetValue("KitsRoot10");
            if (string.IsNullOrEmpty(kits)) return null;
            var bin = Path.Combine(kits, "bin");
            if (!Directory.Exists(bin)) return null;
            return Directory.GetFiles(bin, "tracepdb.exe", SearchOption.AllDirectories)
                .Where(p => p.Contains(@"\x64\", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p)
                .LastOrDefault();
        }
        catch { return null; }
    }

    /// <summary>
    /// Extract TMFs from the given source folders (PDBs, and binaries via <paramref name="symbolPath"/>)
    /// into the managed cache. Returns the total TMF count now in the cache and a diagnostic log.
    /// </summary>
    public static BuildTmfsResult BuildTmfs(string sourceFolders, string symbolPath)
    {
        var sb = new StringBuilder();
        var outcomes = new List<SymbolOutcome>();
        var tracepdb = FindTracePdb();
        if (string.IsNullOrEmpty(tracepdb))
            return new BuildTmfsResult
            {
                TmfCount = CountTmfs(),
                Log = "tracepdb.exe not found — install the Windows SDK/WDK (Debugging Tools).",
                Outcomes = outcomes,
            };
        sb.AppendLine($"tracepdb: {tracepdb}");

        var cache = TmfCacheDir;
        Directory.CreateDirectory(cache);

        var folders = SplitFolders(sourceFolders);
        if (folders.Count == 0)
            sb.AppendLine("No PDB/binary source folder configured.");

        var resolver = new WppSymbols.PdbResolver();
        var symbolResolverPlugins = GetSymbolResolvers(); // consulted once we've exhausted the built-in lookup
        if (symbolResolverPlugins.Count > 0)
            sb.AppendLine($"{symbolResolverPlugins.Count} symbol-resolver plugin(s) available as a fallback.");
        // PDB paths already extracted (or rejected as stale) this run — so the loose-PDB sweep
        // below neither re-extracts a resolved PDB nor touches one that failed verification.
        var extracted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rejected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder)) { sb.AppendLine($"skip (not found): {folder}"); continue; }

            // 1) Binaries: read the exact PDB identity from the PE, resolve it ourselves (loose
            //    folders first, then the symbol path), then let tracepdb extract from the match.
            foreach (var pattern in new[] { "*.dll", "*.exe", "*.sys" })
            {
                foreach (var binary in SafeEnumerate(folder, pattern))
                {
                    var id = WppSymbols.PdbIdentity.TryReadFromBinary(binary, out var idErr);
                    if (id == null)
                    {
                        sb.AppendLine($"skip {Path.GetFileName(binary)}: {idErr}");
                        continue;
                    }
                    sb.AppendLine($"{Path.GetFileName(binary)} needs: {id} (key {id.Key})");
                    var res = resolver.Resolve(id, folders, symbolPath, PdbCacheDir, sb);
                    foreach (var r in res.RejectedLooseCandidates) rejected.Add(r);

                    // Built-in lookup first; if it missed, give the resolver plugins (SMB share, symbol
                    // server, …) a shot before declaring the binary unresolved.
                    string resolvedPath = res.Found ? res.ResolvedPath : TryResolverPlugins(symbolResolverPlugins, id, binary, sb);
                    if (resolvedPath == null)
                    {
                        sb.AppendLine($"FAILED to resolve {id.PdbFileName} for {Path.GetFileName(binary)} — probes above show every location tried.");
                        outcomes.Add(NotFoundOrWrong(binary, id, res));
                        continue;
                    }
                    if (extracted.Add(resolvedPath))
                    {
                        // Snapshot-diff (not count-based) so re-extracting a PDB whose TMFs are
                        // already cached — same files rewritten, count unchanged — doesn't misfire.
                        var beforeTmfs = SnapshotTmfs(cache);
                        Run(tracepdb, $"-f \"{resolvedPath}\" -p \"{cache}\"", sb);
                        // The most confusing failure mode is "everything resolved, still no TMFs".
                        // Per Microsoft's docs, WPP trace-format data is STRIPPED from public
                        // symbols (tracepdb needs the full/private PDB); since Win8 a component can
                        // opt individual trace functions into its public PDB, but most don't.
                        // A GUID+age match can't tell the two apart — a stripped PDB keeps the
                        // private one's identity — so explain it here, at extraction time.
                        bool producedTmf = AnyTmfChanged(cache, beforeTmfs);
                        if (!producedTmf)
                            sb.AppendLine(
                                $"note: {id.PdbFileName} matched (GUID+age) but produced no TMFs — it carries no " +
                                "WPP trace-format data. Either the binary doesn't use WPP, or this is a " +
                                "public/stripped PDB (symbol servers like msdl strip TMF data; WPP decoding " +
                                "needs the component's private PDB).");
                        outcomes.Add(new SymbolOutcome
                        {
                            Status = producedTmf ? SymbolStatus.Resolved : SymbolStatus.NoTmf,
                            Binary = Path.GetFileName(binary),
                            PdbName = id.PdbFileName,
                            Guid = id.Guid.ToString("N").ToUpperInvariant(),
                            Age = id.Age,
                            ResolvedPath = resolvedPath,
                            Detail = producedTmf ? $"TMF extracted from {resolvedPath}"
                                                 : "PDB matched but carries no WPP trace-format data (public/stripped PDB)",
                        });
                    }
                }
            }

            // 2) Loose PDBs with no binary alongside (the "folder of PDBs" workflow) — extracted
            //    as before, EXCEPT files a binary's GUID/age check explicitly rejected.
            foreach (var pdb in SafeEnumerate(folder, "*.pdb", recurse: true))
            {
                if (extracted.Contains(pdb)) continue;
                if (rejected.Contains(pdb))
                {
                    sb.AppendLine($"skip (stale — rejected by GUID/age check above): {pdb}");
                    continue;
                }
                if (extracted.Add(pdb))
                {
                    var beforeTmfs = SnapshotTmfs(cache);
                    Run(tracepdb, $"-f \"{pdb}\" -p \"{cache}\"", sb);
                    bool producedTmf = AnyTmfChanged(cache, beforeTmfs);
                    outcomes.Add(new SymbolOutcome
                    {
                        Status = producedTmf ? SymbolStatus.Resolved : SymbolStatus.NoTmf,
                        PdbName = Path.GetFileName(pdb),
                        ResolvedPath = pdb,
                        Detail = producedTmf ? "TMF extracted (loose PDB, no binary to verify against)"
                                             : "loose PDB carries no WPP trace-format data",
                    });
                }
            }
        }

        int count = CountTmfs();
        sb.AppendLine($"TMF cache now holds {count} file(s): {cache}");
        return new BuildTmfsResult { TmfCount = count, Log = sb.ToString(), Outcomes = outcomes };
    }

    // On-demand provisioning serializes (tracepdb writes a shared TMF cache) and remembers which source
    // folders it already swept this session, so a second ETL from the same drop doesn't re-run the
    // (possibly network) resolve. Cleared implicitly per process launch.
    private static readonly object _provisionLock = new();
    private static readonly HashSet<string> _provisionedSources = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// On-demand symbol provisioning for the DECODE path, registered as
    /// <see cref="FindNeedlePluginLib.WppSymbolProvisioning.Handler"/>. When a WPP ETL fails to decode for
    /// missing TMFs, sweep the ETL's own folder plus the configured symbol-source folder(s) for
    /// binaries/PDBs, resolve them (built-in lookup, then the <see cref="ISymbolResolver"/> plugins — SMB
    /// share, symbol server, …), extract their TMFs into the managed cache via <see cref="BuildTmfs"/>, and
    /// refresh <c>TRACE_FORMAT_SEARCH_PATH</c>. Returns true if NEW TMFs were produced (so the caller
    /// retries the decode). Each source folder is swept at most once per session. Never throws.
    /// </summary>
    public static bool TryProvision(FindNeedlePluginLib.WppProvisionRequest request, string symbolSourcePath, string symbolPath)
    {
        if (request == null) return false;
        lock (_provisionLock)
        {
            try
            {
                // Sources: the ETL's own folder (captures often ship binaries alongside the trace) + the
                // user's configured symbol-source folder(s). Symbol path (servers / PDB stores) is passed
                // through so the resolver can pull a PDB it can't find locally.
                var sources = new List<string>();
                try
                {
                    var dir = Path.GetDirectoryName(request.EtlPath);
                    if (!string.IsNullOrEmpty(dir)) sources.Add(dir);
                }
                catch { /* bad path — just skip the ETL folder */ }
                sources.AddRange(SplitFolders(symbolSourcePath));

                // Only sweep folders we haven't already tried this session (idempotent; skips repeat
                // network hits when many ETLs share a drop).
                int before = CountTmfs();

                // 1) Binary-driven sweep: the ETL's folder + configured symbol sources, once per folder/session.
                //    Reads each binary's PDB identity, resolves it (built-in + ISymbolResolver plugins), extracts
                //    TMFs with tracepdb. Skipped when a capture ships no binaries — that's what step 2 is for.
                var fresh = sources.Where(s => _provisionedSources.Add(s)).ToList();
                if (fresh.Count > 0)
                {
                    BuildTmfs(string.Join(";", fresh), symbolPath);
                    EnsureTmfCacheOnSearchPath();
                }

                // 2) GUID-driven TMF resolution: for the ETL-only case (no binary to read a PDB identity from),
                //    ask the IWppTmfResolver plugins for each missing message GUID directly. GUID-keyed, so it
                //    runs every time (not folder-deduped) and needs no binary/tracepdb.
                var tmfLog = new StringBuilder();
                int tmfWritten = ProvisionTmfsByGuid(request.MissingMessageGuids, request.EtlPath, TmfCacheDir, tmfLog);
                if (tmfWritten > 0)
                {
                    EnsureTmfCacheOnSearchPath(); // put the newly-cached TMFs on TRACE_FORMAT_SEARCH_PATH
                    FindNeedlePluginLib.Logger.Instance.Log(tmfLog.ToString().TrimEnd());
                }

                int after = CountTmfs();
                FindNeedlePluginLib.Logger.Instance.Log(
                    $"WPP provision for {request.EtlPath}: swept {fresh.Count} folder(s), {tmfWritten} TMF(s) via GUID resolvers, TMF cache {before}->{after}");
                return after > before;
            }
            catch (Exception ex)
            {
                FindNeedlePluginLib.Logger.Instance.Log(
                    $"WPP symbol provision failed for {request?.EtlPath}: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Discovery-only: for each binary in <paramref name="sourceFolders"/>, read its expected PDB
    /// identity and resolve it against loose folders + LOCAL stores only — no extraction, no network.
    /// Returns a per-binary status so callers (e.g. the decode-warning banner) can name the exact PDB
    /// each binary needs and whether it's missing or the WRONG build. Fast and side-effect-free;
    /// bounded by <paramref name="maxBinaries"/> so it stays cheap enough to run on the banner path.
    /// </summary>
    public static IReadOnlyList<SymbolOutcome> Diagnose(string sourceFolders, string symbolPath, int maxBinaries = 64)
    {
        var outcomes = new List<SymbolOutcome>();
        var folders = SplitFolders(sourceFolders);
        if (folders.Count == 0) return outcomes;
        var resolver = new WppSymbols.PdbResolver(new NullFetcher()); // local-only: HTTP probes no-op
        var sink = new StringBuilder();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // A local-only diagnosis can't see PDBs on a symbol server; when one is configured, say so on
        // the "not found" rows rather than implying they're unrecoverable (Build DOES probe the server).
        bool hasHttp = HasHttpSymbolServer(symbolPath);
        int scanned = 0;
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var pattern in new[] { "*.dll", "*.exe", "*.sys" })
                foreach (var binary in SafeEnumerate(folder, pattern))
                {
                    if (scanned >= maxBinaries) return outcomes;
                    scanned++;
                    var id = WppSymbols.PdbIdentity.TryReadFromBinary(binary, out _);
                    if (id == null) continue;
                    if (!seenKeys.Add(id.Key)) continue; // one line per distinct PDB identity
                    var res = resolver.Resolve(id, folders, symbolPath, PdbCacheDir, sink);
                    if (res.Found)
                        outcomes.Add(new SymbolOutcome
                        {
                            Status = SymbolStatus.FoundLocal,
                            Binary = Path.GetFileName(binary),
                            PdbName = id.PdbFileName,
                            Guid = id.Guid.ToString("N").ToUpperInvariant(),
                            Age = id.Age,
                            ResolvedPath = res.ResolvedPath,
                            Detail = $"PDB present at {res.ResolvedPath} — Build TMFs to extract",
                        });
                    else
                    {
                        var oc = NotFoundOrWrong(binary, id, res);
                        if (oc.Status == SymbolStatus.NotFound && hasHttp)
                            oc = new SymbolOutcome
                            {
                                Status = SymbolStatus.NotFound,
                                Binary = oc.Binary, PdbName = oc.PdbName, Guid = oc.Guid, Age = oc.Age,
                                Detail = oc.Detail + " — a symbol server is configured, so Build & reopen may still fetch it",
                            };
                        outcomes.Add(oc);
                    }
                }
        }
        return outcomes;
    }

    /// <summary>True when the symbol path names any HTTP(S) symbol server. A local-only
    /// <see cref="Diagnose"/> can't see server PDBs, so callers use this to caveat "not found".</summary>
    public static bool HasHttpSymbolServer(string symbolPath)
    {
        try
        {
            foreach (var chain in WppSymbols.SymbolPathParser.Parse(symbolPath ?? "", null))
                foreach (var store in chain)
                    if (store.IsHttp) return true;
        }
        catch { /* malformed path — treat as no server */ }
        return false;
    }

    /// <summary>
    /// Deterministic synthetic outcomes for exercising the resolution UI at scale (e.g. the dev
    /// "1000 providers, 75% missing" simulation) — no binaries, no I/O, no randomness. The missing
    /// ones are interspersed (not clustered) so the list looks like a real mixed result. Roughly
    /// <paramref name="missingFraction"/> of the entries are <see cref="SymbolStatus.NotFound"/>; a
    /// few of the resolved ones are marked <see cref="SymbolStatus.WrongVersion"/> so the UI shows
    /// every state a real fix-up would surface.
    /// </summary>
    public static List<SymbolOutcome> GenerateSimulatedOutcomes(int count, double missingFraction)
    {
        var list = new List<SymbolOutcome>(Math.Max(0, count));
        missingFraction = Math.Clamp(missingFraction, 0, 1);
        // Resolve every k-th entry so ~missingFraction are missing, interspersed.
        int resolvedStride = missingFraction >= 1 ? int.MaxValue
                            : Math.Max(1, (int)Math.Round(1.0 / (1.0 - missingFraction)));
        for (int i = 0; i < count; i++)
        {
            var guid = (i.ToString("X8") + new string('0', 24)).Substring(0, 32); // deterministic 32-hex
            int age = (i % 20) + 1;
            var name = $"provider{i:D4}";
            bool missing = (i % resolvedStride) != 0;
            if (missing)
            {
                // Every 7th missing one is a "wrong build present" rather than absent, for variety.
                bool wrong = (i % 7) == 0;
                list.Add(new SymbolOutcome
                {
                    Status = wrong ? SymbolStatus.WrongVersion : SymbolStatus.NotFound,
                    Binary = $"{name}.dll",
                    PdbName = $"{name}.pdb",
                    Guid = guid,
                    Age = age,
                    Detail = wrong
                        ? $"found {name}.pdb but it's a different build (has {(i.ToString("X8") + new string('1', 24)).Substring(0, 32)} age {age + 1}, need {guid} age {age})"
                        : $"not found in the symbol source or symbol path (need {guid} age {age})",
                });
            }
            else
            {
                list.Add(new SymbolOutcome
                {
                    Status = SymbolStatus.Resolved,
                    Binary = $"{name}.dll",
                    PdbName = $"{name}.pdb",
                    Guid = guid,
                    Age = age,
                    Detail = "TMF extracted",
                });
            }
        }
        return list;
    }

    /// <summary>Classify a binary whose PDB did NOT resolve: either the user pointed us at a
    /// wrong-build PDB (right name, wrong GUID/age) or nothing was found at all.</summary>
    private static SymbolOutcome NotFoundOrWrong(string binary, WppSymbols.PdbIdentity id, WppSymbols.PdbResolveResult res)
    {
        var guidHex = id.Guid.ToString("N").ToUpperInvariant();
        var need = $"{guidHex} age {id.Age}";
        if (res.RejectedLooseCandidates.Count > 0)
        {
            var bad = res.RejectedLooseCandidates[0];
            var info = WppSymbols.MsfPdbInfo.TryRead(bad, out _);
            var detail = info != null
                ? $"found {id.PdbFileName} but it's a different build " +
                  $"(has {info.Value.guid.ToString("N").ToUpperInvariant()} age {info.Value.age}, need {need})"
                : $"found {id.PdbFileName} but its version couldn't be verified (need {need})";
            return new SymbolOutcome
            {
                Status = SymbolStatus.WrongVersion,
                Binary = Path.GetFileName(binary),
                PdbName = id.PdbFileName,
                Guid = guidHex,
                Age = id.Age,
                Detail = detail,
            };
        }
        return new SymbolOutcome
        {
            Status = SymbolStatus.NotFound,
            Binary = Path.GetFileName(binary),
            PdbName = id.PdbFileName,
            Guid = guidHex,
            Age = id.Age,
            Detail = $"not found in the symbol source or symbol path (need {need})",
        };
    }

    private static List<string> SplitFolders(string sourceFolders) =>
        (sourceFolders ?? "")
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

    /// <summary>Make sure the managed TMF cache is on <c>TRACE_FORMAT_SEARCH_PATH</c> so the decode
    /// retry finds the TMFs we just extracted. Host-agnostic (the GUI additionally layers the user's
    /// configured TMF folder + ambient path via its own settings exporter); prepending the cache is the
    /// one thing provisioning must guarantee. Idempotent — no-op if the cache is already on the path.</summary>
    private static void EnsureTmfCacheOnSearchPath()
    {
        try
        {
            const string TmfVar = "TRACE_FORMAT_SEARCH_PATH";
            var cache = TmfCacheDir;
            var cur = Environment.GetEnvironmentVariable(TmfVar) ?? "";
            var parts = cur.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Any(p => string.Equals(p.Trim(), cache, StringComparison.OrdinalIgnoreCase))) return;
            Environment.SetEnvironmentVariable(
                TmfVar, string.IsNullOrEmpty(cur) ? cache : cache + ";" + cur);
        }
        catch { /* env not writable — decode retry will just miss, same as before provisioning */ }
    }

    /// <summary>A fetcher that never touches the network, so <see cref="Diagnose"/>'s HTTP store
    /// probes miss instantly — a diagnosis can't block on a slow/absent symbol server.</summary>
    private sealed class NullFetcher : WppSymbols.ISymbolFetcher
    {
        public byte[] TryGet(string url, out string error) { error = "network skipped (local diagnosis)"; return null; }
    }

    private static IEnumerable<string> SafeEnumerate(string folder, string pattern, bool recurse = false)
    {
        try
        {
            return Directory.EnumerateFiles(folder, pattern,
                recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).ToList();
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }

    /// <summary>Per-file write times of the cache's TMFs, taken right before an extraction so
    /// <see cref="AnyTmfChanged"/> can tell whether THAT extraction wrote anything (a rewrite of an
    /// already-cached TMF bumps its timestamp; a wall-clock window would false-positive on files
    /// another recent run just wrote).</summary>
    private static Dictionary<string, DateTime> SnapshotTmfs(string cacheDir)
    {
        var snap = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var f in Directory.EnumerateFiles(cacheDir, "*.tmf"))
                snap[f] = File.GetLastWriteTimeUtc(f);
        }
        catch { /* unreadable cache — empty snapshot */ }
        return snap;
    }

    private static bool AnyTmfChanged(string cacheDir, Dictionary<string, DateTime> before)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(cacheDir, "*.tmf"))
                if (!before.TryGetValue(f, out var prev) || File.GetLastWriteTimeUtc(f) != prev)
                    return true;
        }
        catch { /* unreadable cache — treat as nothing written */ }
        return false;
    }

    private static int CountTmfs()
    {
        try { return Directory.Exists(TmfCacheDir) ? Directory.GetFiles(TmfCacheDir, "*.tmf").Length : 0; }
        catch { return 0; }
    }

    /// <summary>Backstop for a wedged tracepdb (runs under the provision lock, so a hang stalls the decode).</summary>
    private const int TracePdbTimeoutMs = 120_000;

    private static void Run(string exe, string args, StringBuilder log)
    {
        if (RunTracePdbOverride != null)
        {
            log.AppendLine($"> tracepdb {args}");
            RunTracePdbOverride(exe, args, log);
            return;
        }
        log.AppendLine($"> tracepdb {args}");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var p = new Process { StartInfo = psi };
            var outSb = new StringBuilder();
            var errSb = new StringBuilder();
            // Read stdout AND stderr CONCURRENTLY (async, on the thread pool). Reading them sequentially with
            // ReadToEnd() deadlocks: while we block on stdout, tracepdb can fill the (~4 KB) stderr pipe buffer,
            // then blocks writing to it — so it never exits, never closes stdout, and we hang forever. The
            // event-based readers drain both pipes as data arrives, so neither can back up.
            p.OutputDataReceived += (_, e) => { if (e.Data != null) { lock (outSb) outSb.AppendLine(e.Data); } };
            p.ErrorDataReceived  += (_, e) => { if (e.Data != null) { lock (errSb) errSb.AppendLine(e.Data); } };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            // Bound the wait so a wedged tracepdb can't hang BuildTmfs (which holds _provisionLock).
            if (!p.WaitForExit(TracePdbTimeoutMs))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* already gone */ }
                log.AppendLine($"tracepdb timed out after {TracePdbTimeoutMs} ms — killed");
                return;
            }
            p.WaitForExit(); // parameterless: block until the async output handlers have flushed to EOF

            string outp, err;
            lock (outSb) outp = outSb.ToString();
            lock (errSb) err = errSb.ToString();
            if (!string.IsNullOrWhiteSpace(outp)) log.AppendLine(outp.Trim());
            if (!string.IsNullOrWhiteSpace(err)) log.AppendLine(err.Trim());
        }
        catch (Exception ex)
        {
            log.AppendLine($"tracepdb failed for [{args}]: {ex.Message}");
        }
    }
}

/// <summary>What happened to one binary/PDB during a Build or Diagnose pass — the structured form of
/// the resolver's per-line log, so the UI can show a readable status list instead of a raw dump.</summary>
public enum SymbolStatus
{
    Resolved,      // PDB found (GUID+age) and a TMF was extracted
    NoTmf,         // PDB matched but carries no WPP data (public/stripped, or a non-WPP binary)
    FoundLocal,    // (diagnose-only) PDB present locally but not yet extracted
    WrongVersion,  // a PDB by the right name was present but is a different build
    NotFound,      // the needed PDB wasn't found anywhere searched
}

/// <summary>One binary/PDB's result from <see cref="WppSymbolResolver.BuildTmfs"/> or
/// <see cref="WppSymbolResolver.Diagnose"/>.</summary>
public sealed class SymbolOutcome
{
    public SymbolStatus Status { get; init; }
    public string Binary { get; init; }        // producing binary's file name (null for a bare loose PDB)
    public string PdbName { get; init; }        // e.g. wppcat.pdb
    public string Guid { get; init; }           // needed PDB GUID (32 hex), when known
    public int Age { get; init; }               // needed age, when known
    public string Detail { get; init; }         // one human line (what was found / where we looked)
    public string ResolvedPath { get; init; }   // for Resolved / NoTmf / FoundLocal

    /// <summary>True for the states the user needs to act on (missing / wrong / no-data).</summary>
    public bool IsProblem => Status is SymbolStatus.WrongVersion or SymbolStatus.NotFound or SymbolStatus.NoTmf;

    /// <summary>Leading status glyph for the UI list.</summary>
    public string Glyph => Status switch
    {
        SymbolStatus.Resolved or SymbolStatus.FoundLocal => "✓", // ✓
        SymbolStatus.NoTmf => "⚠",                                // ⚠
        _ => "✗",                                                 // ✗
    };

    /// <summary>One-line label for the status list.</summary>
    public string Headline
    {
        get
        {
            var who = Binary ?? PdbName ?? "(unknown)";
            return Status switch
            {
                SymbolStatus.Resolved     => $"{who} — TMF extracted",
                SymbolStatus.FoundLocal   => $"{who} — PDB found ({PdbName}); Build TMFs to extract",
                SymbolStatus.NoTmf        => $"{who} — PDB matched but no WPP data (public/stripped; needs the private PDB)",
                SymbolStatus.WrongVersion => $"{who} — WRONG PDB: {Detail}",
                SymbolStatus.NotFound     => $"{who} — {PdbName} {Guid} age {Age} not found",
                _ => who,
            };
        }
    }
}

/// <summary>Result of <see cref="WppSymbolResolver.BuildTmfs"/>: the cache count, the full resolver
/// log, and the per-binary outcomes. Deconstructs to the old <c>(tmfCount, log)</c> tuple so existing
/// callers keep working.</summary>
public sealed class BuildTmfsResult
{
    public int TmfCount { get; init; }
    public string Log { get; init; } = "";
    public IReadOnlyList<SymbolOutcome> Outcomes { get; init; } = System.Array.Empty<SymbolOutcome>();

    /// <summary>Back-compat with the old (tmfCount, log) tuple: <c>var (count, log) = BuildTmfs(...)</c>.</summary>
    public void Deconstruct(out int tmfCount, out string log) { tmfCount = TmfCount; log = Log; }

    /// <summary>Compact one-line roll-up for the Settings status text.</summary>
    public string Summary
    {
        get
        {
            int resolved = 0, wrong = 0, missing = 0, notmf = 0;
            foreach (var o in Outcomes)
                switch (o.Status)
                {
                    case SymbolStatus.Resolved: case SymbolStatus.FoundLocal: resolved++; break;
                    case SymbolStatus.WrongVersion: wrong++; break;
                    case SymbolStatus.NotFound: missing++; break;
                    case SymbolStatus.NoTmf: notmf++; break;
                }
            var parts = new List<string> { $"{TmfCount} TMF(s) in cache" };
            if (resolved > 0) parts.Add($"{resolved} resolved");
            if (notmf > 0) parts.Add($"{notmf} no-WPP-data");
            if (wrong > 0) parts.Add($"{wrong} wrong version");
            if (missing > 0) parts.Add($"{missing} missing");
            return string.Join(" · ", parts);
        }
    }
}
