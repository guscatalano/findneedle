using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using FindNeedleCoreUtils;
using findneedle.WDK;
using Newtonsoft.Json;
using FindNeedlePluginLib;
using FindNeedlePluginLib.Interfaces;
using Windows.Media.PlayTo;
// Do not import FindPluginCore directly, use reflection for Logger

namespace findneedle.Implementations.FileExtensions;
public class ETLProcessor : IFileExtensionProcessor, IPluginDescription, IReportProgress
{
    public TraceFmtResult currentResult
    {
        get; private set; 
    }

    public Dictionary<string, int> providers = new();

    public bool LoadEarly = true;
    private readonly string tempPath = "";

    public string inputfile = "";
    private SearchProgressSink? _progressSink;

    // ----- Triage scope -----
    // The decode loop honors the ambient DecodeScope.Current (a compiled `scope` rule: provider/time/level)
    // BEFORE wrapping each event, so a scoped "load only these providers / this window" ingests a fraction
    // of a huge capture. The scope is set by the search pipeline (NuSearchQuery) from the loaded scope rule.

    private int _badlyFormattedCount = 0;

    // A modern (non-WPP) .etl decodes via the TraceEvent library. We *defer* that decode to the
    // consumer instead of doing it eagerly in DoPreProcessing: GetResultsWithCallback streams it
    // straight into batches (→ storage) without ever building the full in-memory list, so a 5M-row
    // trace costs ~one batch of RAM instead of all rows at once. GetResults() (the legacy/sync
    // contract) still materializes the list lazily on first call. _decodedToList guards that the
    // eager decode runs at most once.
    private bool _traceEventModern = false;
    private bool _decodedToList = false;

    // The WPP/tracefmt text-parse (and the .txt/.log passthrough) is deferred the same way the modern
    // decode is: tracefmt runs in DoPreProcessing (it can't be streamed), but the cheap line-parse of
    // its formatted output is moved to GetResultsWithCallback / GetResults so rows stream straight to
    // storage in batches instead of being materialized into one big list up front. PreLoad runs inline
    // during that parse (so it never needs a separate LoadInMemory pass). _fmtParsed guards once.
    private bool _deferredFmtParse = false;
    private bool _fmtParsed = false;

    // How this file was decoded + the resulting row count, surfaced via GetDecodeInfo() for the
    // Statistics "Decode by file" breakdown. Set as DoPreProcessing / the decoders run.
    private string _decodeMethod = "(pending)";
    private long _lastDecodeRowCount = 0;

    // When tracefmt is used, the formatted text it produces is moved here (out of the temp dir, which
    // is deleted on Dispose) so the UI can offer "view raw tracefmt output". Null otherwise.
    private string _rawOutputPath = null;
    private string _resolveLogPath = null;
    // Distinct message GUIDs tracefmt couldn't format (missing TMF) — the "requires symbol XYZ" list.
    private readonly HashSet<string> _missingTmfGuids = new(StringComparer.OrdinalIgnoreCase);
    // True when we bailed from the fast pre-scan (counts are sample-scoped, full file not decoded).
    private bool _prescanFailFast = false;
    // Guards the ON-DEMAND symbol provisioning (WppSymbolProvisioning) to a single attempt per run: when a
    // WPP decode fails for missing TMFs we ask the host to resolve them and retry the decode once — never
    // in a loop. Reset per DoPreProcessing run (this processor instance can be reused across searches).
    private bool _provisionAttempted = false;

    // "Decode anyway" de-dupes unformattable events by message GUID — this tallies how many events
    // each distinct GUID had, so the single emitted row can show the collapsed count.
    private readonly Dictionary<string, long> _forcedGuidCounts = new(StringComparer.OrdinalIgnoreCase);

    public ETLProcessor()
    {
        Logger.Instance.Log("ETLProcessor constructed");
        currentResult = new TraceFmtResult(); //empty
        tempPath = TempStorage.GetNewTempPath("etl");
    }

    public void Dispose()
    {
        Logger.Instance.Log($"Disposing ETLProcessor for file: {inputfile}");
        TempStorage.DeleteSomeTempPath(tempPath);
    }

    public void OpenFile(string fileName)
    {
        Logger.Instance.Log($"OpenFile called in ETLProcessor for file: {fileName}");
        inputfile = fileName;
    }

