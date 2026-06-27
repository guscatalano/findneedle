using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FindPluginCore.Diagnostics;

/// <summary>One completed timing phase: its name and how long it took.</summary>
public sealed class PerfPhase
{
    public string Name { get; set; } = "";
    public long ElapsedMs { get; set; }
}

/// <summary>
/// Structured "why did this search/load take so long" report for the most recent run. Built by
/// <see cref="PerfReport"/> from the same phase events that go to perf-log.txt, so it never
/// duplicates timing logic. The UI (Statistics page) renders <see cref="ToText"/>; the value is
/// also persisted to <c>last-search-report.json</c> so tooling/tests can read it.
/// </summary>
public sealed class SearchRunReport
{
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public string Source { get; set; } = "";

    /// <summary>Completed phases in the order they finished (from each phase's <c>.end</c> event).</summary>
    public List<PerfPhase> Phases { get; set; } = new();

    // ----- Storage decision -----
    public string StorageType { get; set; } = "";       // InMemoryStorage / SqliteStorage / HybridStorage
    public string StorageMode { get; set; } = "";       // auto / forced
    public long StorageEstimatedRows { get; set; } = -1; // estimate Auto was handed (-1 = unknown)

    // ----- Volumes / behaviour -----
    public long RawRows { get; set; }
    public long StoredRows { get; set; }
    public bool CacheHit { get; set; }            // reused a valid on-disk cache (skipped the scan)
    public bool CacheWritten { get; set; }        // moved results into a reusable cache this run
    public string CacheWriteSkipReason { get; set; } = ""; // why the cache wasn't written (disabled / not_sqlite / …)
    public bool ConsolidatedFullList { get; set; } // true = full result list materialized in RAM
    public bool FtsIndexBuilt { get; set; }        // SQLite FTS5 trigram index was built this run

    // ----- Derived timings (ms) -----
    public long SearchMs { get; set; }  // search.run
    public long ViewerMs { get; set; }  // viewer.*.load
    public long TotalMs => SearchMs + ViewerMs;

    public long PhaseMs(string name) =>
        Phases.Where(p => p.Name == name).Select(p => p.ElapsedMs).DefaultIfEmpty(0).Max();

    /// <summary>Human-readable "why" hints derived from the recorded data.</summary>
    public List<string> BuildHints()
    {
        var hints = new List<string>();
        if (CacheHit)
            hints.Add($"Reused the on-disk cache ({StoredRows:N0} rows) — the file read/parse/index was skipped, so this was fast.");
        else if (CacheWriteSkipReason == "disabled")
            hints.Add("Caching was disabled, so results weren't saved — the next open of this file will scan again.");
        else if (CacheWritten)
            hints.Add("This was a fresh scan; results were written to the cache, so the next open of this same file should be much faster.");

        if (StorageType == "InMemoryStorage" && StoredRows >= 50_000)
        {
            var why = StorageMode == "auto"
                ? $"Auto picked in-memory storage because the row estimate it saw was {StorageEstimatedRows:N0}"
                : "Storage was forced to in-memory";
            hints.Add($"{why}, so all {StoredRows:N0} rows were held in RAM. " +
                      "SQLite would page from disk and use far less memory.");
        }

        var fallback = PhaseMs("viewer.native.getloglines_fallback");
        if (fallback > 1000)
            hints.Add($"The viewer materialized every row into memory ({fallback:N0} ms) instead of paging " +
                      "from SQLite — this happens when the search used in-memory/hybrid storage.");

        if (ConsolidatedFullList && StoredRows >= 100_000)
            hints.Add($"The full {StoredRows:N0}-row result list was consolidated in RAM (a processor/output/rule " +
                      "was active). Without those, large searches stay lazy.");

        var settle = PhaseMs("search.settle");
        if (settle > 1000)
            hints.Add($"Hybrid storage settled {StoredRows:N0} rows to disk at the end of the search ({settle:N0} ms).");

        if (hints.Count == 0)
            hints.Add("Nothing stands out — time was spent reading, parsing and indexing the source.");
        return hints;
    }

    /// <summary>The biggest phases, largest first, for an at-a-glance breakdown.</summary>
    public IEnumerable<PerfPhase> TopPhases(int n = 8) =>
        Phases.OrderByDescending(p => p.ElapsedMs).Take(n);

    public string ToText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Search performance report  ·  {StartedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        if (!string.IsNullOrEmpty(Source)) sb.AppendLine($"Source:  {Source}");
        sb.AppendLine();
        sb.AppendLine($"Total time:     {TotalMs:N0} ms   (search {SearchMs:N0} ms + viewer {ViewerMs:N0} ms)");
        sb.AppendLine($"Storage:        {StorageType}  ({StorageMode}" +
                      (StorageEstimatedRows >= 0 ? $", estimate {StorageEstimatedRows:N0} rows" : "") + ")");
        sb.AppendLine($"Rows:           {StoredRows:N0} stored" + (RawRows > 0 ? $"  ({RawRows:N0} raw)" : ""));
        sb.AppendLine($"Cache:          {(CacheHit ? "HIT — reused on-disk cache, scan skipped"
                                       : CacheWritten ? "MISS — fresh scan; results written to cache for next time"
                                       : "not cached" + (string.IsNullOrEmpty(CacheWriteSkipReason) ? "" : $" ({CacheWriteSkipReason})"))}");
        sb.AppendLine($"Consolidated:   {(ConsolidatedFullList ? "yes — full list materialized in RAM" : "no — stayed lazy")}");
        sb.AppendLine();
        sb.AppendLine("Where the time went:");
        foreach (var p in TopPhases())
            sb.AppendLine($"   {p.ElapsedMs,8:N0} ms   {p.Name}");
        sb.AppendLine();
        sb.AppendLine("Why:");
        foreach (var h in BuildHints())
            sb.AppendLine($"   • {h}");
        return sb.ToString();
    }

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
}

