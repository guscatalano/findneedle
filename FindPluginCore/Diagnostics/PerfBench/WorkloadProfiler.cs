#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FindNeedleCoreUtils;
using FindPluginCore.Implementations.Storage;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace FindPluginCore.Diagnostics.PerfBench;

/// <summary>
/// The "Profile workload" mode: answers <i>which code paths take the longest</i>, not how long things
/// take. It CPU-samples this very process via in-proc EventPipe while a representative load
/// (ingest → FTS index build → selective + worst-case search) runs under a single
/// <see cref="RunWorkload"/> entrypoint, then attributes samples to the innermost method.
///
/// Why it's a SEPARATE mode: continuous sampling perturbs wall-clock time, so profiling numbers must
/// never contaminate the timed benchmark. The output is a list of hot frames as a percentage of the
/// samples taken <b>on the workload thread</b> — background/idle threads (including the profiler's own
/// pipe-copy thread) are excluded by keying on the workload entrypoint frame, which is the trick that
/// makes the percentages reflect real work rather than idle waits.
/// </summary>
public static class WorkloadProfiler
{
    /// <summary>The .NET runtime's CPU sample-profiler provider (one stack sample per managed thread per tick).</summary>
    private const string SampleProfilerProvider = "Microsoft-DotNETCore-SampleProfiler";

    /// <summary>Substring identifying the workload entrypoint frame — the anchor for thread attribution.</summary>
    private const string EntrypointMarker = "WorkloadProfiler.RunWorkload";

    /// <summary>
    /// Profiles the standard load-and-search workload over <paramref name="rows"/> synthetic lines and
    /// returns the hottest <paramref name="topN"/> code paths (by on-CPU sample share). Best-effort:
    /// returns an empty list with an explanatory note if sampling or trace parsing is unavailable.
    /// </summary>
    public static (List<PerfBenchHotFrame> frames, string note) Profile(long rows, int topN = 12)
        => ProfileAction(() => RunWorkload(rows), EntrypointMarker, topN);

    /// <summary>
    /// General CPU-profile harness: run <paramref name="workload"/> under in-proc EventPipe sampling and
    /// return the hottest <paramref name="topN"/> code paths, counting only samples on threads whose stack
    /// contains <paramref name="threadMarker"/> — that filter includes the threads actually doing the work
    /// (e.g. a namespace/type substring the workload runs through) and excludes the profiler's own pipe-copy
    /// thread and idle/background threads. Best-effort: returns an empty list + explanatory note on failure.
    /// Reused for both the synthetic ingest workload and the ETL-decode workload.
    /// </summary>
    public static (List<PerfBenchHotFrame> frames, string note) ProfileAction(Action workload, string threadMarker, int topN = 12)
    {
        var nettrace = Path.Combine(Path.GetTempPath(), $"perfbench_profile_{Guid.NewGuid():N}.nettrace");
        try
        {
            Capture(workload, nettrace);
            var (frames, active, total) = Aggregate(nettrace, topN, threadMarker);
            var note = active > 0
                ? $"{active:N0} CPU samples on the work threads ({total:N0} across all threads). "
                + "Percentages are the share of processor time spent with that code on top of the stack."
                : "No workload samples were captured (the run may have been too short).";
            return (frames, note);
        }
        catch (Exception ex)
        {
            return (new List<PerfBenchHotFrame>(),
                    "Profiling was not available on this machine: " + ex.Message);
        }
        finally { try { if (File.Exists(nettrace)) File.Delete(nettrace); } catch { } }
    }

    // ---- capture: EventPipe self-session around the workload ----

    private static void Capture(Action workload, string nettrace)
    {
        var providers = new[]
        {
            new EventPipeProvider(SampleProfilerProvider, System.Diagnostics.Tracing.EventLevel.Informational),
        };
        var client = new DiagnosticsClient(Environment.ProcessId);
        using var session = client.StartEventPipeSession(providers, requestRundown: true, circularBufferMB: 256);

        // Drain the event stream on a background task while the workload runs on THIS thread.
        var copy = Task.Run(() =>
        {
            using var fs = File.Create(nettrace);
            session.EventStream.CopyTo(fs);
        });

        workload();

        session.Stop();
        copy.Wait();
    }

