using System;
using System.Collections.Generic;
using System.IO;
using FindNeedlePluginLib;

namespace SmbSymbolResolverPlugin;

/// <summary>
/// Reference <see cref="ISymbolResolver"/>: locates PDBs on one or more symbol shares (SMB/UNC or local),
/// configured via the <c>FINDNEEDLE_SYMBOL_SHARES</c> environment variable (a ';'-separated list of share
/// roots). For each root it probes the standard symbol-server (symstore / SSQP) layout
/// <c>&lt;root&gt;\&lt;pdb&gt;\&lt;GUID+age&gt;\&lt;pdb&gt;</c>. Because that layout is keyed by the PDB's
/// GUID+age, a hit is guaranteed to be the exact PDB the binary needs — no stale-symbol risk.
///
/// Point <c>FINDNEEDLE_SYMBOL_SHARES</c> at your org's symbol store and WPP TMFs resolve automatically.
/// This is a template — fork <see cref="TryResolvePdb"/> for custom logic (a flat share with an identity
/// check, a REST/symbol-server lookup, credentials, a local cache copy, a build-drop naming scheme, …).
///
/// Auto-loaded via the registry seam: point <c>HKCU\Software\FindNeedle\Plugins</c> at this DLL (see
/// tools\symbol-resolver\install-symbol-resolver.ps1). Implements <see cref="IPluginDescription"/> so the
/// plugin subsystem discovers it.
/// </summary>
public sealed class SmbSymbolResolver : ISymbolResolver, IPluginDescription
{
    /// <summary>';'-separated symbol-share roots, e.g. <c>\\corp\symbols;\\build\drops\symbols</c>.</summary>
    public const string SharesEnv = "FINDNEEDLE_SYMBOL_SHARES";

    public string TryResolvePdb(SymbolLookupRequest request)
    {
        if (request == null || string.IsNullOrEmpty(request.PdbFileName)) return null;
        foreach (var root in Roots())
        {
            // symbol-server (symstore/SSQP) layout — keyed by GUID+age, so a match is always the right PDB.
            var ssqp = Path.Combine(root, request.PdbFileName, request.Key, request.PdbFileName);
            if (SafeExists(ssqp)) return ssqp;
        }
        return null; // nothing here → FindNeedle asks the next resolver, then reports "not found"
    }

    private static IEnumerable<string> Roots()
    {
        var v = Environment.GetEnvironmentVariable(SharesEnv);
        if (string.IsNullOrWhiteSpace(v)) yield break;
        foreach (var r in v.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return r;
    }

    private static bool SafeExists(string path)
    {
        try { return File.Exists(path); } catch { return false; } // an unreachable share must never throw
    }

    public string GetPluginTextDescription()
        => "Locates PDBs on symbol shares listed in FINDNEEDLE_SYMBOL_SHARES (symbol-server layout), so WPP "
         + "TMFs resolve automatically without hand-configuring a symbol path.";
    public string GetPluginFriendlyName() => "SMB Symbol Resolver";
    public string GetPluginClassName() => IPluginDescription.GetPluginClassNameBase(this);
}
