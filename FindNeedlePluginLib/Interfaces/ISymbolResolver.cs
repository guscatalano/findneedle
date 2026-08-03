using System;

namespace FindNeedlePluginLib;

/// <summary>
/// Identity of the PDB a binary was linked against — passed to an <see cref="ISymbolResolver"/> so it can
/// locate the matching PDB. Read from the binary's PE CodeView (RSDS) record: file name + GUID + age.
/// </summary>
public sealed class SymbolLookupRequest
{
    public SymbolLookupRequest(string pdbFileName, Guid guid, int age, string binaryPath)
    {
        PdbFileName = pdbFileName;
        Guid = guid;
        Age = age;
        BinaryPath = binaryPath;
    }

    /// <summary>The PDB file name, e.g. <c>mydriver.pdb</c>.</summary>
    public string PdbFileName { get; }

    /// <summary>CodeView signature GUID.</summary>
    public Guid Guid { get; }

    /// <summary>PDB age.</summary>
    public int Age { get; }

    /// <summary>Path to the binary (.dll/.exe/.sys) being resolved.</summary>
    public string BinaryPath { get; }

    /// <summary>Symbol-store (SSQP / symstore) key: 32 uppercase hex GUID digits + age in hex — the folder
    /// a symbol-server layout uses (<c>&lt;pdbname&gt;\&lt;Key&gt;\&lt;pdbname&gt;</c>). Same convention
    /// FindNeedle's built-in resolver uses, so a share laid out for symsrv Just Works.</summary>
    public string Key => Guid.ToString("N").ToUpperInvariant() + Age.ToString("X");

    /// <summary>All-lowercase variant, for case-sensitive SSQP servers.</summary>
    public string KeyLower => Guid.ToString("N") + Age.ToString("x");

    /// <summary>Diagnostic sink — anything the resolver writes here lands in the resolution log FindNeedle
    /// shows (attributed to the resolver, in context under this PDB). Never null (a no-op when the host wires
    /// none), so a plugin can call it unconditionally to explain what it probed and why it passed.</summary>
    public Action<string> Log { get; init; } = _ => { };

    public override string ToString() => $"{PdbFileName} {{{Guid}}} age {Age}";
}

/// <summary>
/// Plugin seam for locating symbols. When FindNeedle's built-in local + symbol-path lookup fails to find
/// a binary's PDB (needed to extract WPP TMFs), each registered <c>ISymbolResolver</c> is asked, in turn,
/// to find it. Implement this — together with <see cref="IPluginDescription"/> — to search your own
/// source: an SMB share, a company symbol server, a REST service, etc. Use the identity in
/// <paramref name="request"/> (name, GUID, age, and the ready-made symbol-store <see cref="SymbolLookupRequest.Key"/>).
///
/// Return a local or UNC path to the matching PDB, or <c>null</c> to pass to the next resolver. FindNeedle
/// extracts the TMFs from the returned PDB itself, so a resolver never needs to know about tracefmt/TMF
/// internals — it only answers "given this identity, where is the PDB?".
///
/// This is a first-class I/O plugin kind (like <see cref="ISearchLocation"/> / <see cref="IFileExtensionProcessor"/>),
/// discovered the same way: list the plugin DLL in <c>PluginConfig.json</c> and it's enumerated via
/// <c>PluginManager</c>. It is consulted only on the build/extract path (never on the cheap offline
/// diagnostic banner), so a resolver may do network I/O.
/// </summary>
public interface ISymbolResolver
{
    /// <summary>Return a PDB path for <paramref name="request"/>, or null if this resolver can't find it.</summary>
    string TryResolvePdb(SymbolLookupRequest request);

    /// <summary>
    /// How long, in milliseconds, this resolver may run on a single call before the host abandons it as hung
    /// and discards its result. Return your own worst-case budget — a resolver that pulls a large PDB over a
    /// slow link or does symbol-server round trips can legitimately take minutes, and the host must not cut it
    /// off mid-fetch. The host honors the LARGER of this and its own configured backstop, so a resolver can ask
    /// for MORE time but can never shrink the host's floor. Default 0 = "no preference, use the host default".
    /// (A default interface method, so existing resolvers need not implement it.)
    /// </summary>
    int SuggestedTimeoutMs => 0;
}
