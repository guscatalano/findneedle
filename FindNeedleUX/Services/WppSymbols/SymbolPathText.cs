using System;
using System.Collections.Generic;
using System.Linq;

namespace FindNeedleUX.Services.WppSymbols;

/// <summary>
/// Pure text helpers for the WPP symbol editors — converting the ';'-joined settings
/// (SymbolPath / SymbolSourcePath / TraceFormatSearchPath) to and from the page's per-line and
/// per-row editing views, de-duped and trimmed. Kept out of the page so they're unit-testable.
/// </summary>
internal static class SymbolPathText
{
    /// <summary>A ';'-joined setting → trimmed, non-empty elements (folders or path elements).</summary>
    public static List<string> Split(string setting) =>
        (setting ?? "")
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

    /// <summary>Elements → a ';'-joined setting (trimmed, empties dropped).</summary>
    public static string Join(IEnumerable<string> elements) =>
        string.Join(";", (elements ?? Enumerable.Empty<string>())
            .Select(s => (s ?? "").Trim()).Where(s => s.Length > 0));

    /// <summary>Setting (';'-joined) → one element per line, for a multiline TextBox.</summary>
    public static string ToLines(string setting) => string.Join("\n", Split(setting));

    /// <summary>Multiline editor text (\r/\n separated) → ';'-joined setting.</summary>
    public static string FromLines(string text) =>
        string.Join(";", (text ?? "")
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).Where(s => s.Length > 0));

    /// <summary>Add <paramref name="folder"/> to a ';'-joined list, de-duped (case-insensitive),
    /// preserving order. Returns the new ';'-joined value.</summary>
    public static string AppendFolder(string setting, string folder)
    {
        var list = Split(setting);
        folder = (folder ?? "").Trim();
        if (folder.Length > 0 && !list.Any(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase)))
            list.Add(folder);
        return Join(list);
    }
}
