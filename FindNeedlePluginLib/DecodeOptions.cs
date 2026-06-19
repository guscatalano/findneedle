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
}
