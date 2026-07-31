using System;
using System.Collections.Generic;

namespace FindNeedlePluginLib;

/// <summary>
/// Cross-layer seam that lets the WPP decoder (in ETWPlugin, which can't reference the host or the plugin
/// subsystem) consult the host-registered <see cref="IWppEventDecoder"/> plugins. Same ambient-static pattern
/// as <see cref="WppSymbolProvisioning"/> / <see cref="DecodeOptions"/>: the host (FindNeedleUX) sets
/// <see cref="Provider"/> at startup to enumerate the loaded decoder plugins; when none is registered (CLI,
/// tests) the decode path simply has no manual decoders and behaves exactly as before.
/// </summary>
public static class WppEventDecoding
{
    /// <summary>Host-supplied enumerator of the registered raw-event decoder plugins. Null when no host set it.</summary>
    public static Func<IReadOnlyList<IWppEventDecoder>> Provider { get; set; }

    /// <summary>Cheap gate: is any decoder source registered?</summary>
    public static bool HasDecoders => Provider != null;

    /// <summary>The registered decoders, or an empty list. Never throws — a broken provider yields none, so it
    /// can't break the decode.</summary>
    public static IReadOnlyList<IWppEventDecoder> GetDecoders()
    {
        var provider = Provider;
        if (provider == null) return Array.Empty<IWppEventDecoder>();
        try { return provider() ?? Array.Empty<IWppEventDecoder>(); }
        catch { return Array.Empty<IWppEventDecoder>(); }
    }
}
