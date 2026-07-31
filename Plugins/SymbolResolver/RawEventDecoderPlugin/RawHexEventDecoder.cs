using System;
using System.Collections.Generic;
using FindNeedlePluginLib;

namespace RawEventDecoderPlugin;

/// <summary>
/// Reference <see cref="IWppEventDecoder"/> — the LAST-RESORT decode escape hatch. Where the symbol resolvers
/// LOCATE a PDB/TMF and let FindNeedle decode, this DECODES the raw event itself: for a provider with no TMF
/// at all, it's handed the raw argument blob and returns a message string.
///
/// This template claims the provider GUIDs listed in <c>FINDNEEDLE_RAWDECODE_GUIDS</c> (';'-separated) and
/// renders each event's payload as a hex dump — so an otherwise-undecodable provider is at least inspectable.
/// A REAL decoder would replace <see cref="TryDecode"/>'s body with parsing of <see cref="WppRawEvent.Data"/>
/// per that provider's known layout (read its args off the blob using <see cref="WppRawEvent.PointerSize"/>).
///
/// Note the split of responsibility: <see cref="CanDecode"/> is asked once per GUID (cached by FindNeedle),
/// while <see cref="TryDecode"/> runs per event on the decode thread — so keep TryDecode fast and non-blocking.
/// Implements <see cref="IPluginDescription"/> so the plugin subsystem discovers it.
/// </summary>
public sealed class RawHexEventDecoder : IWppEventDecoder, IPluginDescription
{
    /// <summary>';'-separated provider/trace GUIDs this decoder should claim, e.g.
    /// <c>d58c126f-b309-11d1-969e-0000f875a5bc;{another-guid}</c>.</summary>
    public const string GuidsEnv = "FINDNEEDLE_RAWDECODE_GUIDS";

    public bool CanDecode(Guid providerGuid)
    {
        foreach (var g in ClaimedGuids())
            if (g == providerGuid) return true;
        return false;
    }

    public string TryDecode(WppRawEvent e)
    {
        // TEMPLATE: a real decoder parses e.Data per its provider's format. This one just renders the raw
        // bytes legibly, which is genuinely useful as a fallback — you can see the payload of a provider that
        // has no symbols at all. Fast + non-blocking, as required on the decode hot path.
        var span = e.Data.Span;
        var hex = span.Length == 0 ? "(no data)" : Convert.ToHexString(span);
        e.Log($"raw-decoded msg {e.MessageNumber} ({span.Length} bytes)");
        return $"[raw msg {e.MessageNumber}] {hex}";
    }

    private static IEnumerable<Guid> ClaimedGuids()
    {
        var v = Environment.GetEnvironmentVariable(GuidsEnv);
        if (string.IsNullOrWhiteSpace(v)) yield break;
        foreach (var s in v.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (Guid.TryParse(s, out var g)) yield return g;
    }

    public string GetPluginTextDescription()
        => "Last-resort WPP decode: for provider GUIDs listed in FINDNEEDLE_RAWDECODE_GUIDS (no TMF needed), "
         + "renders each raw event's payload as a hex dump. A template — fork TryDecode to parse your format.";
    public string GetPluginFriendlyName() => "Raw Hex Event Decoder";
    public string GetPluginClassName() => IPluginDescription.GetPluginClassNameBase(this);
}
