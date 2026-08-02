using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FindPluginCore.Wpp.Symbols;

namespace FindNeedleUX.Services;

/// <summary>
/// Exports the user's WPP symbol settings into the environment variables the WDK trace tools read,
/// so WPP ETLs decode in a normal run without the user setting env vars by hand:
///   • <c>TRACE_FORMAT_SEARCH_PATH</c> = the TMF folder setting + the managed TMF cache (built from
///     symbols) + whatever was ambient at launch — tracefmt searches these for .tmf files.
///   • <c>_NT_SYMBOL_PATH</c> = the symbol-path setting (PDB folders / symbol servers) + ambient.
/// Call <see cref="Apply"/> at startup and on settings change. The child tracefmt/tracepdb processes
/// inherit these.
/// </summary>
public static class TraceFormatConfig
{
    private const string TmfVar = "TRACE_FORMAT_SEARCH_PATH";
    private const string SymVar = "_NT_SYMBOL_PATH";

    // Ambient values at launch, captured once so clearing a setting restores them rather than wiping.
    private static readonly string _origTmf = Environment.GetEnvironmentVariable(TmfVar) ?? "";
    private static readonly string _origSym = Environment.GetEnvironmentVariable(SymVar) ?? "";

    // Delegates to the shared FindPluginCore.Wpp.Symbols.TraceFormatEnv so the GUI and CLI set up the WDK
    // trace-tool environment identically (they used to diverge — the CLI didn't do this at all). Passes the
    // launch-time ambient values so re-applying on a settings change doesn't accumulate paths.
    public static void Apply()
        => FindPluginCore.Wpp.Symbols.TraceFormatEnv.Apply(
            ResultsViewerSettings.TraceFormatSearchPath, ResultsViewerSettings.SymbolPath, _origTmf, _origSym);
}
