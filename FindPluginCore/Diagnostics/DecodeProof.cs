namespace FindPluginCore.Diagnostics;

/// <summary>
/// The CLI "decode-proof" verdict: an exit code an external ISymbolResolver author asserts on to prove a
/// WPP .etl actually decoded. Pulled out of Program.Main so the 0/1/2 boundaries are unit-testable without
/// spawning the binary — this logic silently returned the wrong code once (see wpp-decode-proof memory) and
/// nothing caught it.
/// </summary>
public static class DecodeProof
{
    public const int FullyDecoded = 0;      // rows decoded, nothing left unformatted
    public const int UnresolvedSymbols = 1; // rows decoded, but some WPP events remain unformatted (missing TMF)
    public const int NothingDecoded = 2;    // zero rows

    /// <summary>
    /// The verdict is driven by the ACTUAL decode result, NOT by whether symbol provisioning ran or succeeded.
    /// Provisioning is now discovery-driven and fires UP FRONT (before the decode) — and it can over-fire on a
    /// trace tracefmt self-resolves, where the resolver is asked, returns nothing, yet every event still
    /// decodes. "Provisioning invoked but made 0 symbols" is a resolver diagnostic (reported separately), not
    /// evidence the output is incomplete. So: rows + events-left-unformatted decide the exit code.
    /// </summary>
    /// <param name="rowsDecoded">Authoritative decoded-row count (from ResultStorage, NOT the lazy AtSearch stat).</param>
    /// <param name="unresolvedEvents">WPP events the decoder saw but could NOT format (missing TMF). &gt;0 with
    /// rows &gt; 0 is a partial decode → UnresolvedSymbols; 0 means everything present was rendered.</param>
    public static int ComputeExitCode(int rowsDecoded, long unresolvedEvents = 0)
    {
        if (rowsDecoded <= 0) return NothingDecoded;
        if (unresolvedEvents > 0) return UnresolvedSymbols;
        return FullyDecoded;
    }

    /// <summary>Human-readable verdict for the summary line.</summary>
    public static string Describe(int exitCode) => exitCode switch
    {
        FullyDecoded => "fully decoded",
        UnresolvedSymbols => "decoded WITH UNRESOLVED symbols",
        _ => "no rows decoded",
    };
}
