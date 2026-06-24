using System;
using System.IO;
using System.Linq;

namespace FindNeedleUX.Services;

/// <summary>
/// The file extensions Find Needle registers to open from Explorer ("Open with → Find Needle").
///
/// IMPORTANT: this list is the single source of truth for the app, but the OS association itself is
/// declared in the packaged manifest. Keep this in sync with the
/// <c>windows.fileTypeAssociation</c> extension in <c>FindNeedleUX\Package.appxmanifest</c>.
/// </summary>
public static class FileAssociations
{
    /// <summary>Extensions (with leading dot) Find Needle advertises as openable.</summary>
    public static readonly string[] Extensions = { ".etl", ".evtx", ".log", ".txt", ".zip" };

    /// <summary>True when <paramref name="path"/> has one of the <see cref="Extensions"/> (case-insensitive).</summary>
    public static bool IsSupported(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var ext = Path.GetExtension(path);
        return Extensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
    }
}
