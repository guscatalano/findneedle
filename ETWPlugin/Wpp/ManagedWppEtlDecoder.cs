using System;
using System.Collections.Generic;
using Microsoft.Diagnostics.Tracing;

namespace findneedle.Wpp;

/// <summary>One managed-decoded WPP event: the wire fields TraceEvent gives us + the message this decoder
/// rendered from the TMF (the part tracefmt would otherwise produce).</summary>
public sealed class WppDecodedEvent
{
    public DateTime TimeStamp { get; init; }
    public int ProcessId { get; init; }
    public int ThreadId { get; init; }
    public int Cpu { get; init; }                    // processor number (was hardcoded 0 in the row)
    public int EventLevel { get; init; } = -1;        // ETW severity (1=Critical..5=Verbose); -1 = unknown
    public Guid ActivityId { get; init; }             // for causal-sequence correlation
    public Guid RelatedActivityId { get; init; }
    public Guid ProviderGuid { get; init; }
    public Guid MessageGuid { get; init; }
    public int MessageNumber { get; init; }
    public string Component { get; init; } = "";
    public string Level { get; init; } = "";          // TMF flag/level name (e.g. TRACE_GENERAL)
    public string Func { get; init; } = "";
    public string Message { get; init; } = "";
}

/// <summary>
/// A fully managed WPP decoder — no WDK, no tracefmt.exe. It leans on TraceEvent purely to READ the ETL
/// (which already de-frames each classic WPP event for us), and does the WPP formatting itself via
/// <see cref="TmfDatabase"/> + <see cref="WppMessageFormatter"/>.
///
/// The key finding that makes this simple: for a classic WPP event, TraceEvent exposes
///   • <c>TraceEvent.TaskGuid</c> = the WPP message GUID (the .tmf filename),
///   • <c>TraceEvent.ID</c>       = the WPP message number (the #typev number), and
///   • <c>TraceEvent.EventData()</c> = exactly the packed argument blob (no extra header).
/// So decoding is: look up (TaskGuid, ID) in the TMF, decode the typed args off EventData(), apply the
/// format string. This is the managed reimplementation of tracefmt's core loop.
///
/// PROTOTYPE status: validated end-to-end against a real WppEmitter capture (integer/hex args). String and
/// exotic WPP item types are handled by the formatter but not yet validated against a capture that uses them.
/// Not wired into the ETLProcessor decode path — this is a standalone decoder for evaluation.
/// </summary>
public sealed class ManagedWppEtlDecoder
{
    private readonly TmfDatabase _tmf;

    public ManagedWppEtlDecoder(TmfDatabase tmf) => _tmf = tmf ?? throw new ArgumentNullException(nameof(tmf));

    /// <summary>Number of events whose (message GUID, number) had no TMF entry — the "missing symbols" tally.</summary>
    public long Unresolved { get; private set; }

    /// <summary>Distinct message GUIDs that had no TMF entry — the "requires symbol XYZ" list.</summary>
    public HashSet<Guid> UnresolvedGuids { get; } = new();

    /// <summary>Count of non-WPP (manifest/EventSource) events the trace also contains — surfaced so a caller
    /// can note a MIXED trace whose modern part the WPP decoder doesn't render. (Excludes ETW session/rundown
    /// infrastructure, which can't be cleanly told apart from real app events at this layer — see gap #10.)</summary>
    public long ModernEvents { get; private set; }

    /// <summary>Count of events a manual <see cref="FindNeedlePluginLib.IWppEventDecoder"/> plugin formatted
    /// after the TMF lookup missed — the last-resort decode tier.</summary>
    public long PluginDecoded { get; private set; }

    // Raw-event decoder plugins (fetched once per Decode) + a per-GUID claim cache: for a given provider GUID,
    // the decoder that claimed it, or null once we've asked and none did. Keeps CanDecode to one call per GUID.
    private System.Collections.Generic.IReadOnlyList<FindNeedlePluginLib.IWppEventDecoder> _eventDecoders;
    private readonly Dictionary<Guid, FindNeedlePluginLib.IWppEventDecoder> _decoderClaims = new();

