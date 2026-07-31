using System;

namespace FindNeedlePluginLib;

/// <summary>
/// One raw ETL event handed to an <see cref="IWppEventDecoder"/> — everything a bare event carries once
/// FindNeedle has failed to format it from a TMF. This is the DECODE escape hatch: there is no format string
/// available (that's why we're here), so <see cref="Data"/> is the raw argument blob and the plugin owns the
/// wire-format parsing per its provider's known layout.
/// </summary>
public sealed class WppRawEvent
{
    /// <summary>The provider / trace GUID (WPP message GUID) this event came from.</summary>
    public Guid ProviderGuid { get; init; }

    /// <summary>The WPP message number (the <c>#typev</c> number) — which trace statement fired.</summary>
    public int MessageNumber { get; init; }

    /// <summary>The raw packed argument blob (exactly what <c>TraceEvent.EventData()</c> returned). The plugin
    /// parses this itself; FindNeedle can't hand over typed args because it has no TMF describing them.</summary>
    public ReadOnlyMemory<byte> Data { get; init; }

    /// <summary>Native pointer size of the capture (4 or 8) — needed to read pointer/size-typed args.</summary>
    public int PointerSize { get; init; } = 8;

    public DateTime TimeStamp { get; init; }
    public int ProcessId { get; init; }
    public int ThreadId { get; init; }
    public int Cpu { get; init; }
    public int Level { get; init; } = -1;              // ETW severity (1=Critical..5=Verbose); -1 = unknown

    /// <summary>Diagnostic sink — anything the decoder writes here lands in FindNeedle's log. Never null (a
    /// no-op when the host wires none), so a plugin can call it unconditionally.</summary>
    public Action<string> Log { get; init; } = _ => { };
}

/// <summary>
/// Plugin seam for the LAST-RESORT case: an event whose format string (TMF) is missing AND can't be located,
/// for a provider whose layout you know only in code. Where <see cref="ISymbolResolver"/> and
/// <see cref="IWppTmfResolver"/> LOCATE symbols and let FindNeedle decode, this plugin DECODES the raw event
/// itself — it is handed the raw argument blob and returns the formatted message.
///
/// Use it for a manifest-less / proprietary provider, or to override how a specific GUID renders. It is the
/// bottom tier of the decode path: built-in TMF → <see cref="IWppTmfResolver"/> (find the TMF) → this. Because
/// there is no TMF, the plugin must parse <see cref="WppRawEvent.Data"/> per the provider's own format — so it
/// only helps when you know that format out of band. If you can instead SHIP the format, prefer
/// <see cref="IWppTmfResolver"/> (including its <c>TryResolveTmfText</c> option) — it's cheaper and reusable.
///
/// PERFORMANCE: <see cref="TryDecode"/> runs per-event on the streaming decode thread — it MUST be fast and
/// non-blocking (no network, no locks). <see cref="CanDecode"/> is asked once per GUID and cached, so a
/// provider you don't claim costs nothing per event. Discovered like the other resolver plugins; implement
/// <see cref="IPluginDescription"/> alongside so the plugin subsystem finds it.
/// </summary>
public interface IWppEventDecoder
{
    /// <summary>Does this decoder handle events from <paramref name="providerGuid"/>? Asked once per GUID and
    /// cached — keep it cheap and side-effect-free.</summary>
    bool CanDecode(Guid providerGuid);

    /// <summary>Format one raw event this decoder claimed into a message string, or null to pass (the event is
    /// then counted as unresolved, as before). Runs on the decode hot path — must be fast and non-blocking.</summary>
    string TryDecode(WppRawEvent rawEvent);
}
