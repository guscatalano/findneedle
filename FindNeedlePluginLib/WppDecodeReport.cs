using System;
using System.Collections.Generic;
using System.Linq;

namespace FindNeedlePluginLib;

/// <summary>
/// Ambient sink for WPP decode diagnostics so a host — chiefly the CLI's decode summary — can report the
/// REAL missing-TMF GUIDs and unresolved-event count aggregated across every file it decoded.
///
/// Why this exists: the CLI's "Distinct missing message GUIDs seen" used to come only from the symbol-
/// provisioning seam, which fires ONLY on an all-unknown fail-fast. A PARTIAL decode (some TMFs present,
/// many events unresolved) never triggers it, so the summary reported "0 missing GUIDs" while clearly
/// leaving events unformatted. The ETL processor now also reports what it actually found here.
///
/// Usage: the host calls <see cref="Reset"/> before a run; the ETL processor calls <see cref="AddMissingGuids"/>
/// / <see cref="AddUnresolvedEvents"/> as it decodes; the host reads <see cref="Snapshot"/> afterwards.
/// Process-global + locked (a run may decode files on several threads).
/// </summary>
public static class WppDecodeReport
{
    private static readonly object _lock = new();
    private static readonly HashSet<string> _missing = new(StringComparer.OrdinalIgnoreCase);
    private static long _unresolvedEvents;

    public static void Reset() { lock (_lock) { _missing.Clear(); _unresolvedEvents = 0; } }

    public static void AddMissingGuids(IEnumerable<string> guids)
    {
        if (guids == null) return;
        lock (_lock) { foreach (var g in guids) if (!string.IsNullOrWhiteSpace(g)) _missing.Add(g.Trim()); }
    }

    public static void AddUnresolvedEvents(long n)
    {
        if (n <= 0) return;
        lock (_lock) { _unresolvedEvents += n; }
    }

    /// <summary>Distinct missing-TMF GUIDs and total unresolved (unformatted) events across the run.</summary>
    public static (string[] missingGuids, long unresolvedEvents) Snapshot()
    {
        lock (_lock) { return (_missing.ToArray(), _unresolvedEvents); }
    }
}