    /// <summary>
    /// Retain tracefmt's formatted output + a symbol-resolution log (search paths, outcome, missing
    /// TMF GUIDs, tracefmt narration) outside the temp dir so the UI can show them. Source-keyed.
    /// </summary>
    private void RetainTracefmtArtifacts()
    {
        if (!_decodeMethod.StartsWith("tracefmt")
            || currentResult?.outputfile == null
            || !File.Exists(currentResult.outputfile)
            || string.Equals(currentResult.outputfile, inputfile, StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            var dir = Path.Combine(FileIO.GetAppDataFindNeedlePluginFolder(), "tracefmt-output");
            Directory.CreateDirectory(dir);
            var stable = Path.Combine(dir, CachedStorage.GetCacheFileName(inputfile, ".tracefmt.txt"));
            File.Copy(currentResult.outputfile, stable, overwrite: true);
            _rawOutputPath = stable;

            var rlog = new StringBuilder();
            rlog.AppendLine($"WPP symbol resolution for: {inputfile}");
            rlog.AppendLine($"Decoded: {DateTime.Now}");
            if (_prescanFailFast)
                rlog.AppendLine("(Fast pre-scan of the first ~8 MB only — the full file was NOT decoded. Counts are sample-scoped.)");
            rlog.AppendLine();

            // ---- WHAT IT TRIED ---- each search-path entry, annotated with whether it actually exists.
            rlog.AppendLine("What it tried — TMF search paths (TRACE_FORMAT_SEARCH_PATH):");
            AppendPaths(rlog, Environment.GetEnvironmentVariable("TRACE_FORMAT_SEARCH_PATH"));
            rlog.AppendLine("What it tried — symbol paths (_NT_SYMBOL_PATH, for PDB→TMF):");
            AppendPaths(rlog, Environment.GetEnvironmentVariable("_NT_SYMBOL_PATH"));
            rlog.AppendLine();

            // ---- WHAT TRACEFMT ACTUALLY SEARCHED / LOADED ---- parsed from its own output, because
            // tracefmt ALSO tries the TMF/PDB paths embedded in the trace (not just our env vars). The
            // "WPPFMT : error : 0xN loading <file>" lines are the real "tried this exact file and failed".
            var searched = new List<string>();
            var failures = new List<string>();
            foreach (var raw in (currentResult.ConsoleOutput ?? "").Split('\n'))
            {
                var t = raw.Trim();
                if (t.Length == 0) continue;
                if (t.StartsWith("Examining ", StringComparison.OrdinalIgnoreCase)
                    || t.StartsWith("Searching for TMF", StringComparison.OrdinalIgnoreCase))
                    searched.Add(t);
                else if (t.IndexOf("WPPFMT", StringComparison.OrdinalIgnoreCase) >= 0
                         && (t.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0
                             || t.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0))
                    failures.Add(t);
            }
            rlog.AppendLine("What tracefmt actually searched / loaded (from its output):");
            if (searched.Count == 0) rlog.AppendLine("  (tracefmt reported no examine/search lines)");
            else foreach (var s in searched) rlog.AppendLine("  " + s);
            rlog.AppendLine();

            // ---- WHAT WORKED ----
            long total = currentResult.TotalEventsProcessed;
            long formatted = Math.Max(0, total - currentResult.TotalFormatsUnknown);
            var pct = total > 0 ? $"{formatted * 100.0 / total:0.#}%" : "n/a";
            rlog.AppendLine("What worked:");
            rlog.AppendLine($"  resolved {pct} — {formatted:N0} of {total:N0} events formatted " +
                            $"(formatErrors={currentResult.TotalFormatErrors:N0}, eventsLost={currentResult.TotalEventsLost:N0})");
            rlog.AppendLine();

            // ---- WHAT DIDN'T WORK ----
            if (_missingTmfGuids.Count == 0 && currentResult.TotalFormatsUnknown == 0)
            {
                rlog.AppendLine("What didn't work: nothing — every required TMF was found.");
            }
            else
            {
                rlog.AppendLine($"What didn't work — {_missingTmfGuids.Count} message GUID(s) had no TMF " +
                                $"({currentResult.TotalFormatsUnknown:N0} events unformatted). Supply each TMF (or the PDB it comes from):");
                foreach (var g in _missingTmfGuids)
                    rlog.AppendLine($"  {g}   →  expected file {g}.tmf");
                rlog.AppendLine("  Fix: set a PDB folder + symbol path under Settings → Results viewer → Logs, then \"Build TMFs from symbols\" and reopen. (The symbol path defaults to the Microsoft symbol server.)");
            }
            // The exact files tracefmt tried to load and couldn't — the most actionable "what didn't work"
            // (e.g. a private PDB at its original build path that isn't on disk / the symbol server).
            if (failures.Count > 0)
            {
                rlog.AppendLine();
                rlog.AppendLine("tracefmt load failures (the exact file it tried + the error):");
                foreach (var f in failures) rlog.AppendLine("  " + f);
            }
            if (!string.IsNullOrWhiteSpace(currentResult.SymbolDiagnostics))
            {
                rlog.AppendLine();
                rlog.AppendLine("----- detailed symbol diagnostics (dbghelp — like WinDbg !sym noisy) -----");
                rlog.AppendLine("Every PDB tracefmt's dbghelp tried, the search path, and the result per session:");
                rlog.AppendLine(currentResult.SymbolDiagnostics);
            }
            rlog.AppendLine();
            rlog.AppendLine("----- tracefmt output -----");
            rlog.AppendLine(currentResult.ConsoleOutput ?? "(none)");
            var resolveStable = Path.Combine(dir, CachedStorage.GetCacheFileName(inputfile, ".resolve.txt"));
            File.WriteAllText(resolveStable, rlog.ToString());
            _resolveLogPath = resolveStable;
            Logger.Instance.Log($"Retained tracefmt artifacts: {stable} + {resolveStable}");
        }
        catch (Exception ex) { Logger.Instance.Log($"Could not retain tracefmt output: {ex.Message}"); }
    }

    /// <summary>Write each ';'-separated search-path entry annotated with whether it actually exists
    /// (or is a symbol server), so the resolution log shows what was really searched.</summary>
    private static void AppendPaths(StringBuilder sb, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) { sb.AppendLine("  (none set)"); return; }
        foreach (var raw in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string note;
            if (raw.StartsWith("srv*", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("cache*", StringComparison.OrdinalIgnoreCase))
                note = raw.Contains("msdl.microsoft.com", StringComparison.OrdinalIgnoreCase) ? "[Microsoft symbol server]" : "[symbol server]";
            else if (Directory.Exists(raw) || File.Exists(raw)) note = "[found]";
            else note = "[MISSING]";
            sb.AppendLine($"  {raw}   {note}");
        }
    }

    /// <summary>Read just the first <paramref name="maxLines"/> of tracefmt's output to collect the
    /// distinct missing message GUIDs (for the fast-fail path — no full multi-million-line parse).</summary>
    private void SampleMissingGuids(string fmtFile, int maxLines)
    {
        try
        {
            if (string.IsNullOrEmpty(fmtFile) || !File.Exists(fmtFile)) return;
            using var sr = new StreamReader(fmtFile);
            string line; int n = 0;
            while ((line = sr.ReadLine()) != null && n++ < maxLines)
            {
                if (!line.StartsWith("Unknown")) continue;
                var gm = System.Text.RegularExpressions.Regex.Match(line,
                    @"GUID=([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
                if (gm.Success) _missingTmfGuids.Add(gm.Groups[1].Value);
            }
        }
        catch (Exception ex) { Logger.Instance.Log($"SampleMissingGuids failed: {ex.Message}"); }
    }

    public Dictionary<string, int> GetProviderCount()
    {
        return providers;
    }

    /// <summary>Per-file decode diagnostics for the Statistics "Decode by file" breakdown.</summary>
    public Dictionary<string, string> GetDecodeInfo()
    {
        var info = new Dictionary<string, string>
        {
            ["method"] = _decodeMethod,
            ["rows"] = _lastDecodeRowCount.ToString("N0"),
            ["providers"] = providers.Count.ToString(),
        };
        if (_badlyFormattedCount > 0)
            info["badlyFormatted"] = _badlyFormattedCount.ToString("N0");
        if (currentResult != null && _decodeMethod.StartsWith("tracefmt"))
        {
            info["eventsProcessed"] = currentResult.TotalEventsProcessed.ToString("N0");
            if (currentResult.TotalEventsProcessed > 0)
                info["decodable"] = $"{(currentResult.TotalEventsProcessed - currentResult.TotalFormatsUnknown) * 100.0 / currentResult.TotalEventsProcessed:0.#}%";
            info["eventsLost"] = currentResult.TotalEventsLost.ToString("N0");
            info["buffersProcessed"] = currentResult.TotalBuffersProcessed.ToString("N0");
            info["formatErrors"] = currentResult.TotalFormatErrors.ToString("N0");
            info["unknowns"] = currentResult.TotalFormatsUnknown.ToString("N0");
            if (!string.IsNullOrEmpty(currentResult.TotalElapsedTime)) info["elapsed"] = currentResult.TotalElapsedTime;
        }
        // These can exist even if tracefmt fell back to TraceEvent, so report them regardless of method.
        if (_missingTmfGuids.Count > 0) info["missingTmfs"] = string.Join(", ", _missingTmfGuids);
        if (!string.IsNullOrEmpty(_rawOutputPath) && File.Exists(_rawOutputPath)) info["rawOutput"] = _rawOutputPath;
        if (!string.IsNullOrEmpty(_resolveLogPath) && File.Exists(_resolveLogPath)) info["resolveLog"] = _resolveLogPath;
        return info;
    }

    public string GetFileName()
    {
        return inputfile; 
    }

   
    public void DoPreProcessing()
    {
        DoPreProcessing(CancellationToken.None);
    }
    public void DoPreProcessing(CancellationToken cancellationToken)
    {
        Logger.Instance.Log($"DoPreProcessing started for file: {inputfile}");
        _progressSink?.NotifyProgress(0, $"Preprocessing {inputfile}");

        // Reset per-run decode state. This processor instance can be reused across searches (e.g. the
        // fail-fast open followed by "Decode anyway"); without this, stale values like _decodeMethod
        // leak from the previous run (so the forced-decode label/banner would be wrong).
        _decodeMethod = "(pending)";
        _lastDecodeRowCount = 0;
        _forcedGuidCounts.Clear();
        _prescanFailFast = false;
        _provisionAttempted = false;
        _missingTmfGuids.Clear();
        _deferredFmtParse = false;
        _fmtParsed = false;

        if (inputfile.EndsWith(".txt") || inputfile.EndsWith(".log"))
        {
            Logger.Instance.Log($"Input file is .txt or .log, skipping TraceFmt: {inputfile}");
            _decodeMethod = "text (passthrough)";
            currentResult.ProcessedFile = inputfile;
            currentResult.outputfile = inputfile;
            currentResult.summaryfile = inputfile;
        }
        else
        {
            var outcome = DecodeEtlOnce(cancellationToken);

            // On-demand symbol provisioning: if the decode failed ONLY because WPP symbols (TMFs) were
            // missing, ask the host to resolve them — the ISymbolResolver plugins (SMB share, symbol
            // server, …) sweeping the ETL's folder + configured symbol sources, extracting TMFs into the
            // cache and refreshing TRACE_FORMAT_SEARCH_PATH — then retry the decode ONCE. When no host is
            // registered (CLI/tests) this is a no-op and we report "symbols missing" exactly as before.
            if (outcome == EtlDecodeOutcome.MissingSymbols
                && !_provisionAttempted
                && FindNeedlePluginLib.WppSymbolProvisioning.HasHandler)
            {
                _provisionAttempted = true;
                _progressSink?.NotifyProgress("Missing WPP symbols — asking symbol resolvers to fetch them…");
                Logger.Instance.Log($"Missing WPP symbols for {inputfile} ({_missingTmfGuids.Count} GUID(s)); invoking symbol provisioner");
                bool provisioned = FindNeedlePluginLib.WppSymbolProvisioning.TryProvision(
                    new FindNeedlePluginLib.WppProvisionRequest
                    {
                        EtlPath = inputfile,
                        MissingMessageGuids = _missingTmfGuids.ToArray(),
                    });
                if (provisioned)
                {
                    Logger.Instance.Log($"Symbol provisioner produced new TMFs; retrying decode for {inputfile}");
                    // Clear the first attempt's missing-symbols state so a successful retry doesn't inherit
                    // the "symbols missing" method/flags (the shared fall-through only overwrites "(pending)").
                    _missingTmfGuids.Clear();
                    _prescanFailFast = false;
                    _decodeMethod = "(pending)";
                    outcome = DecodeEtlOnce(cancellationToken);
                }
                else
                {
                    Logger.Instance.Log($"Symbol provisioner made no new symbols available for {inputfile}");
                }
            }

            switch (outcome)
            {
                case EtlDecodeOutcome.Handled:
                    return; // modern-deferred / tracefmt-unavailable — DecodeEtlOnce already reported.
                case EtlDecodeOutcome.MissingSymbols:
                    ReportMissingSymbolsAndBail(); // still missing after any provisioning → report + bail.
                    return;
                case EtlDecodeOutcome.Materialized:
                    // Managed decoder already built the rows (with level/activity/cpu). Nothing to parse.
                    Logger.Instance.Log($"Managed WPP decode complete for {inputfile}: method={_decodeMethod}, rows={_lastDecodeRowCount}");
                    _progressSink?.NotifyProgress(100, $"Preprocessing complete for {inputfile}");
                    return;
                case EtlDecodeOutcome.FormattedNeedsParse:
                    break; // tracefmt formatted events → fall through to the shared deferred-parse setup.
            }
        }
        // tracefmt has run (it can't be streamed). Defer the cheap line-parse of its formatted output
        // (and the .txt/.log passthrough) to GetResultsWithCallback / GetResults so rows flow to storage
        // in batches and the viewer can open partway, instead of materializing the whole list here. Only
        // the non-WPP fallback below is decoded eagerly.

        // Fallback for non-WPP traces. tracefmt only formats WPP (driver/software) traces; for a modern
        // .etl (EventSource / manifest / kernel) it processes ~no events. Decode those directly with the
        // TraceEvent library. Only when tracefmt processed ~no events — if it DID process events (a WPP
        // trace) but formatted nothing, that's a missing-symbols problem TraceEvent can't fix. (The
        // modern-trace probe + fail-fast usually catch these first; this covers the rest, incl. "Decode
        // anyway".)
        bool tracefmtProcessedEvents = currentResult != null && currentResult.TotalEventsProcessed > 0;
        bool isTextPassthrough = inputfile.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                              || inputfile.EndsWith(".log", StringComparison.OrdinalIgnoreCase);
        if (!tracefmtProcessedEvents && !isTextPassthrough && File.Exists(inputfile))
        {
            Logger.Instance.Log($"tracefmt produced no events for {inputfile}; falling back to TraceEvent decode");
            _progressSink?.NotifyProgress(50, "Decoding ETL with TraceEvent (non-WPP trace)");
            _decodeMethod = "TraceEvent (fallback after tracefmt)";
            DecodeWithTraceEvent(cancellationToken); // eager (edge case); populates results
        }
        else
        {
            // WPP (tracefmt formatted events) or the text passthrough → defer the parse. Set a
            // provisional method now so stats read before the stream don't show "(pending)"; the forced
            // ("Decode anyway") label is finalized in ParseFormattedOutput.
            _deferredFmtParse = true;
            if (_decodeMethod == "(pending)") _decodeMethod = "tracefmt (WPP)";
        }

        // Retain the symbol-resolution log AFTER _decodeMethod is finalized. Its guard keys off the method
        // (tracefmt-* only), and on the successful WPP path the method was still "(pending)" until just
        // above — so calling it earlier silently skipped the log and the Stats-page button never appeared.
        RetainTracefmtArtifacts();

        Logger.Instance.Log(
            $"Decode summary {inputfile}: method={_decodeMethod} deferredParse={_deferredFmtParse} " +
            $"providers={providers.Count}" +
            (currentResult != null && _decodeMethod.StartsWith("tracefmt")
                ? $" eventsProcessed={currentResult.TotalEventsProcessed} formatErrors={currentResult.TotalFormatErrors} unknowns={currentResult.TotalFormatsUnknown}"
                : ""));
        Logger.Instance.Log($"DoPreProcessing complete for {inputfile}");
        _progressSink?.NotifyProgress(100, $"Preprocessing complete for {inputfile}");
    }

    /// <summary>Outcome of a single <see cref="DecodeEtlOnce"/> attempt.</summary>
    private enum EtlDecodeOutcome
    {
        /// <summary>Nothing more for DoPreProcessing to do here — it should return. Used for a modern
        /// (TraceEvent-deferred) trace and for "tracefmt unavailable"; the banner/return state is set inline.</summary>
        Handled,
        /// <summary>≈all events unformattable — the WPP symbols (TMFs) are missing. currentResult,
        /// _decodeMethod, _prescanFailFast and _missingTmfGuids are set; the caller decides whether to
        /// provision symbols + retry, and emits the terminal banner (ReportMissingSymbolsAndBail) if not.</summary>
        MissingSymbols,
        /// <summary>tracefmt formatted events — fall through to the shared deferred-parse setup.</summary>
        FormattedNeedsParse,
        /// <summary>The managed WPP decoder already built the rows into <c>results</c> (with level/activity/cpu
        /// the text path can't carry) — DoPreProcessing just returns; GetResults returns the materialized list.</summary>
        Materialized,
    }

    /// <summary>
    /// One attempt to decode the .etl: modern-trace probe → tracefmt pre-scan → full tracefmt decode, with
    /// the "≈everything unformattable = missing WPP symbols" fail-fast. Deliberately does NOT emit the
    /// terminal "symbols missing" banner or retain the resolution log for that case — the caller does, AFTER
    /// deciding whether to provision symbols and retry, so a successful retry leaves no stale "missing" state.
    /// Safe to call twice (the retry after provisioning).
    /// </summary>
    private EtlDecodeOutcome DecodeEtlOnce(CancellationToken cancellationToken)
    {
        // Skip tracefmt for modern (non-WPP) traces — it can't decode them and would emit one "Unknown"
        // line per event. Defer the TraceEvent decode to the consumer so the streaming search path pumps
        // events straight to storage without ever holding the whole trace in RAM.
        if (LooksLikeModernTrace(inputfile, cancellationToken))
        {
            Logger.Instance.Log($"{inputfile} is a modern (non-WPP) trace; will decode with TraceEvent on demand (deferred)");
            _traceEventModern = true;
            _decodeMethod = "TraceEvent (modern)";
            _progressSink?.NotifyProgress(100, $"Preprocessing complete for {inputfile} (decode deferred)");
            return EtlDecodeOutcome.Handled;
        }

        // WPP decoder selection. Managed mode (or Auto when the WDK/tracefmt isn't available) decodes the WPP
        // trace in-process with no external tools. It returns the same outcomes as the tracefmt path, so the
        // provision-and-retry-once logic in DoPreProcessing (ISymbolResolver plugins → BuildTmfs → refreshed
        // TRACE_FORMAT_SEARCH_PATH) wraps it identically — the managed decode is retried after symbols land.
        if (DecodeOptions.WppDecoder == FindNeedlePluginLib.WppDecoder.Compare)
        {
            return DecodeEtlCompare(cancellationToken);
        }
        if (DecodeOptions.WppDecoder == FindNeedlePluginLib.WppDecoder.Managed
            || (DecodeOptions.WppDecoder == FindNeedlePluginLib.WppDecoder.Auto && !TraceFmt.IsAvailable()))
        {
            return DecodeEtlManaged(cancellationToken);
        }

        // Fast pre-scan: decode only the first few MB to estimate decodability before the full (slow,
        // processing-bound) run. If the sample is ~all unformattable, it's a missing-symbols problem —
        // report the missing GUIDs instead of grinding the whole file. Skipped for "Decode anyway".
        var pre = DecodeOptions.ForceFullDecode ? null : TraceFmt.PreScan(inputfile, tempPath, _progressSink);
        if (!DecodeOptions.ForceFullDecode)
            _progressSink?.NotifyProgress(5, "Pre-scanning ETL for decodability…");
        if (pre != null && pre.TotalEventsProcessed > 0
            && pre.TotalFormatsUnknown >= pre.TotalEventsProcessed * 0.99)
        {
            currentResult = pre;
            _prescanFailFast = true;
            _decodeMethod = "tracefmt (WPP) — symbols missing";
            _lastDecodeRowCount = 0;
            SampleMissingGuids(pre.outputfile, 200_000);
            return EtlDecodeOutcome.MissingSymbols;
        }

        // Surface the decode phase on the loading screen. tracefmt runs synchronously (a black box until
        // it finishes), so the count can't tick during it — but the phase label + animation show.
        FindNeedlePluginLib.FlowProgress.Begin(FindNeedlePluginLib.FlowPhase.DecodeEtl);
        Logger.Instance.Log($"Calling TraceFmt.ParseSimpleETL for file: {inputfile}");
        currentResult = TraceFmt.ParseSimpleETL(inputfile, tempPath, _progressSink);
        if (currentResult == null)
        {
            Logger.Instance.Log($"TraceFmt result is null for {inputfile}, skipping ETL processing.");
            _progressSink?.NotifyProgress(100, $"TraceFmt not found or failed for {inputfile}, skipping ETL processing.");
            return EtlDecodeOutcome.Handled;
        }

        // Fail fast: tracefmt's summary (already parsed) tells us how much is decodable BEFORE we grind
        // line-by-line through the output. If (nearly) everything is unformattable, it's a missing-symbols
        // problem — sample the missing GUIDs and skip the multi-million-line parse + futile TraceEvent fallback.
        long total = currentResult.TotalEventsProcessed;
        long unknown = currentResult.TotalFormatsUnknown;
        if (!DecodeOptions.ForceFullDecode && total > 0 && unknown >= total * 0.99)
        {
            _decodeMethod = "tracefmt (WPP) — symbols missing";
            _lastDecodeRowCount = 0;
            SampleMissingGuids(currentResult.outputfile, 200_000);
            return EtlDecodeOutcome.MissingSymbols;
        }

        return EtlDecodeOutcome.FormattedNeedsParse;
    }

    /// <summary>
    /// Decode the WPP .etl in managed code (no WDK / no tracefmt.exe) and write the result in tracefmt's text
    /// format, so the existing <see cref="ParseFormattedOutput"/> consumes it identically (same ETLLogLine
    /// objects, same streaming/scope). TMFs come from TRACE_FORMAT_SEARCH_PATH — the same env var tracefmt
    /// reads, set by the host from the user's symbol settings + the managed TMF cache. Returns the SAME
    /// outcomes as the tracefmt path, so the missing-symbols fail-fast + the ISymbolResolver provisioning
    /// retry in DoPreProcessing apply unchanged (the managed decode is simply retried once symbols land).
    /// </summary>
    private EtlDecodeOutcome DecodeEtlManaged(CancellationToken cancellationToken)
    {
        FindNeedlePluginLib.FlowProgress.Begin(FindNeedlePluginLib.FlowPhase.DecodeEtl);
        _progressSink?.NotifyProgress(5, "Decoding ETL (managed WPP decoder)…");
        results.Clear(); // retry-safe: this can re-run after symbol provisioning
        var tmf = LoadTmfDatabaseFromSearchPath();
        var decoder = new findneedle.Wpp.ManagedWppEtlDecoder(tmf);
        long formatted = 0;
        try
        {
            decoder.Decode(inputfile, e =>
            {
                if (cancellationToken.IsCancellationRequested) return;
                // Triage scope filter (mirror the tracefmt parse path): drop out-of-scope rows before building.
                if (DecodeScope.Current is { } scope)
                {
                    var t = e.TimeStamp;
                    DateTime? tsUtc = t == DateTime.MinValue ? (DateTime?)null : t.ToUniversalTime();
                    if (!scope.Keep(e.Component, tsUtc, e.EventLevel)) return;
                }
                var row = BuildWppRow(e);
                row.PreLoad();
                if (row.tasktxt == "Badly formatted event") _badlyFormattedCount++;
                providers[e.Component] = providers.TryGetValue(e.Component, out var c) ? c + 1 : 1;
                results.Add(row);
                formatted++;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"Managed WPP decode failed for {inputfile}: {ex.Message}");
            _progressSink?.NotifyProgress(100, $"Managed WPP decode failed: {ex.Message}");
            return EtlDecodeOutcome.Handled;
        }

        long unresolved = decoder.Unresolved;
        long total = formatted + unresolved;
        currentResult = new TraceFmtResult
        {
            TotalEventsProcessed = (int)Math.Min(total, int.MaxValue),
            TotalFormatsUnknown = (int)Math.Min(unresolved, int.MaxValue),
            ConsoleOutput = $"(managed WPP decoder: {formatted:N0} rows, {unresolved:N0} without TMF; {tmf.Count} TMF entries)",
        };
        foreach (var g in decoder.UnresolvedGuids) _missingTmfGuids.Add(g.ToString());
        Logger.Instance.Log($"Managed WPP decode {inputfile}: {formatted} rows, {unresolved} unresolved, {tmf.Count} TMF entries");

        // Same "≈all unformattable = missing symbols" fail-fast → triggers provisioning + retry.
        if (!DecodeOptions.ForceFullDecode && total > 0 && unresolved >= total * 0.99)
        {
            results.Clear();
            _decodeMethod = "managed WPP — symbols missing";
            _lastDecodeRowCount = 0;
            return EtlDecodeOutcome.MissingSymbols;
        }
        _decodeMethod = "managed WPP";
        _lastDecodeRowCount = formatted;
        return EtlDecodeOutcome.Materialized;
    }

    // Build one row from a managed-decoded WPP event: reuse the tracefmt-line parse for cpu/pid/tid/time/
    // provider/message, then set the fields the text format can't carry — level, activity IDs, provider GUID —
    // which the managed decoder DOES have (so the managed path is actually richer than tracefmt's text output).
    private ETLLogLine BuildWppRow(findneedle.Wpp.WppDecodedEvent e)
    {
        var line = new ETLLogLine(ManagedWppLine(e), inputfile);
        line.eventLevel = e.EventLevel;
        line.activityId = e.ActivityId == Guid.Empty ? "" : e.ActivityId.ToString();
        line.relatedActivityId = e.RelatedActivityId == Guid.Empty ? "" : e.RelatedActivityId.ToString();
        var pg = e.ProviderGuid != Guid.Empty ? e.ProviderGuid : e.MessageGuid;
        line.providerGuid = pg == Guid.Empty ? "" : pg.ToString();
        return line;
    }

    /// <summary>Run the managed WPP decoder, writing tracefmt-format text to <paramref name="outFile"/>.
    /// Returns the formatted/unresolved counts, the TMF-entry count, and the distinct unresolved message GUIDs.
    /// Does NOT touch currentResult — callers decide how to use it (single-decoder vs compare).</summary>
    private (long formatted, long unresolved, int tmfCount, HashSet<string> guids) RunManagedWpp(string outFile, CancellationToken ct)
    {
        var tmf = LoadTmfDatabaseFromSearchPath();
        var decoder = new findneedle.Wpp.ManagedWppEtlDecoder(tmf);
        long formatted = 0;
        using (var w = new StreamWriter(outFile, append: false, Encoding.UTF8))
        {
            decoder.Decode(inputfile, ev =>
            {
                if (ct.IsCancellationRequested) return;
                w.WriteLine(ManagedWppLine(ev));
                formatted++;
            }, ct);
        }
        var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in decoder.UnresolvedGuids) guids.Add(g.ToString());
        return (formatted, decoder.Unresolved, tmf.Count, guids);
    }

    // Set currentResult from a managed decode + apply the shared missing-symbols fail-fast (→ provisioning retry).
    private EtlDecodeOutcome FinishManagedResult(string outFile, long formatted, long unresolved, int tmfCount,
        HashSet<string> guids, string methodLabel)
    {
        long total = formatted + unresolved;
        currentResult = new TraceFmtResult
        {
            outputfile = outFile,
            TotalEventsProcessed = (int)Math.Min(total, int.MaxValue),
            TotalFormatsUnknown = (int)Math.Min(unresolved, int.MaxValue),
            ConsoleOutput = $"(managed WPP decoder: {formatted:N0} formatted, {unresolved:N0} without TMF; {tmfCount} TMF entries loaded)",
        };
        foreach (var g in guids) _missingTmfGuids.Add(g);
        Logger.Instance.Log($"Managed WPP decode {inputfile}: {formatted} formatted, {unresolved} unresolved, {tmfCount} TMF entries");

        if (!DecodeOptions.ForceFullDecode && total > 0 && unresolved >= total * 0.99)
        {
            _decodeMethod = methodLabel + " — symbols missing";
            _lastDecodeRowCount = 0;
            return EtlDecodeOutcome.MissingSymbols;
        }
        _decodeMethod = methodLabel;
        return EtlDecodeOutcome.FormattedNeedsParse;
    }

    /// <summary>
    /// Compare mode: decode with BOTH tracefmt and the managed decoder, then keep whichever formatted more
    /// events (tie, or tracefmt-only, → tracefmt as the reference). ~2× decode cost. Also logs any divergence
    /// between the two — a live check of the managed decoder against tracefmt on real traces. Returns the same
    /// outcomes as the single-decoder paths, so the missing-symbols → provisioning → retry logic still applies.
    /// </summary>
    private EtlDecodeOutcome DecodeEtlCompare(CancellationToken cancellationToken)
    {
        FindNeedlePluginLib.FlowProgress.Begin(FindNeedlePluginLib.FlowPhase.DecodeEtl);
        _progressSink?.NotifyProgress(5, "Decoding ETL (comparing tracefmt vs managed)…");

        // tracefmt (only if the WDK is available).
        TraceFmtResult tf = null;
        long tfFormatted = -1, tfUnknown = 0;
        if (TraceFmt.IsAvailable())
        {
            try { tf = TraceFmt.ParseSimpleETL(inputfile, tempPath, _progressSink); }
            catch (Exception ex) { Logger.Instance.Log($"WPP compare: tracefmt failed for {inputfile}: {ex.Message}"); tf = null; }
            if (tf != null) { tfUnknown = tf.TotalFormatsUnknown; tfFormatted = Math.Max(0, tf.TotalEventsProcessed - tf.TotalFormatsUnknown); }
        }

        // managed.
        var managedOut = Path.Combine(tempPath, "managed-wpp.fmt.txt");
        long mFormatted = -1, mUnresolved = 0; int mTmf = 0; HashSet<string> mGuids = new(StringComparer.OrdinalIgnoreCase);
        try { (mFormatted, mUnresolved, mTmf, mGuids) = RunManagedWpp(managedOut, cancellationToken); }
        catch (Exception ex) { Logger.Instance.Log($"WPP compare: managed failed for {inputfile}: {ex.Message}"); mFormatted = -1; }

        // Winner: more formatted events wins; tie / tracefmt-only → tracefmt (the reference).
        bool useManaged = tf == null ? mFormatted >= 0 : mFormatted > tfFormatted;
        bool differ = tf != null && mFormatted >= 0 && mFormatted != tfFormatted;
        Logger.Instance.Log($"WPP compare {inputfile}: tracefmt formatted={tfFormatted} unknown={tfUnknown}; " +
            $"managed formatted={mFormatted} unresolved={mUnresolved} (tmf={mTmf}) -> using {(useManaged ? "managed" : "tracefmt")}{(differ ? "  (DIFFER)" : "")}");
        if (differ)
            _progressSink?.NotifyProgress($"WPP compare: tracefmt {tfFormatted:N0} vs managed {mFormatted:N0} rows — using {(useManaged ? "managed" : "tracefmt")}");

        if (useManaged)
            return FinishManagedResult(managedOut, mFormatted, mUnresolved, mTmf, mGuids, "managed WPP (compare)");

        // tracefmt won (or managed failed / both failed).
        if (tf == null) { _progressSink?.NotifyProgress(100, "Both WPP decoders failed."); return EtlDecodeOutcome.Handled; }
        currentResult = tf;
        long total = tf.TotalEventsProcessed;
        if (!DecodeOptions.ForceFullDecode && total > 0 && tfUnknown >= total * 0.99)
        {
            _decodeMethod = "tracefmt (WPP) (compare) — symbols missing";
            _lastDecodeRowCount = 0;
            SampleMissingGuids(tf.outputfile, 200_000);
            return EtlDecodeOutcome.MissingSymbols;
        }
        _decodeMethod = "tracefmt (WPP) (compare)";
        return EtlDecodeOutcome.FormattedNeedsParse;
    }

    // One decoded WPP event → tracefmt's text line format, so ETLLogLine reparses it exactly like tracefmt output.
    private static string ManagedWppLine(findneedle.Wpp.WppDecodedEvent e)
    {
        var src = string.IsNullOrEmpty(e.Component) ? "WPP" : e.Component;
        return $"[{e.Cpu}]{e.ProcessId:X}.{e.ThreadId:X}::{e.TimeStamp:MM/dd/yyyy-HH:mm:ss.fff} [{src}]{e.Message}";
    }

    // Load every TMF on TRACE_FORMAT_SEARCH_PATH (the same paths tracefmt searches) into a managed database.
    private static findneedle.Wpp.TmfDatabase LoadTmfDatabaseFromSearchPath()
    {
        var db = new findneedle.Wpp.TmfDatabase();
        var paths = Environment.GetEnvironmentVariable("TRACE_FORMAT_SEARCH_PATH");
        if (!string.IsNullOrEmpty(paths))
        {
            foreach (var p in paths.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    if (Directory.Exists(p))
                        foreach (var f in Directory.EnumerateFiles(p, "*.tmf", SearchOption.AllDirectories))
                            db.AddFile(f);
                }
                catch { /* skip an unreadable dir */ }
            }
        }
        return db;
    }

    /// <summary>Terminal report for the "≈all events unformattable = missing WPP symbols" case: retain the
    /// resolution log (which TMFs/symbols are still needed) and surface the banner. Split out of the
    /// fail-fast so on-demand provisioning can run BETWEEN the failed decode and this report — and be
    /// skipped entirely on a successful retry.</summary>
    private void ReportMissingSymbolsAndBail()
    {
        RetainTracefmtArtifacts();
        long total = currentResult?.TotalEventsProcessed ?? 0;
        long unknown = currentResult?.TotalFormatsUnknown ?? 0;
        var which = _missingTmfGuids.Count > 0 ? string.Join(", ", _missingTmfGuids.Take(5)) : "?";
        var countPart = total > 0 ? $"{unknown:N0} of {total:N0} events" : "events";
        Logger.Instance.Log($"Missing WPP symbols (final): {inputfile} — {countPart} unformattable; {_missingTmfGuids.Count} GUID(s) unresolved. Missing: {which}");
        _progressSink?.NotifyProgress(100,
            $"Can't decode: {countPart} need WPP symbols (missing TMF for {which}). Set a symbol/TMF path in settings and reopen.");
    }

    /// <summary>
    /// Parse tracefmt's formatted output (or the .txt/.log passthrough) line-by-line, PreLoading each
    /// row and handing it to <paramref name="emit"/>. This is the body that used to run inline in
    /// DoPreProcessing; deferring it lets the streaming search pump rows to storage in batches (and the
    /// viewer open partway) rather than building the whole list first. Runs at most once (_fmtParsed).
    /// Side effects mirror the old inline path: provider counts, missing-TMF GUIDs, "Decode anyway"
    /// forced rows, the badly-formatted tally, and the decode method + row count.
    /// </summary>
    private void ParseFormattedOutput(Action<ISearchResult> emit, CancellationToken cancellationToken)
    {
        if (_fmtParsed) return;
        _fmtParsed = true;
        var getLock = 50;
        _progressSink?.NotifyProgress(20, "Parsing output file");
        while (getLock > 0)
        {
            if (cancellationToken.IsCancellationRequested) return;
            try
            {
                if (currentResult.outputfile == null)
                {
                    Logger.Instance.Log($"Output file is not set for {inputfile}");
                    throw new InvalidOperationException("Output file is not set.");
                }
                using var fileStream = File.OpenRead(currentResult.outputfile);
                using var streamReader = new StreamReader(fileStream, Encoding.UTF8, false); //change buffer if there's perf reasons

                string? line;
                int lineCount = 0;
                int corruptCount = 0; // count + summarize; logging every corrupted line on a modern
                                      // trace meant millions of writes (a 286 MB log on a 5M .etl).
                while ((line = streamReader.ReadLine()) != null)
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    var failsafe = 10;
                    while (!ETLLogLine.DoesHeaderLookRight(line) && failsafe > 0)
                    {
                        if (line.StartsWith("Unknown"))
                        {
                            failsafe = 0; //This is corrupted, let's just bail;
                            if (corruptCount < 5) Logger.Instance.Log($"Corrupted line detected in {inputfile}: {line}");
                            corruptCount++;
                            // "Unknown( N): GUID=<msg-guid> (No Format Information found)." — the GUID is
                            // the message GUID = the TMF filename. Collect the distinct missing ones so
                            // the resolution log can say exactly which TMFs/symbols are needed.
                            var gm = System.Text.RegularExpressions.Regex.Match(line,
                                @"GUID=([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
                            var guid = gm.Success ? gm.Groups[1].Value : "unknown";
                            if (gm.Success) _missingTmfGuids.Add(guid);
                            // "Decode anyway": de-dupe by GUID — just tally per-GUID event counts here;
                            // one representative row per distinct GUID (with its collapsed count) is
                            // emitted after the read loop.
                            if (DecodeOptions.ForceFullDecode)
                                _forcedGuidCounts[guid] = _forcedGuidCounts.TryGetValue(guid, out var c) ? c + 1 : 1;
                            // Surface WHY this is slow: skipping millions of unformattable events
                            // (these don't advance the "Processed N lines" counter, so without this the
                            // status looks stuck). Names the missing GUID(s) so it's clearly a symbol issue.
                            if (corruptCount % 50000 == 0)
                            {
                                var which = _missingTmfGuids.Count > 0 ? string.Join(", ", _missingTmfGuids.Take(3)) : "?";
                                _progressSink?.NotifyProgress(
                                    $"Missing WPP symbols — {corruptCount:N0} events can't be formatted (no TMF for {which}). Set a symbol/TMF path in settings.");
                            }
                            continue;
                        }
                        //line is not complete!
                        failsafe--;
                        line += streamReader.ReadLine();
                    }
                    if (failsafe == 0)
                    {
                        continue; // corrupted/incomplete (counted above); don't throw or we skip too much
                    }
                    var etlline = new ETLLogLine(line, inputfile);
                    etlline.PreLoad(); // was a separate LoadInMemory pass — do it inline so streamed rows are ready
                    if (etlline.tasktxt == "Badly formatted event") _badlyFormattedCount++;
                    // ----- Triage scope filter (WPP / tracefmt path) -----
                    // Mirror DecodeWithTraceEvent (:634): drop out-of-scope lines BEFORE the wrap + storage
                    // ingest + FTS, so a "load only these providers / this time window" scope skips most of a
                    // huge WPP capture instead of ingesting all of it. Provider = parsed Source; timestamp is
                    // null for an un-timestamped line (kept); level not applied at decode (matches modern -1).
                    if (DecodeScope.Current is { } scope)
                    {
                        var t = etlline.GetLogTime();
                        DateTime? tsUtc = t == DateTime.MinValue ? (DateTime?)null : t.ToUniversalTime();
                        if (!scope.Keep(etlline.GetSource(), tsUtc, -1)) continue;
                    }
                    if (providers.ContainsKey(etlline.GetSource()))
                    {
                        providers[etlline.GetSource()]++;
                    }
                    else
                    {
                        providers[etlline.GetSource()] = 1;
                    }
                    emit(etlline);
                    lineCount++;
                    if (lineCount % 1000 == 0)
                    {
                        // Estimate against ~100k lines but clamp to [20,90] — a file with >100k lines must
                        // not push the bar past 100% (this was the "300%" bug on big WPP/ETL files).
                        var pct = Math.Clamp(20 + (int)(70.0 * lineCount / 100000), 20, 90);
                        _progressSink?.NotifyProgress(pct, $"Processed {lineCount} lines");
                        // Tick the line count on the loading screen (the spinner reads FlowProgress, not the
                        // progress sink) so a big WPP parse shows visible movement instead of sitting at 0.
                        FindNeedlePluginLib.FlowProgress.Detail($"{lineCount:N0} lines parsed", pct, estimate: true);
                    }
                }
                if (corruptCount > 0)
                    Logger.Instance.Log($"Skipped {corruptCount} corrupted/unformattable lines in {inputfile} (tracefmt couldn't decode them)");
                // "Decode anyway": emit one representative row per distinct unformattable GUID, annotated
                // with how many events collapsed into it.
                if (DecodeOptions.ForceFullDecode && _forcedGuidCounts.Count > 0)
                {
                    foreach (var kv in _forcedGuidCounts.OrderByDescending(kv => kv.Value))
                    {
                        emit(ETLLogLine.Unformatted(kv.Key, kv.Value, inputfile));
                        lineCount++;
                    }
                    providers["(unformatted WPP)"] = checked((int)Math.Min(int.MaxValue, _forcedGuidCounts.Values.Sum()));
                }
                Logger.Instance.Log($"Finished reading output file for {inputfile}, total lines: {lineCount}");
                _progressSink?.NotifyProgress(90, $"Finished reading output file, total lines: {lineCount}");
                // The WPP/text method was set provisionally in DoPreProcessing; only upgrade the label
                // for the forced ("Decode anyway") case, which we can't know until we've counted corrupts.
                if (DecodeOptions.ForceFullDecode && corruptCount > 0)
                    _decodeMethod = $"tracefmt (WPP) — forced; {corruptCount:N0} events unformatted across {_forcedGuidCounts.Count} GUID(s)";
                _lastDecodeRowCount = lineCount;
                break;
            }
            catch (Exception ex)
            {
                Logger.Instance.Log($"Exception while reading output file for {inputfile}: {ex.Message}");
                Thread.Sleep(100);
                getLock--; // Sometimes tracefmt can hold the lock, wait until file is ready
            }
        }
    }

    /// <summary>
    /// Decode an .etl directly via the TraceEvent library (manifest / EventSource / kernel events).
    /// Used as a fallback when tracefmt yields nothing because the trace isn't WPP. Builds the same
    /// <see cref="ETLLogLine"/> objects as the real-time collector, so downstream is identical.
    /// Never throws — a decode failure just leaves results as-is.
    /// </summary>
    /// <summary>
    /// Bounded probe to decide whether an .etl is a "modern" trace (manifest / EventSource /
    /// TraceLogging / NT-kernel — all decodable by TraceEvent) versus a WPP/classic trace that needs
    /// tracefmt. tracefmt can't format modern traces: it emits one "Unknown(...) decoding error 1168"
    /// line per event, which we then parse and discard before falling back to TraceEvent anyway — on
    /// a multi-million-event .etl that's a huge waste (and a giant temp file + log). We sample only
    /// the first <c>probe</c> events so this stays cheap even on an 880 MB / 5M-event file.
    ///
    /// Conservative: classifies as modern only when almost no events go unhandled (i.e. no real WPP
    /// content). Anything ambiguous, or a probe failure, returns false so the existing tracefmt path
    /// runs unchanged — the WPP sample trace and its tests are unaffected.
    /// </summary>
    private static bool LooksLikeModernTrace(string etlPath, CancellationToken cancellationToken)
    {
        const long probe = 20000;
        long handled = 0, unhandled = 0;
        // A valid .etl holds at least one ETW buffer + headers; a tiny/garbage/truncated file makes the
        // ETWTraceEventSource CONSTRUCTOR throw, and TraceEvent's finalizer then NREs (Dispose(false) on a
        // half-built object) and crashes the whole process on a later GC. Guard the probe: too-small files
        // aren't a real capture — return false and let the tracefmt/managed path report them.
        try { if (!File.Exists(etlPath) || new FileInfo(etlPath).Length < 512) return false; }
        catch { return false; }
        try
        {
            using var source = new Microsoft.Diagnostics.Tracing.ETWTraceEventSource(etlPath);
            void Bump(bool ok)
            {
                if (ok) handled++; else unhandled++;
                if (cancellationToken.IsCancellationRequested || handled + unhandled >= probe)
                    source.StopProcessing();
            }
            source.Dynamic.All += _ => Bump(true);   // manifest / EventSource / TraceLogging
            source.Kernel.All += _ => Bump(true);    // NT kernel logger
            source.UnhandledEvents += _ => Bump(false); // classic/WPP that TraceEvent can't parse
            source.Process();
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"Modern-trace probe failed for {etlPath}: {ex.Message} — using tracefmt path");
            return false;
        }
        long total = handled + unhandled;
        bool modern = total > 0 && (double)unhandled / total < 0.05; // <5% unparseable → no real WPP
        Logger.Instance.Log($"Modern-trace probe for {etlPath}: handled={handled} unhandled={unhandled} -> modern={modern}");
        return modern;
    }

