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
    public static int ComputeExitCode(int rowsDecoded, int provisionInvocations, int provisionSucceeded)
    {
        if (rowsDecoded <= 0) return NothingDecoded;
        if (provisionInvocations > 0 && provisionSucceeded < provisionInvocations) return UnresolvedSymbols;
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
