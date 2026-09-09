using System;
using System.IO;

namespace FindNeedleUX.Services;

/// <summary>Small, pure text helpers for the Home page rows ("2 minutes ago", "1.2M rows", the
/// ETW/Folder/EventLog source tag). Kept UI-free so they are unit-testable.</summary>
public static class HomeFormatting
{
    /// <summary>"just now", "5 minutes ago", "3 hours ago", "Yesterday", "Aug 1" / "Aug 1, 2024".</summary>
    public static string RelativeTime(DateTime when, DateTime now)
    {
        var delta = now - when;
        if (delta < TimeSpan.Zero) delta = TimeSpan.Zero;
        if (delta.TotalSeconds < 60) return "just now";
        if (delta.TotalMinutes < 60)
        {
            int m = (int)delta.TotalMinutes;
            return m == 1 ? "1 minute ago" : $"{m} minutes ago";
        }
        if (delta.TotalHours < 24 && when.Date == now.Date)
        {
            int h = (int)delta.TotalHours;
            return h == 1 ? "1 hour ago" : $"{h} hours ago";
        }
        if (when.Date == now.Date.AddDays(-1)) return "Yesterday";
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return when.Year == now.Year ? when.ToString("MMM d", inv) : when.ToString("MMM d, yyyy", inv);
    }

    /// <summary>"38k", "1.2M", "210k", "912" — the compact count used for "rows".</summary>
    public static string CompactCount(long n)
    {
        if (n < 0) n = 0;
        if (n < 1000) return n.ToString();
        if (n < 10_000) return (n / 1000.0).ToString("0.#") + "k";
        if (n < 1_000_000) return (n / 1000.0).ToString("0") + "k";
        if (n < 10_000_000) return (n / 1_000_000.0).ToString("0.#") + "M";
        return (n / 1_000_000.0).ToString("0") + "M";
    }

    /// <summary>The type tag for a loaded source, from its location class name and its name/path: a
    /// FolderLocation is tagged by its path (see <see cref="SourceTagForPath"/>); Kusto / ADO / GitHub /
    /// EventLog locations by their kind; anything else by its class name minus "Location".</summary>
    public static string SourceTagFor(string locationTypeName, string nameOrPath)
    {
        var t = locationTypeName ?? "";
        if (t.Length == 0 || t.Equals("FolderLocation", StringComparison.OrdinalIgnoreCase)) return SourceTagForPath(nameOrPath);
        if (t.Contains("Kusto", StringComparison.OrdinalIgnoreCase)) return "Kusto";
        if (t.Contains("Ado", StringComparison.Ordinal) || t.Contains("ADO", StringComparison.Ordinal)) return "ADO";
        if (t.Contains("Github", StringComparison.OrdinalIgnoreCase)) return "GitHub";
        if (t.Contains("EventLog", StringComparison.OrdinalIgnoreCase)) return "EventLog";
        var stripped = t.EndsWith("Location", StringComparison.OrdinalIgnoreCase) ? t[..^"Location".Length] : t;
        return stripped.Length == 0 ? "Source" : stripped;
    }

    /// <summary>The short type tag shown next to a source path: Folder / ETW / EventLog / Archive / Dump /
    /// CSV / JSON / Pcap / Log. Decided by the path alone (a directory or the file extension).</summary>
    public static string SourceTagForPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "Source";
        try { if (Directory.Exists(path)) return "Folder"; } catch { }
        var ext = "";
        try { ext = Path.GetExtension(path) ?? ""; } catch { }
        switch (ext.ToLowerInvariant())
        {
            case ".etl": return "ETW";
            case ".evtx": return "EventLog";
            case ".zip":
            case ".cab":
            case ".7z": return "Archive";
            case ".dmp": return "Dump";
            case ".csv":
            case ".tsv": return "CSV";
            case ".json": return "JSON";
            case ".pcap":
            case ".pcapng": return "Pcap";
            case "": return path.EndsWith("\\", StringComparison.Ordinal) || path.EndsWith("/", StringComparison.Ordinal) ? "Folder" : "Log";
            default: return "Log";
        }
    }
}
