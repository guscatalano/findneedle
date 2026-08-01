using System;
using System.IO;
using System.Reflection.PortableExecutable;

namespace FindNeedleUX.Services.WppSymbols;

/// <summary>
/// The PDB a binary was linked against, read from the PE debug directory's CodeView (RSDS) record:
/// file name + GUID + age. This is the identity symbol stores key on (SSQP:
/// <c>&lt;name&gt;/&lt;GUID&gt;&lt;age&gt;/&lt;name&gt;</c>), so we can log exactly which PDB we
/// need before probing anywhere. Managed equivalent of DbgHelp's <c>SymSrvGetFileIndexInfo</c>.
/// </summary>
public sealed record PdbIdentity(string PdbFileName, Guid Guid, int Age)
{
    /// <summary>Store lookup key: 32 uppercase hex GUID digits + age in hex (symsrv convention).</summary>
    public string Key => Guid.ToString("N").ToUpperInvariant() + Age.ToString("X");

    /// <summary>All-lowercase variant, for case-sensitive SSQP servers (tried second over HTTP).</summary>
    public string KeyLower => Guid.ToString("N") + Age.ToString("x");

    public override string ToString() => $"{PdbFileName} {{{Guid}}} age {Age}";

    /// <summary>
    /// Read the identity from a PE binary (native or managed). Returns null with a reason when the
    /// file isn't a PE image or carries no CodeView debug entry (built without a PDB / stripped /
    /// resource-only).
    /// </summary>
    public static PdbIdentity TryReadFromBinary(string binaryPath, out string error)
    {
        error = null;
        try
        {
            using var fs = File.OpenRead(binaryPath);
            using var pe = new PEReader(fs);
            foreach (var entry in pe.ReadDebugDirectory())
            {
                if (entry.Type != DebugDirectoryEntryType.CodeView) continue;
                var cv = pe.ReadCodeViewDebugDirectoryData(entry);
                // cv.Path is often a full build-machine path — store keys use just the file name.
                var name = Path.GetFileName(cv.Path);
                if (string.IsNullOrEmpty(name)) name = cv.Path;
                return new PdbIdentity(name, cv.Guid, cv.Age);
            }
            error = "no CodeView debug entry (binary built without a PDB, or stripped)";
            return null;
        }
        catch (BadImageFormatException) { error = "not a PE image"; return null; }
        catch (Exception ex) { error = ex.Message; return null; }
    }
}
