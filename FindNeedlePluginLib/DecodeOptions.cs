namespace FindNeedlePluginLib;

/// <summary>
/// Cross-cutting decode toggles read by file-format processors at parse time. Set by the UI (or CLI)
/// just before a (re)run, then reset. Kept here so both the plugins (e.g. ETWPlugin) and the host app
/// can see the same flag without a direct project reference between them.
/// </summary>
public static class DecodeOptions
{
    /// <summary>
    /// When true, processors skip their "fail fast on undecodable input" short-circuits and decode
    /// whatever they can — even if the result is mostly unformatted garbage. Drives the result
    /// viewer's "Decode anyway" action for ETLs whose WPP symbols (TMFs) are missing.
    /// </summary>
    public static bool ForceFullDecode { get; set; }

    /// <summary>Which WPP decoder the ETL processor uses. Set by the host from user settings.</summary>
    public static WppDecoder WppDecoder { get; set; } = WppDecoder.Auto;
}

/// <summary>How a WPP (classic) ETL is decoded.</summary>
public enum WppDecoder
{
    /// <summary>tracefmt if the WDK is available, otherwise the managed decoder (default).</summary>
    Auto,
    /// <summary>Always the WDK's tracefmt.exe (today's behavior).</summary>
    Tracefmt,
    /// <summary>Always the built-in managed decoder — no WDK / no external process.</summary>
    Managed,
    /// <summary>Run BOTH tracefmt and the managed decoder and keep whichever decoded more events (tie →
    /// tracefmt, the reference). ~2× decode cost; also surfaces any divergence between the two.</summary>
    Compare,
}

/// <summary>Maps a CLI <c>--wpp-decoder=&lt;value&gt;</c> string to a <see cref="WppDecoder"/>. Pulled out so
/// the mapping is unit-testable and shared. Unknown/empty falls back to <see cref="WppDecoder.Auto"/> — which
/// prefers the WDK's tracefmt (the reference decoder) and only uses the managed decoder when tracefmt isn't
/// installed. The CLI must NOT hard-pin the prototype managed decoder, or a resolver author can't validate
/// against tracefmt.</summary>
public static class WppDecoderParsing
{
    public static WppDecoder FromArg(string value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "tracefmt" => WppDecoder.Tracefmt,
        "managed" => WppDecoder.Managed,
        "compare" => WppDecoder.Compare,
        "auto" => WppDecoder.Auto,
        _ => WppDecoder.Auto,
    };

    /// <summary>True if <paramref name="value"/> is a recognized decoder name (for a "did you mean" warning).</summary>
    public static bool IsKnown(string value)
    {
        var v = (value ?? string.Empty).Trim().ToLowerInvariant();
        return v is "tracefmt" or "managed" or "compare" or "auto";
    }
}
