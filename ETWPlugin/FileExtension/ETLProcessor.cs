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

    private int _badlyFormattedCount = 0;

    // A modern (non-WPP) .etl decodes via the TraceEvent library. We *defer* that decode to the
    // consumer instead of doing it eagerly in DoPreProcessing: GetResultsWithCallback streams it
    // straight into batches (→ storage) without ever building the full in-memory list, so a 5M-row
    // trace costs ~one batch of RAM instead of all rows at once. GetResults() (the legacy/sync
    // contract) still materializes the list lazily on first call. _decodedToList guards that the
    // eager decode runs at most once.
    private bool _traceEventModern = false;
    private bool _decodedToList = false;

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
            rlog.AppendLine();
            rlog.AppendLine("Search paths consulted:");
            rlog.AppendLine($"  TRACE_FORMAT_SEARCH_PATH = {Environment.GetEnvironmentVariable("TRACE_FORMAT_SEARCH_PATH")}");
            rlog.AppendLine($"  _NT_SYMBOL_PATH          = {Environment.GetEnvironmentVariable("_NT_SYMBOL_PATH")}");
            rlog.AppendLine();
            rlog.AppendLine("Outcome:");
            if (_prescanFailFast)
                rlog.AppendLine("  (Fast pre-scan of the first ~8 MB only — the full file was NOT decoded. Counts are sample-scoped.)");
            rlog.AppendLine($"  events={currentResult.TotalEventsProcessed:N0}  formatErrors={currentResult.TotalFormatErrors:N0}  unknowns={currentResult.TotalFormatsUnknown:N0}");
            rlog.AppendLine($"  {(currentResult.TotalFormatsUnknown == 0 ? "All events formatted — every required TMF was found." : "Some events couldn't be formatted — a TMF is missing. Add the matching PDB/symbol path and Build TMFs.")}");
            if (_missingTmfGuids.Count > 0)
            {
                rlog.AppendLine();
                rlog.AppendLine($"Missing TMFs ({_missingTmfGuids.Count}) — these message GUIDs had no format info; supply each TMF (or the PDB it comes from):");
                foreach (var g in _missingTmfGuids)
                    rlog.AppendLine($"  {g}   →  expected file {g}.tmf");
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
        var getLock = 50;

        // Reset per-run decode state. This processor instance can be reused across searches (e.g. the
        // fail-fast open followed by "Decode anyway"); without this, stale values like _decodeMethod
        // leak from the previous run (so the forced-decode label/banner would be wrong).
        _decodeMethod = "(pending)";
        _lastDecodeRowCount = 0;
        _forcedGuidCounts.Clear();
        _prescanFailFast = false;
        _missingTmfGuids.Clear();

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
            // Skip tracefmt for modern (non-WPP) traces — it can't decode them and would emit one
            // "Unknown" line per event. Decode directly with TraceEvent instead.
            if (LooksLikeModernTrace(inputfile, cancellationToken))
            {
                // Don't decode here — defer to the consumer so the streaming search path
                // (GetResultsWithCallback) can pump events straight to storage without ever
                // holding the whole trace in RAM. GetResults() decodes lazily for legacy callers.
                Logger.Instance.Log($"{inputfile} is a modern (non-WPP) trace; will decode with TraceEvent on demand (deferred)");
                _traceEventModern = true;
                _decodeMethod = "TraceEvent (modern)";
                _progressSink?.NotifyProgress(100, $"Preprocessing complete for {inputfile} (decode deferred)");
                return;
            }

            // Fast pre-scan: decode only the first few MB to estimate decodability before the full
            // (slow, processing-bound) run. If the sample is ~all unformattable, it's a missing-symbols
            // problem — bail in well under a second with the missing GUIDs instead of grinding the
            // whole file. Skipped entirely when the user chose "Decode anyway" (force full decode).
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
                RetainTracefmtArtifacts();
                var which = _missingTmfGuids.Count > 0 ? string.Join(", ", _missingTmfGuids.Take(5)) : "?";
                Logger.Instance.Log($"Pre-scan fail-fast: {inputfile} — sample {pre.TotalFormatsUnknown:N0}/{pre.TotalEventsProcessed:N0} unformattable. Missing: {which}");
                _progressSink?.NotifyProgress(100,
                    $"Pre-scan: ~0% of events decodable — missing WPP symbols (TMF for {which}). Set a symbol/TMF path in settings and reopen.");
                return;
            }

            Logger.Instance.Log($"Calling TraceFmt.ParseSimpleETL for file: {inputfile}");
            currentResult = TraceFmt.ParseSimpleETL(inputfile, tempPath, _progressSink);
            if (currentResult == null)
            {
                Logger.Instance.Log($"TraceFmt result is null for {inputfile}, skipping ETL processing.");
                _progressSink?.NotifyProgress(100, $"TraceFmt not found or failed for {inputfile}, skipping ETL processing.");
                return;
            }

            // Fail fast: tracefmt's summary (already parsed) tells us how much is decodable BEFORE we
            // grind line-by-line through the output. If (nearly) everything is unformattable, it's a
            // missing-symbols problem — sample the missing GUIDs, report, and skip the multi-million-
            // line parse + the (futile) TraceEvent fallback so the user can fix symbols and reopen.
            long total = currentResult.TotalEventsProcessed;
            long unknown = currentResult.TotalFormatsUnknown;
            if (!DecodeOptions.ForceFullDecode && total > 0 && unknown >= total * 0.99)
            {
                _decodeMethod = "tracefmt (WPP) — symbols missing";
                _lastDecodeRowCount = 0;
                SampleMissingGuids(currentResult.outputfile, 200_000);
                RetainTracefmtArtifacts();
                var which = _missingTmfGuids.Count > 0 ? string.Join(", ", _missingTmfGuids.Take(5)) : "?";
                Logger.Instance.Log($"Fail-fast: {inputfile} — {unknown:N0}/{total:N0} events unformattable (missing TMF). Skipping full parse. Missing: {which}");
                _progressSink?.NotifyProgress(100,
                    $"Can't decode: {unknown:N0} of {total:N0} events need WPP symbols (missing TMF for {which}). Set a symbol/TMF path in settings and reopen.");
                return;
            }
        }
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
                    if (providers.ContainsKey(etlline.GetSource()))
                    {
                        providers[etlline.GetSource()]++;
                    }
                    else
                    {
                        providers[etlline.GetSource()] = 1;
                    }
                    results.Add(etlline);
                    lineCount++;
                    if (lineCount % 1000 == 0)
                    {
                        Logger.Instance.Log($"Processed {lineCount} lines for {inputfile}");
                        _progressSink?.NotifyProgress(20 + (int)(70.0 * lineCount / 100000), $"Processed {lineCount} lines");
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
                        results.Add(ETLLogLine.Unformatted(kv.Key, kv.Value, inputfile));
                        lineCount++;
                    }
                    providers["(unformatted WPP)"] = checked((int)Math.Min(int.MaxValue, _forcedGuidCounts.Values.Sum()));
                }
                Logger.Instance.Log($"Finished reading output file for {inputfile}, total lines: {lineCount}");
                _progressSink?.NotifyProgress(90, $"Finished reading output file, total lines: {lineCount}");
                // Reaching here via the .etl branch means tracefmt formatted it (the text branch
                // already set its method). Rows = what we parsed out of the formatted output.
                if (_decodeMethod == "(pending)")
                    _decodeMethod = DecodeOptions.ForceFullDecode && corruptCount > 0
                        ? $"tracefmt (WPP) — forced; {corruptCount:N0} events unformatted across {_forcedGuidCounts.Count} GUID(s)"
                        : "tracefmt (WPP)";
                _lastDecodeRowCount = results.Count;
                break;
            }
            catch (Exception ex)
            {
                Logger.Instance.Log($"Exception while reading output file for {inputfile}: {ex.Message}");
                Thread.Sleep(100);
                getLock--; // Sometimes tracefmt can hold the lock, wait until file is ready
            }
        }

        RetainTracefmtArtifacts();

        // Fallback for non-WPP traces. tracefmt only formats WPP (driver/software) traces; for a
        // modern .etl (EventSource / manifest / kernel) it emits "Unknown" lines and we end up
        // with zero rows. In that case decode the .etl directly with the TraceEvent library — the
        // same source LiveCollector uses for real-time — which understands those event kinds.
        //
        // Only fall back when tracefmt itself processed ~no events: that signals a non-WPP trace it
        // couldn't read. If tracefmt DID process events (a WPP trace) but we got no rows, it's a
        // missing-symbols problem — TraceEvent can't decode WPP either, so the fallback would just
        // burn time for nothing. (The fail-fast normally catches this; "Decode anyway" reaches here.)
        bool tracefmtProcessedEvents = currentResult != null && currentResult.TotalEventsProcessed > 0;
        if (results.Count == 0
            && !tracefmtProcessedEvents
            && !inputfile.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            && !inputfile.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
            && File.Exists(inputfile))
        {
            Logger.Instance.Log($"tracefmt produced no rows for {inputfile}; falling back to TraceEvent decode");
            _progressSink?.NotifyProgress(50, "Decoding ETL with TraceEvent (non-WPP trace)");
            _decodeMethod = "TraceEvent (fallback after tracefmt)";
            DecodeWithTraceEvent(cancellationToken);
        }

        Logger.Instance.Log(
            $"Decode summary {inputfile}: method={_decodeMethod} rows={_lastDecodeRowCount:N0} " +
            $"providers={providers.Count}" +
            (currentResult != null && _decodeMethod.StartsWith("tracefmt")
                ? $" eventsProcessed={currentResult.TotalEventsProcessed} formatErrors={currentResult.TotalFormatErrors} unknowns={currentResult.TotalFormatsUnknown}"
                : ""));
        Logger.Instance.Log($"DoPreProcessing complete for {inputfile}");
        _progressSink?.NotifyProgress(100, $"Preprocessing complete for {inputfile}");
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

        // Already materialized (text / WPP path, or a prior GetResults) → batch from the list.
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
        // Stub: no performance data available
        return (null, null);
    }
}