    /// <summary>Eager decode into the retained <see cref="results"/> list (legacy GetResults path).</summary>
    private void DecodeWithTraceEvent(CancellationToken cancellationToken)
        => DecodeWithTraceEvent(line => results.Add(line), cancellationToken, reportFlow: true);

    /// <summary>
    /// Decode an .etl via the TraceEvent library, handing each decoded line to <paramref name="emit"/>.
    /// The streaming search path passes a sink that batches straight to storage (so the full set is
    /// never held); the legacy path passes <c>results.Add</c>. Progress is tracked from a local
    /// counter rather than <c>results.Count</c> so it works regardless of whether rows are retained.
    ///
    /// <paramref name="reportFlow"/> gates the cross-layer FlowProgress updates. On the streaming
    /// search path the engine's scan callback already owns the "Reading &amp; parsing" step and reports
    /// it as it writes rows to storage; if the decoder *also* wrote that step (with an estimate "~%"),
    /// the two alternated every few hundred ms and the percent/"~" flickered. So streaming passes
    /// false and stays silent; only the eager/sync path (no other reporter) reports.
    /// </summary>
    private void DecodeWithTraceEvent(Action<ETLLogLine> emit, CancellationToken cancellationToken, bool reportFlow)
    {
        try
        {
            if (reportFlow) FindNeedlePluginLib.FlowProgress.Begin(FindNeedlePluginLib.FlowPhase.DecodeEtl);
            // No exact event count for an .etl, so estimate total events from the file size using a
            // typical bytes-per-event figure — surfaced as a clearly-marked "~%" estimate.
            const long AvgEtlBytesPerEvent = 180;
            long estTotalEvents = 0;
            try { estTotalEvents = Math.Max(1, new System.IO.FileInfo(inputfile).Length / AvgEtlBytesPerEvent); } catch { }
            using var source = new Microsoft.Diagnostics.Tracing.ETWTraceEventSource(inputfile);

            // source.Process() decodes the whole file synchronously with no built-in progress, so on
            // a multi-million-event .etl it would sit silent for a long time. Report a running count,
            // throttled by wall-clock so we don't flood the sink (no reliable total mid-decode).
            long lastReportMs = Environment.TickCount64;
            long produced = 0;
            void Handle(Microsoft.Diagnostics.Tracing.TraceEvent e)
            {
                if (cancellationToken.IsCancellationRequested) { source.StopProcessing(); return; }
                // ----- Triage scope filter (prototype) -----
                // Drop events outside the requested provider set / time window BEFORE wrapping them into an
                // ETLLogLine (the wrap + insert is the expensive part). This is what lets a "load only these
                // providers / this time range" choice from the triage panel skip most of a huge capture
                // (e.g. the ~90% kernel events you don't want) instead of ingesting everything.
                var scope = DecodeScope.Current;
                if (scope != null && !scope.Keep(e.ProviderName, e.TimeStamp.ToUniversalTime(), -1)) return;
                var line = new ETLLogLine(e);
                var src = line.GetSource() ?? string.Empty;
                providers[src] = providers.TryGetValue(src, out var c) ? c + 1 : 1;
                emit(line);
                produced++;

                long now = Environment.TickCount64;
                if (now - lastReportMs >= 300)
                {
                    lastReportMs = now;
                    // Rough progress from the file-size estimate (marked "~%" since it's not exact).
                    int? pct = estTotalEvents > 0
                        ? Math.Clamp((int)(produced * 100L / estTotalEvents), 1, 99) : (int?)null;
                    _progressSink?.NotifyProgress($"Decoding ETL with TraceEvent… {produced:N0} events");
                    if (reportFlow)
                        FindNeedlePluginLib.FlowProgress.Detail($"{produced:N0} events", pct, estimate: true);
                }
            }

            // Dynamic = manifest/EventSource providers; Kernel = NT kernel logger events. Each event
            // is dispatched to exactly one, so no double counting.
            source.Dynamic.All += Handle;
            source.Kernel.All += Handle;
            source.Process();
            // Decode finished — snap to 100% with the true count so the estimate doesn't linger at
            // ~97% (it never reaches 100 mid-decode, and post-decode Step 1 work keeps this phase up).
            if (reportFlow)
                FindNeedlePluginLib.FlowProgress.Detail($"{produced:N0} events", 100, estimate: false);
            _lastDecodeRowCount = produced;
            Logger.Instance.Log($"TraceEvent decode produced {produced} rows for {inputfile}");
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"TraceEvent decode failed for {inputfile}: {ex.Message}");
        }
    }

    readonly List<ISearchResult> results = new();
    public void LoadInMemory() 
    {
        LoadInMemory(CancellationToken.None);
    }
    public void LoadInMemory(CancellationToken cancellationToken)
    {
        Logger.Instance.Log($"LoadInMemory called for ETLProcessor, file: {inputfile}");
        _badlyFormattedCount = 0;
        _progressSink?.NotifyProgress(0, $"Loading results into memory for {inputfile}");
        if (LoadEarly)
        {
            int total = results.Count;
            int count = 0;
            var lastProgressTime = DateTime.UtcNow;
            foreach(var result in results)
            {
                if (cancellationToken.IsCancellationRequested) return;
                if (result is ETLLogLine etlLogLine)
                {
                    etlLogLine.PreLoad();
                    if (etlLogLine.tasktxt == "Badly formatted event")
                    {
                        _badlyFormattedCount++;
                    }
                }
                count++;
                // Throttle progress to wall-clock (not every 100 rows). Logging every 100 rows wrote
                // ~50,000 lines to the log on a 5M-row file and dominated this pass; PreLoad itself is
                // a no-op for TraceEvent-decoded rows, so that logging was pure waste.
                if (total > 0)
                {
                    var now = DateTime.UtcNow;
                    if ((now - lastProgressTime).TotalMilliseconds >= 250)
                    {
                        lastProgressTime = now;
                        _progressSink?.NotifyProgress((int)(100.0 * count / total),
                            $"Loading results into memory… {count:N0} / {total:N0}");
                    }
                }
            }
        }
        Logger.Instance.Log($"Finished loading results into memory for {inputfile} (badly formatted: {_badlyFormattedCount})");
        _progressSink?.NotifyProgress(100, $"Finished loading results into memory for {inputfile} (badly formatted: {_badlyFormattedCount})");
    }

    public List<ISearchResult> GetResults()
    {
        // Legacy/sync contract: callers expect the full materialized list. For a deferred modern
        // trace, decode into the list on first request. (The streaming search path uses
        // GetResultsWithCallback instead and never lands here, so the full list is never built.)
        if (_traceEventModern && !_decodedToList && results.Count == 0)
        {
            Logger.Instance.Log($"GetResults: lazily decoding deferred modern trace into list for {inputfile}");
            DecodeWithTraceEvent(CancellationToken.None);
            _decodedToList = true;
        }
        // Likewise for a deferred WPP/tracefmt (or text) parse: materialize into the list on first ask.
        if (_deferredFmtParse && !_fmtParsed && results.Count == 0)
        {
            Logger.Instance.Log($"GetResults: lazily parsing deferred tracefmt/text output into list for {inputfile}");
            ParseFormattedOutput(results.Add, CancellationToken.None);
        }
        Logger.Instance.Log($"GetResults called for ETLProcessor, file: {inputfile}, results: {results.Count}");
        return results;
    }

    public async Task GetResultsWithCallback(Action<List<ISearchResult>> onBatch, CancellationToken cancellationToken = default, int batchSize = 1000)
    {
        // Streaming path for a deferred modern trace that hasn't been materialized: decode straight
        // into batches and hand each to onBatch (→ storage) without ever retaining the full set.
        // This is what keeps a 5M-row .etl from piling every row into RAM at once.
        if (_traceEventModern && !_decodedToList && results.Count == 0)
        {
            var streamBatch = new List<ISearchResult>(batchSize);
            DecodeWithTraceEvent(line =>
            {
                if (cancellationToken.IsCancellationRequested) return;
                streamBatch.Add(line);
                if (streamBatch.Count >= batchSize)
                {
                    onBatch(streamBatch);
                    streamBatch = new List<ISearchResult>(batchSize);
                }
            }, cancellationToken, reportFlow: false);
            if (streamBatch.Count > 0 && !cancellationToken.IsCancellationRequested)
            {
                onBatch(streamBatch);
            }
            await Task.CompletedTask;
            return;
        }

        // Streaming path for a deferred WPP/tracefmt (or text passthrough) parse: read the formatted
        // output and hand each batch to onBatch (→ storage) without building the full list. Same memory
        // win as the modern path, now for WPP too.
        if (_deferredFmtParse && !_fmtParsed && results.Count == 0)
        {
            var streamBatch = new List<ISearchResult>(batchSize);
            ParseFormattedOutput(line =>
            {
                if (cancellationToken.IsCancellationRequested) return;
                streamBatch.Add(line);
                if (streamBatch.Count >= batchSize)
                {
                    onBatch(streamBatch);
                    streamBatch = new List<ISearchResult>(batchSize);
                }
            }, cancellationToken);
            if (streamBatch.Count > 0 && !cancellationToken.IsCancellationRequested)
            {
                onBatch(streamBatch);
            }
            await Task.CompletedTask;
            return;
        }

        // Already materialized (a prior GetResults, or the eager TraceEvent fallback) → batch from list.
        var batch = new List<ISearchResult>(batchSize);
        foreach (var result in results)
        {
            if (cancellationToken.IsCancellationRequested) break;
            batch.Add(result);
            if (batch.Count >= batchSize)
            {
                onBatch(batch);
                batch = new List<ISearchResult>(batchSize);
            }
        }
        if (batch.Count > 0)
        {
            onBatch(batch);
        }
        await Task.CompletedTask;
    }

    public List<string> RegisterForExtensions()
    {
        Logger.Instance.Log("RegisterForExtensions called for ETLProcessor");
        return new List<string>() { ".etl", ".txt", ".log" };
    }

    public bool CheckFileFormat()
    {
        Logger.Instance.Log($"CheckFileFormat called for ETLProcessor, file: {inputfile}");
        if (inputfile.EndsWith(".txt") || inputfile.EndsWith(".log"))
        {
            using var reader = new StreamReader(inputfile);
            string? validLine = null;
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!line.StartsWith("Unknown("))
                {
                    validLine = line;
                    break;
                }
            }
            if (validLine == null)
            {
                Logger.Instance.Log($"All lines start with 'Unknown(', not a valid ETL format: {inputfile}");
                return false;
            }
            if (ETLLogLine.DoesHeaderLookRight(validLine))
            {
                Logger.Instance.Log($"File format looks right for .txt/.log: {inputfile}");
                return true;
            }
            else
            {
                Logger.Instance.Log($"File format does NOT look right for .txt/.log: {inputfile}");
                return false;
            }
        }
        else
        {
            Logger.Instance.Log($"Assuming file format is correct for .etl: {inputfile}");
        }
        return true;
    }

    public string GetPluginTextDescription() {
        return "Parses ETL and formatted ETL files";
    }
    public string GetPluginFriendlyName()
    {
        return "ETLProcessor";
    }
    public string GetPluginClassName()
    {
        return IPluginDescription.GetPluginClassNameBase(this);
    }

    public void SetProgressSink(SearchProgressSink sink)
    {
        Logger.Instance.Log($"SetProgressSink called for ETLProcessor, file: {inputfile}");
        _progressSink = sink;
    }

    public (TimeSpan? timeTaken, int? recordCount) GetSearchPerformanceEstimate(CancellationToken cancellationToken = default)
    {
        // An .etl has no event count in its header, so estimate from file size using the same
        // typical bytes-per-event figure the decoder uses for its "~%" progress (≈180 B/event). Rough
        // (event sizes vary by provider) but good enough for the "12,345 / ~N rows" denominator and for
        // the parallel-ingest size gate. WPP traces decode via a different path and report no estimate.
        const long AvgEtlBytesPerEvent = 180;
        try
        {
            if (!string.IsNullOrEmpty(inputfile) && System.IO.File.Exists(inputfile))
            {
                long bytes = new System.IO.FileInfo(inputfile).Length;
                if (bytes > 0)
                {
                    long est = Math.Max(1, bytes / AvgEtlBytesPerEvent);
                    return (null, (int)Math.Min(est, int.MaxValue));
                }
            }
        }
        catch { /* unknown — fall through */ }
        return (null, null);
    }
}
