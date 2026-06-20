using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;

namespace findneedle.Implementations.Locations.EventLogQueryLocation;

/// <summary>
/// Cheap metadata peek for an .evtx file, used by auto-rule matching. Reads up to a capped number of
/// records and collects the distinct provider names — enough to match a provider condition without
/// paying for a full load of a large event log. Never throws (returns what it managed to read).
/// </summary>
public static class EvtxMetaExtractor
{
    public static HashSet<string> QuickScanProviders(string evtxPath, int maxRecords = 20000)
    {
        var providers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (string.IsNullOrEmpty(evtxPath) || !File.Exists(evtxPath)) return providers;
            var query = new EventLogQuery(evtxPath, PathType.FilePath);
            using var reader = new EventLogReader(query);
            int n = 0;
            for (EventRecord rec = reader.ReadEvent(); rec != null && n < maxRecords; rec = reader.ReadEvent())
            {
                using (rec)
                {
                    if (!string.IsNullOrEmpty(rec.ProviderName)) providers.Add(rec.ProviderName);
                }
                n++;
            }
        }
        catch { /* best-effort — partial provider list is fine for matching */ }
        return providers;
    }
}
