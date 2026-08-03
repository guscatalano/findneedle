using System;

namespace FindNeedlePluginLib;

/// <summary>
/// The WPP message/trace GUID a capture needs a TMF for, handed to an <see cref="IWppTmfResolver"/>. This is
/// the ETL-ONLY resolution path: unlike <see cref="SymbolLookupRequest"/> (which is a PDB identity read from
/// a binary's PE header), this carries only what a bare <c>.etl</c> exposes — the trace GUID of an event
/// whose format string (TMF) is missing, plus the capture it came from as a source hint.
/// </summary>
public sealed class WppTmfResolveRequest
{
    public WppTmfResolveRequest(Guid messageGuid, string etlPath)
    {
        MessageGuid = messageGuid;
        EtlPath = etlPath;
    }

    /// <summary>The WPP message/trace GUID with no local TMF. A <c>.tmf</c> file is keyed by exactly this
    /// GUID, so it's all a TMF store needs to find the right format definitions.</summary>
    public Guid MessageGuid { get; }

    /// <summary>Full path to the .etl being decoded (its folder is a source hint), or null on the manual path.</summary>
    public string EtlPath { get; }

    /// <summary>The GUID in the canonical TMF-filename form: lowercase, hyphenated, no braces
    /// (<c>d58c126f-b309-11d1-969e-0000f875a5bc</c>). The naming most WPP TMF stores use.</summary>
    public string GuidD => MessageGuid.ToString("D");

    /// <summary>The GUID as 32 lowercase hex digits, no hyphens — for stores that name files that way.</summary>
    public string GuidN => MessageGuid.ToString("N");

    /// <summary>Diagnostic sink — anything the resolver writes here lands in the resolution log FindNeedle
    /// shows (attributed to the resolver, in context under this GUID). Never null; call it unconditionally.</summary>
    public Action<string> Log { get; init; } = _ => { };

    public override string ToString() => $"TMF for {GuidD}";
}

/// <summary>
/// Plugin seam for locating WPP symbols when there is NO binary — just <c>.etl</c> files. FindNeedle's other
/// resolver kind (<see cref="ISymbolResolver"/>) is binary-driven: it reads a PDB identity from a
/// <c>.dll/.exe/.sys</c> and finds the PDB, from which TMFs are extracted with <c>tracepdb</c>. A capture
/// that ships only ETLs has no binary to read an identity from, so that path never fires.
///
/// This seam works from the one durable key an ETL-only capture exposes — the message/trace GUID of an event
/// whose TMF is missing (FindNeedle already discovers these during decode). Implement this — together with
/// <see cref="IPluginDescription"/> — to answer "given this trace GUID, where is its <c>.tmf</c>?" from your
/// own source: a TMF share/store, a build drop, a REST service. Return a local or UNC path to a <c>.tmf</c>
/// file that defines the GUID, or <c>null</c> to pass. FindNeedle copies the returned TMF into its cache and
/// retries the decode — no PDB, no <c>tracepdb</c>, no WDK involved.
///
/// Discovered exactly like <see cref="ISymbolResolver"/> (list the DLL under the plugin registry seam);
/// consulted only on the build/extract path, so a resolver may do network I/O. Each resolver call is bounded
/// by a per-call timeout, so a hung resolver can't stall the decode.
/// </summary>
public interface IWppTmfResolver
{
    /// <summary>Return a path to a <c>.tmf</c> file that defines <paramref name="request"/>'s message GUID, or
    /// null if this resolver can't find it. Use this when the TMF already exists as a file (a share, a store).</summary>
    string TryResolveTmf(WppTmfResolveRequest request);

    /// <summary>
    /// Optional second capability: return the TMF <b>content</b> (the text a <c>.tmf</c> file holds) directly,
    /// for a resolver that GENERATES the format itself rather than pointing at an existing file — e.g. it
    /// derived the TMF from a binary/PDB in memory, or synthesizes it from an internal schema. Return the TMF
    /// text, or null to fall back to <see cref="TryResolveTmf"/>. FindNeedle writes the returned text into its
    /// TMF cache as <c>&lt;guid&gt;.tmf</c>, exactly as if it had copied a file — so the format must be valid
    /// TMF (a <c>&lt;guid&gt; &lt;component&gt;</c> header + <c>#typev</c> blocks). Default: null (path-only).
    /// </summary>
    string TryResolveTmfText(WppTmfResolveRequest request) => null;

    /// <summary>
    /// Per-call hang budget in milliseconds — see <see cref="ISymbolResolver.SuggestedTimeoutMs"/>. Return your
    /// own worst case for a slow store/generator; the host honors the LARGER of this and its configured default.
    /// Default 0 = use the host default. (A default interface method, so existing resolvers need not implement it.)
    /// </summary>
    int SuggestedTimeoutMs => 0;
}
