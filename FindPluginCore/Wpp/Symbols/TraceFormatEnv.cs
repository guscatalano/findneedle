using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FindPluginCore.Wpp.Symbols;

/// <summary>
/// Sets the WDK trace-tool environment (<c>TRACE_FORMAT_SEARCH_PATH</c>, <c>_NT_SYMBOL_PATH</c>) the SAME way
/// for the GUI and the CLI, so a WPP ETL decodes identically in both. The GUI used to do this in its own
/// <c>TraceFormatConfig</c> and the CLI did nothing — so a trace could decode in the GUI (managed TMF cache
/// already on the path) but not the CLI. Both now call this.
///   • <c>TRACE_FORMAT_SEARCH_PATH</c> = configured TMF folder (if any) + the managed TMF cache + ambient.
///   • <c>_NT_SYMBOL_PATH</c>        = configured symbol path (PDB folders / servers) + ambient.
/// The child tracefmt/tracepdb processes inherit these; the managed decoder reads TRACE_FORMAT_SEARCH_PATH too.
/// </summary>
public static class TraceFormatEnv
{
    public const string TmfVar = "TRACE_FORMAT_SEARCH_PATH";
    public const string SymVar = "_NT_SYMBOL_PATH";

    /// <param name="tmfFolder">A folder of existing .tmf files to search first, or null/empty.</param>
    /// <param name="symbolPath">PDB folders / symbol servers (_NT_SYMBOL_PATH-style), or null/empty.</param>
    /// <param name="ambientTmf">The TRACE_FORMAT_SEARCH_PATH to preserve at the tail. Defaults to the current
    /// value; pass a value captured at launch if you re-apply on settings changes (so it doesn't accumulate).</param>
    /// <param name="ambientSym">Same, for _NT_SYMBOL_PATH.</param>
    public static void Apply(string tmfFolder, string symbolPath, string ambientTmf = null, string ambientSym = null)
    {
        ambientTmf ??= Environment.GetEnvironmentVariable(TmfVar) ?? string.Empty;
        ambientSym ??= Environment.GetEnvironmentVariable(SymVar) ?? string.Empty;

        // TRACE_FORMAT_SEARCH_PATH: configured TMF folder, then the managed TMF cache, then ambient.
        var tmfParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(tmfFolder)) tmfParts.Add(tmfFolder.Trim());
        try { if (Directory.Exists(WppSymbolResolver.TmfCacheDir)) tmfParts.Add(WppSymbolResolver.TmfCacheDir); } catch { }
        if (!string.IsNullOrEmpty(ambientTmf)) tmfParts.Add(ambientTmf);
        Environment.SetEnvironmentVariable(TmfVar,
            string.Join(";", tmfParts.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase)));

        // _NT_SYMBOL_PATH: configured symbol path (PDB folders / symbol servers), then ambient.
        var sym = (symbolPath ?? string.Empty).Trim();
        var symCombined = string.IsNullOrEmpty(sym) ? ambientSym
                        : string.IsNullOrEmpty(ambientSym) ? sym
                        : sym + ";" + ambientSym;
        Environment.SetEnvironmentVariable(SymVar, symCombined);
    }
}
