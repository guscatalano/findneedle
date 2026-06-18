using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

    public static void Apply()
    {
        // TRACE_FORMAT_SEARCH_PATH: configured TMF folder, then the managed TMF cache, then ambient.
        var tmfParts = new List<string>();
        var tmfFolder = (ResultsViewerSettings.TraceFormatSearchPath ?? "").Trim();
        if (!string.IsNullOrEmpty(tmfFolder)) tmfParts.Add(tmfFolder);
        try { if (Directory.Exists(WppSymbolResolver.TmfCacheDir)) tmfParts.Add(WppSymbolResolver.TmfCacheDir); } catch { }
        if (!string.IsNullOrEmpty(_origTmf)) tmfParts.Add(_origTmf);
        Environment.SetEnvironmentVariable(TmfVar, string.Join(";", tmfParts.Distinct()));

        // _NT_SYMBOL_PATH: configured symbol path (PDB folders / symbol servers), then ambient.
        var sym = (ResultsViewerSettings.SymbolPath ?? "").Trim();
        var symCombined = string.IsNullOrEmpty(sym) ? _origSym
                        : string.IsNullOrEmpty(_origSym) ? sym
                        : sym + ";" + _origSym;
        Environment.SetEnvironmentVariable(SymVar, symCombined);
    }
}