    /// <summary>
    /// Decode the WPP (classic) events in <paramref name="etlPath"/>, handing each resolved event to
    /// <paramref name="onEvent"/>. Non-WPP events with no TMF entry are counted in <see cref="Unresolved"/> /
    /// <see cref="UnresolvedGuids"/>. <paramref name="maxEvents"/> caps how many events are processed (for a
    /// cheap sample pre-scan). WPP-only by design — matches tracefmt, which also renders only WPP.
    /// </summary>
    public void Decode(string etlPath, Action<WppDecodedEvent> onEvent,
        System.Threading.CancellationToken cancellationToken = default, long maxEvents = long.MaxValue)
    {
        // A tiny/garbage/truncated .etl makes the ETWTraceEventSource constructor throw, and TraceEvent's
        // finalizer then NREs and crashes the process on a later GC. Guard against it — too-small files hold
        // no decodable events anyway.
        if (!System.IO.File.Exists(etlPath) || new System.IO.FileInfo(etlPath).Length < 512) return;
        using var source = new ETWTraceEventSource(etlPath);
        int pointerSize = source.PointerSize > 0 ? source.PointerSize : 8;
        // Manual decoder plugins (if any host registered them) — the last-resort tier below the TMF lookup.
        _eventDecoders = FindNeedlePluginLib.WppEventDecoding.GetDecoders();
        _decoderClaims.Clear();
        long seen = 0;
        source.AllEvents += ev =>
        {
            if (cancellationToken.IsCancellationRequested) { source.StopProcessing(); return; }
            if (++seen >= maxEvents) source.StopProcessing();
            // WPP classic events carry the message GUID on TaskGuid; anything else (kernel/manifest) has a
            // different or empty TaskGuid and won't match a TMF entry.
            var guid = ev.TaskGuid;
            if (guid == Guid.Empty) return;
            int msgNum = (int)ev.ID;
            if (!_tmf.TryGet(guid, msgNum, out var entry))
            {
                // No TMF for this (GUID, msgNum). Before giving up, offer the raw event to a manual decoder
                // plugin (a provider whose format is known only in code). If one formats it, emit; else count
                // it as unresolved exactly as before.
                if (TryPluginDecode(guid, msgNum, ev, pointerSize, out var pluginMsg))
                {
                    PluginDecoded++;
                    onEvent(BuildEvent(ev, guid, msgNum, pluginMsg, component: "", level: "", func: ""));
                    return;
                }
                Unresolved++; UnresolvedGuids.Add(guid); return;
            }

            string message;
            try { message = WppMessageFormatter.Format(entry, ev.EventData(), pointerSize); }
            catch { message = ""; }

            onEvent(BuildEvent(ev, guid, msgNum, message, entry.Component, entry.Level, entry.Func));
        };
        source.Process();
    }

    private WppDecodedEvent BuildEvent(TraceEvent ev, Guid guid, int msgNum, string message,
        string component, string level, string func) => new()
    {
        TimeStamp = ev.TimeStamp,
        ProcessId = ev.ProcessID,
        ThreadId = ev.ThreadID,
        Cpu = SafeInt(() => ev.ProcessorNumber),
        EventLevel = SafeInt(() => (int)ev.Level, -1),
        ActivityId = SafeGuid(() => ev.ActivityID),
        RelatedActivityId = SafeGuid(() => ev.RelatedActivityID),
        ProviderGuid = SafeGuid(() => ev.ProviderGuid),
        MessageGuid = guid,
        MessageNumber = msgNum,
        Component = component,
        Level = level,
        Func = func,
        Message = message,
    };

    /// <summary>Ask a manual decoder plugin to format a raw event whose TMF lookup missed. The claiming
    /// decoder is cached per GUID (CanDecode runs once per GUID), and TryDecode runs on the decode thread —
    /// a plugin fault never drops the trace, it just falls through to "unresolved".</summary>
    private bool TryPluginDecode(Guid guid, int msgNum, TraceEvent ev, int pointerSize, out string message)
    {
        message = null;
        if (_eventDecoders == null || _eventDecoders.Count == 0) return false;

        if (!_decoderClaims.TryGetValue(guid, out var decoder))
        {
            decoder = null;
            foreach (var d in _eventDecoders)
            {
                try { if (d.CanDecode(guid)) { decoder = d; break; } }
                catch { /* a broken claim must not break decode */ }
            }
            _decoderClaims[guid] = decoder; // cache the winner (or null = "asked, nobody claims")
        }
        if (decoder == null) return false;

        try
        {
            var raw = new FindNeedlePluginLib.WppRawEvent
            {
                ProviderGuid = guid,
                MessageNumber = msgNum,
                Data = ev.EventData(),
                PointerSize = pointerSize,
                TimeStamp = ev.TimeStamp,
                ProcessId = ev.ProcessID,
                ThreadId = ev.ThreadID,
                Cpu = SafeInt(() => ev.ProcessorNumber),
                Level = SafeInt(() => (int)ev.Level, -1),
                Log = m => { try { FindNeedlePluginLib.Logger.Instance.Log($"[wpp-decode {guid:D}] {m}"); } catch { } },
            };
            message = decoder.TryDecode(raw);
            return !string.IsNullOrEmpty(message);
        }
        catch { return false; }
    }

    // TraceEvent property access is best-effort — a few can throw on odd events; never let that drop a row.
    private static int SafeInt(Func<int> f, int fallback = 0) { try { return f(); } catch { return fallback; } }
    private static Guid SafeGuid(Func<Guid> f) { try { return f(); } catch { return Guid.Empty; } }

    /// <summary>Convenience: decode into a list.</summary>
    public List<WppDecodedEvent> DecodeToList(string etlPath)
    {
        var list = new List<WppDecodedEvent>();
        Decode(etlPath, list.Add);
        return list;
    }
}
