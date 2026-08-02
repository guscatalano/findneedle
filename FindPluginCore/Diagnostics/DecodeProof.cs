namespace FindPluginCore.Diagnostics;

/// <summary>
/// The CLI "decode-proof" verdict: an exit code an external ISymbolResolver author asserts on to prove a
/// WPP .etl actually decoded. Pulled out of Program.Main so the 0/1/2 boundaries are unit-testable without
/// spawning the binary — this logic silently returned the wrong code once (see wpp-decode-proof memory) and
/// nothing caught it.
/// </summary>
public static class DecodeProof
{
    public const int FullyDecoded = 0;      // rows decoded, no unresolved symbols
    public const int UnresolvedSymbols = 1; // rows decoded, but provisioning ran and couldn't resolve everything
    public const int NothingDecoded = 2;    // zero rows

    /// <param name="rowsDecoded">Authoritative decoded-row count (from ResultStorage, NOT the lazy AtSearch stat).</param>
    /// <param name="provisionInvocations">How many times the WPP symbol-provisioning seam was invoked.</param>
    /// <param name="provisionSucceeded">How many of those invocations produced new symbols.</param>
    /// <param name="unresolvedEvents">
    /// WPP events the decoder saw but could NOT format (missing TMF). Non-zero here means symbols are still
    /// missing even on a PARTIAL decode that never tripped the all-unknown provisioning fail-fast — so "some
    /// rows decoded" must report UnresolvedSymbols, not FullyDecoded. This closes the "0 missing / lots
    /// unformatted / exit 0" contradiction the CLI reported.
    /// </param>
    public static int ComputeExitCode(int rowsDecoded, int provisionInvocations, int provisionSucceeded, long unresolvedEvents = 0)
    {
        if (rowsDecoded <= 0) return NothingDecoded;
        if (provisionInvocations > 0 && provisionSucceeded < provisionInvocations) return UnresolvedSymbols;
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
