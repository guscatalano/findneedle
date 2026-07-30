using System;
using System.Collections.Generic;

namespace FindNeedlePluginLib;

/// <summary>
/// Cross-layer seam for ON-DEMAND WPP symbol provisioning during a decode. When a WPP/ETL decode fails
/// only because its symbols (TMFs) are missing, the ETL processor asks the host to try to make them
/// available — resolve the PDBs (via the built-in lookup AND the <see cref="ISymbolResolver"/> plugins:
/// SMB share, symbol server, …), extract their TMFs into the managed cache, and refresh
/// <c>TRACE_FORMAT_SEARCH_PATH</c> — then the decode is retried once.
///
/// The handler is registered by the host (FindNeedleUX) at startup. ETWPlugin can't reference the host,
/// so the seam lives here — the same ambient-static pattern as <see cref="DecodeOptions"/> /
/// <see cref="DecodeScope"/>. When no handler is registered (CLI without the wiring, unit tests), the
/// decode just fails for missing symbols exactly as before.
/// </summary>
public static class WppSymbolProvisioning
{
    /// <summary>
    /// Host-supplied handler. Given the failing ETL and the message GUIDs that had no TMF, it should try
    /// to provision the symbols and return true if it made NEW symbols available (so the caller retries
    /// the decode). Null when no host is registered.
    /// </summary>
    public static Func<WppProvisionRequest, bool> Handler { get; set; }

    /// <summary>True when a host handler is registered — cheap gate before building a request.</summary>
    public static bool HasHandler => Handler != null;

    /// <summary>Invoke the registered handler, or return false (no-op) when none is set. Never throws —
    /// a provisioning failure must not break the decode; the caller falls back to "symbols missing".</summary>
    public static bool TryProvision(WppProvisionRequest request)
    {
        var handler = Handler;
        if (handler == null || request == null) return false;
        try { return handler(request); }
        catch { return false; }
    }
}

/// <summary>The input to a <see cref="WppSymbolProvisioning"/> handler: the ETL that failed to decode and
/// the distinct message GUIDs tracefmt couldn't format (each is a TMF filename the trace needs).</summary>
public sealed class WppProvisionRequest
{
    /// <summary>Full path to the .etl being decoded (its containing folder is a symbol source).</summary>
    public string EtlPath { get; init; }

    /// <summary>Distinct WPP message GUIDs with no TMF — informational (discovery is folder-based).</summary>
    public IReadOnlyCollection<string> MissingMessageGuids { get; init; } = Array.Empty<string>();
}
