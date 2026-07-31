using System;
using System.Collections.Generic;
using System.IO;
using FindNeedlePluginLib;

namespace TmfStoreResolverPlugin;

/// <summary>
/// Reference <see cref="IWppTmfResolver"/> for the ETL-ONLY case — captures that ship no binaries, so the
/// binary-driven <c>ISymbolResolver</c> path never fires. Given a missing WPP message/trace GUID, it looks
/// for a matching <c>.tmf</c> in one or more TMF stores listed in <c>FINDNEEDLE_TMF_STORES</c> (a
/// ';'-separated list of roots — local, UNC/SMB, wherever). Stateless: like the SMB symbol resolver, the OS
/// owns the connection, so there's nothing to cache or dispose here.
///
/// For each root it probes two layouts (the two that TMF stores actually use in the wild), trying both the
/// canonical hyphenated GUID name and the 32-hex form:
///   • flat:       <c>&lt;root&gt;\&lt;guid&gt;.tmf</c>
///   • SSQP-style: <c>&lt;root&gt;\&lt;guid&gt;\&lt;guid&gt;.tmf</c>  (drops into a symbol-server-style tree)
///
/// Point <c>FINDNEEDLE_TMF_STORES</c> at your org's TMF share and WPP ETLs decode with no binaries and no
/// tracepdb/WDK. Fork <see cref="TryResolveTmf"/> for a REST/service lookup, a build-drop scheme, etc.
/// Implements <see cref="IPluginDescription"/> so the plugin subsystem discovers it.
/// </summary>
public sealed class TmfStoreResolver : IWppTmfResolver, IPluginDescription
{
    /// <summary>';'-separated TMF-store roots, e.g. <c>\\corp\tmf;\\build\drops\tmf</c>.</summary>
    public const string StoresEnv = "FINDNEEDLE_TMF_STORES";

    public string TryResolveTmf(WppTmfResolveRequest request)
    {
        if (request == null) return null;
        foreach (var root in Roots())
            foreach (var candidate in Candidates(root, request))
            {
                request.Log($"probing {candidate}");
                if (SafeExists(candidate))
                    return candidate;
            }
        return null; // not in any store → FindNeedle asks the next resolver, then reports "symbols missing"
    }

    /// <summary>Every path this resolver will try for one GUID, in probe order: flat then SSQP-style, each in
    /// hyphenated then 32-hex naming.</summary>
    private static IEnumerable<string> Candidates(string root, WppTmfResolveRequest r)
    {
        foreach (var name in new[] { r.GuidD, r.GuidN })
        {
            yield return Path.Combine(root, name + ".tmf");        // flat
            yield return Path.Combine(root, name, name + ".tmf");  // SSQP-style subfolder
        }
    }

    private static IEnumerable<string> Roots()
    {
        var v = Environment.GetEnvironmentVariable(StoresEnv);
        if (string.IsNullOrWhiteSpace(v)) yield break;
        foreach (var r in v.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return r;
    }

    private static bool SafeExists(string path)
    {
        try { return File.Exists(path); } catch { return false; } // an unreachable store must never throw
    }

    public string GetPluginTextDescription()
        => "Locates WPP .tmf files by trace GUID in the TMF stores listed in FINDNEEDLE_TMF_STORES, so ETL-only "
         + "captures (no binaries) decode without a PDB or tracepdb.";
    public string GetPluginFriendlyName() => "TMF Store Resolver";
    public string GetPluginClassName() => IPluginDescription.GetPluginClassNameBase(this);
}