/// <summary>
/// Builds and holds the <see cref="SearchRunReport"/> for the most recent run by ingesting the
/// phase events that flow through <see cref="PerfLog.Log"/>. A new run begins at
/// <c>search.run.start</c>; the report is live (continuously updated through the search and viewer
/// phases) and persisted to disk when search and viewer phases complete.
/// </summary>
public static class PerfReport
{
    private static readonly object _lock = new();
    private static SearchRunReport _current;

    /// <summary>The most recent run's report (may still be updating). Null until the first run.</summary>
    public static SearchRunReport Last { get; private set; }

    /// <summary>Raised whenever the current report is updated, so the UI can refresh.</summary>
    public static event Action Updated;

    public static string ReportFilePath { get; } = ComputePath();

    private static string ComputePath()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FindNeedle");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "last-search-report.json");
        }
        catch { return Path.Combine(Path.GetTempPath(), "findneedle-last-search-report.json"); }
    }

    /// <summary>Optional caller-supplied label for the source being searched.</summary>
    public static void SetSource(string source)
    {
        lock (_lock) { if (_current != null) _current.Source = source ?? ""; }
    }

    /// <summary>
    /// Fold one phase event into the current report. Called from <see cref="PerfLog.Log"/> for
    /// every event; must never throw (instrumentation can't break the measured path).
    /// </summary>
    internal static void Ingest(string phase, (string key, object value)[] kvs)
    {
        try
        {
            lock (_lock)
            {
                if (phase == "search.run.start")
                {
                    _current = new SearchRunReport();
                    Last = _current;
                }
                var r = _current;
                if (r == null) return;

                long L(string k) => kvs != null && kvs.Any(x => x.key == k) &&
                    long.TryParse(Convert.ToString(kvs.First(x => x.key == k).value, CultureInfo.InvariantCulture), out var v) ? v : 0;
                string S(string k) => kvs?.FirstOrDefault(x => x.key == k).value?.ToString() ?? "";

                // Completed phase with a duration.
                if (phase.EndsWith(".end", StringComparison.Ordinal) && kvs != null && kvs.Any(x => x.key == "elapsed_ms"))
                    r.Phases.Add(new PerfPhase { Name = phase[..^4], ElapsedMs = L("elapsed_ms") });

                switch (phase)
                {
                    case "storage.selected":
                        r.StorageType = S("type");
                        r.StorageMode = S("mode");
                        r.StorageEstimatedRows = kvs.Any(x => x.key == "est") ? L("est") : -1;
                        break;
                    case "cache.hit":
                        r.CacheHit = true;
                        r.StoredRows = Math.Max(r.StoredRows, L("rows"));
                        break;
                    case "cache.write.ok":
                        r.CacheWritten = true;
                        break;
                    case "cache.write.skip":
                        r.CacheWriteSkipReason = S("reason");
                        break;
                    case "storage.fts":
                        r.FtsIndexBuilt = S("built").Equals("true", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "location.end":
                        r.RawRows += L("rows_this_loc_raw");
                        r.StoredRows = Math.Max(r.StoredRows, L("rows_total"));
                        break;
                    case "consolidate.skipped":
                        r.ConsolidatedFullList = false;
                        r.StoredRows = Math.Max(r.StoredRows, L("known_rows"));
                        break;
                    case "consolidate.end":
                        r.ConsolidatedFullList = true;
                        break;
                    case "search.run.end":
                        r.SearchMs = L("elapsed_ms");
                        Persist(r);
                        break;
                    case "viewer.native.load.end":
                    case "viewer.web.load.end":
                        r.ViewerMs = L("elapsed_ms");
                        Persist(r);
                        break;
                }
                Updated?.Invoke();
            }
        }
        catch { /* never break the measured path */ }
    }

    private static void Persist(SearchRunReport r)
    {
        try { File.WriteAllText(ReportFilePath, r.ToJson()); } catch { /* best-effort */ }
    }

    /// <summary>The last run's report read from disk (<see cref="ReportFilePath"/>), or null. Use when
    /// <see cref="Last"/> is unavailable because the search ran in another process (e.g. a UI test
    /// driving the app reads what the app process persisted).</summary>
    public static SearchRunReport LoadPersisted()
    {
        try
        {
            if (!File.Exists(ReportFilePath)) return null;
            return JsonSerializer.Deserialize<SearchRunReport>(File.ReadAllText(ReportFilePath));
        }
        catch { return null; }
    }
}