    /// <summary>
    /// The profiled load: ingest N synthetic lines into SQLite, build the trigram FTS index, then run a
    /// selective and a worst-case (matches-everything) search. Deliberately single-threaded so every
    /// sample lands on one identifiable thread. This method's frame is the attribution anchor — keep its
    /// name in sync with <see cref="EntrypointMarker"/>.
    /// </summary>
    public static void RunWorkload(long rows)
    {
        var dbBase = Path.Combine(Path.GetTempPath(), "perfbench_profile_" + Guid.NewGuid().ToString("N"));
        bool priorFts = SqliteStorage.DisableFtsForMeasurement;
        SqliteStorage.DisableFtsForMeasurement = false;
        try
        {
            using var s = new SqliteStorage(dbBase);
            s.AddFilteredBatch(PerfBenchRunner.Rows(rows));
            s.BuildSearchIndex();

            long rareCount = SyntheticLogGenerator.RareTokenCount(rows);
            string selective = SyntheticLogGenerator.RareTokenPrefix + (rareCount / 2);
            s.GetFilteredCount(new SqliteStorage.FilterInput { Search = selective });
            s.GetFilteredCount(new SqliteStorage.FilterInput { Search = SyntheticLogGenerator.CommonToken });
        }
        finally
        {
            SqliteStorage.DisableFtsForMeasurement = priorFts;
            TryDeleteDb(dbBase);
        }
    }

    // ---- aggregate: attribute samples to the innermost frame on the workload thread ----

    private static (List<PerfBenchHotFrame> frames, int active, int total) Aggregate(string nettrace, int topN, string threadMarker)
    {
        var etlx = TraceLog.CreateFromEventPipeDataFile(nettrace);
        try
        {
            using var log = TraceLog.OpenOrConvert(etlx);

            // Pass 1: which thread(s) ran the workload entrypoint? Pass 2: count innermost frames there.
            var workloadThreads = new HashSet<int>();
            var samples = new List<(int tid, string name, bool native)>();
            foreach (var ev in log.Events)
            {
                if (!ev.EventName.Contains("Sample")) continue;
                var cs = ev.CallStack();
                if (cs == null) continue;

                var ca = cs.CodeAddress;
                var method = ca?.Method;
                bool native = method == null;
                string name = FriendlyName(method?.FullMethodName, ca?.ModuleFile?.Name);
                samples.Add((ev.ThreadID, name, native));

                for (var f = cs; f != null; f = f.Caller)
                    if ((f.CodeAddress?.Method?.FullMethodName ?? "").Contains(threadMarker))
                    { workloadThreads.Add(ev.ThreadID); break; }
            }

            var active = samples.Where(s => workloadThreads.Contains(s.tid)).ToList();
            var counts = new Dictionary<string, (int n, bool native)>();
            foreach (var s in active)
            {
                var cur = counts.TryGetValue(s.name, out var v) ? v : (0, s.native);
                counts[s.name] = (cur.Item1 + 1, s.native);
            }

            var frames = counts
                .OrderByDescending(kv => kv.Value.n)
                .Take(topN)
                .Select(kv => new PerfBenchHotFrame
                {
                    Method = kv.Key,
                    Samples = kv.Value.n,
                    Percent = active.Count > 0 ? Math.Round(100.0 * kv.Value.n / active.Count, 1) : 0,
                    Kind = kv.Value.native ? "native" : "managed",
                })
                .ToList();
            return (frames, active.Count, samples.Count);
        }
        finally { try { if (File.Exists(etlx)) File.Delete(etlx); } catch { } }
    }

    /// <summary>
    /// A readable label for a frame: the last <c>Type.Method</c> of a managed name (params dropped), or
    /// <c>module (native)</c> for unsymbolized native code (typically the SQLite engine / OS I/O).
    /// </summary>
    private static string FriendlyName(string? fullMethod, string? module)
    {
        if (!string.IsNullOrEmpty(fullMethod))
        {
            var noParams = fullMethod;
            int paren = noParams.IndexOf('(');
            if (paren > 0) noParams = noParams.Substring(0, paren);
            var parts = noParams.Split('.');
            return parts.Length >= 2 ? parts[^2] + "." + parts[^1] : noParams;
        }
        if (!string.IsNullOrEmpty(module))
        {
            var m = module;
            if (m.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                m.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                m = m.Substring(0, m.Length - 4);
            return m + " (native)";
        }
        return "(unresolved)";
    }

    private static void TryDeleteDb(string dbBase)
    {
        try
        {
            var db = CachedStorage.GetCacheFilePath(dbBase, ".db");
            foreach (var f in new[] { db, db + "-wal", db + "-shm", db + "-journal" })
                try { if (File.Exists(f)) File.Delete(f); } catch { }
        }
        catch { }
    }
}
