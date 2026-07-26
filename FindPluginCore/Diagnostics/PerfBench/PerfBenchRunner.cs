#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using FindNeedleCoreUtils;
using FindNeedlePluginLib;
using FindNeedlePluginLib.Interfaces;
using FindPluginCore.Implementations.Storage;

namespace FindPluginCore.Diagnostics.PerfBench;

/// <summary>
/// Runs the performance benchmark and produces a <see cref="PerfBenchResult"/>. v1 covers the
/// <b>engine</b> scenarios — direct SqliteStorage ingest, FTS index build, and selective vs worst-case
/// search — measured median-of-N with flags reset between runs. Ratios (ftsVsScan, µs/row) are the
/// cross-machine numbers; the ms live under <c>cold</c>. Viewer / decode / parallel / scope / tier
/// scenarios are follow-on (recorded in <c>Notes</c>). Mirrors CoreTests/SearchLatencyBenchmark's
/// proven measurement recipe.
/// </summary>
public static class PerfBenchRunner
{
    public static PerfBenchResult RunQuick(bool sampleLoad = true)
        => Run(new long[] { 100_000, 1_000_000 }, repeats: 3, preset: "quick", sampleLoad: sampleLoad);

    public static PerfBenchResult Run(long[] engineSizes, int repeats = 3, string preset = "custom", bool sampleLoad = true)
    {
        var startedUtc = DateTime.UtcNow;
        var runSw = Stopwatch.StartNew();

        var result = new PerfBenchResult
        {
            RunId = Guid.NewGuid().ToString("N").Substring(0, 12),
            TimestampUtc = startedUtc.ToString("o", CultureInfo.InvariantCulture),
            Preset = preset,
            Repeats = repeats,
            App = CollectApp(),
            Machine = CollectMachine(),
            SystemLoad = CollectLoad(sampleLoad),
            PrimaryMetrics = { "ftsVsScan", "usPerRow" },
        };
        result.Notes.Add("v1 engine + paging-latency metrics (ingest / index / search / first-page). Full "
                       + "viewer render, decode, parallel-ingest, time-scope and storage-tier are follow-on.");

        // Watch for OTHER processes stealing CPU during the run — a contended run inflates the ms,
        // and this is what makes that visible in the report (idleCpuPercentBefore only sees the start).
        var sampler = sampleLoad ? new ForeignCpuSampler() : null;

        // Own the process-global storage flags for the duration; restore after.
        bool priorDisableFts = SqliteStorage.DisableFtsForMeasurement;
        SqliteStorage.DisableFtsForMeasurement = false; // we WANT the FTS index built
        try
        {
            foreach (var size in engineSizes)
                result.Scenarios.Add(RunEngine(size, repeats));

            // Same log loaded three ways — the storage-tier tradeoff (RAM vs disk). Real runs only.
            if (engineSizes.Length > 0 && engineSizes.Max() >= 100_000)
                result.Scenarios.AddRange(RunStorageComparison(250_000, Math.Min(repeats, 2)));
        }
        finally
        {
            SqliteStorage.DisableFtsForMeasurement = priorDisableFts;
            if (sampler != null)
            {
                result.SystemLoad.PeakForeignCpuPercentDuring = Math.Round(sampler.PeakForeignPercent, 1);
                sampler.Dispose();
            }
        }

        runSw.Stop();
        result.DurationOfRunSec = Math.Round(runSw.Elapsed.TotalSeconds, 1);
        return result;
    }

    // ---- engine scenario ----

    private static PerfBenchScenario RunEngine(long size, int repeats)
    {
        var ingest = new List<double>();
        var index = new List<double>();
        var likeSel = new List<double>();
        var ftsSel = new List<double>();
        var worst = new List<double>();
        var fp500 = new List<double>();
        var fp1000 = new List<double>();
        var fp5000 = new List<double>();
        var jumpLast = new List<double>();

        // A specific rare token that exists exactly once → selective query hits 1 row.
        long rareCount = SyntheticLogGenerator.RareTokenCount(size);
        string selective = SyntheticLogGenerator.RareTokenPrefix + (rareCount / 2);

        for (int r = 0; r < repeats; r++)
        {
            var dbBase = Path.Combine(Path.GetTempPath(), "perfbench_" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var s = new SqliteStorage(dbBase))
                {
                    var sw = Stopwatch.StartNew();
                    s.AddFilteredBatch(Rows(size));
                    sw.Stop(); ingest.Add(sw.Elapsed.TotalMilliseconds);

                    sw.Restart();
                    s.GetFilteredCount(new SqliteStorage.FilterInput { Search = selective }); // LIKE (no index yet)
                    sw.Stop(); likeSel.Add(sw.Elapsed.TotalMilliseconds);

                    sw.Restart();
                    s.BuildSearchIndex();
                    sw.Stop(); index.Add(sw.Elapsed.TotalMilliseconds);

                    sw.Restart();
                    s.GetFilteredCount(new SqliteStorage.FilterInput { Search = selective }); // FTS
                    sw.Stop(); ftsSel.Add(sw.Elapsed.TotalMilliseconds);

                    sw.Restart();
                    s.GetFilteredCount(new SqliteStorage.FilterInput { Search = SyntheticLogGenerator.CommonToken }); // matches ~all
                    sw.Stop(); worst.Add(sw.Elapsed.TotalMilliseconds);

                    // Paging latency (viewer-responsiveness data side): first page at a few page sizes,
                    // plus jump-to-last (O(pageSize) via the flipped-sort trick). No filter, load order.
                    var empty = new SqliteStorage.FilterInput();
                    sw.Restart(); s.GetFilteredPage(empty, null, 0, 500); sw.Stop(); fp500.Add(sw.Elapsed.TotalMilliseconds);
                    sw.Restart(); s.GetFilteredPage(empty, null, 0, 1000); sw.Stop(); fp1000.Add(sw.Elapsed.TotalMilliseconds);
                    sw.Restart(); s.GetFilteredPage(empty, null, 0, 5000); sw.Stop(); fp5000.Add(sw.Elapsed.TotalMilliseconds);
                    sw.Restart(); s.GetLastFilteredPage(empty, null, 500); sw.Stop(); jumpLast.Add(sw.Elapsed.TotalMilliseconds);
                }
            }
            finally { TryDeleteDb(dbBase); }
        }

        double mIngest = Median(ingest), mIndex = Median(index),
               mLike = Median(likeSel), mFts = Median(ftsSel), mWorst = Median(worst);

        return new PerfBenchScenario
        {
            Id = $"engine.text.{ShortSize(size)}",
            Kind = "engine",
            Dataset = "synthetic-log",
            DatasetVersion = SyntheticLogGenerator.DatasetVersion,
            Rows = size,
            StorageTierChosen = "Sqlite",
            Cold = new()
            {
                ["ingestMs"] = Round(mIngest),
                ["indexBuildMs"] = Round(mIndex),
                ["searchSelectiveMs"] = Round(mFts),
                ["searchWorstMs"] = Round(mWorst),
                ["firstPage500Ms"] = Round(Median(fp500), 1),
                ["firstPage1000Ms"] = Round(Median(fp1000), 1),
                ["firstPage5000Ms"] = Round(Median(fp5000), 1),
                ["jumpToLastMs"] = Round(Median(jumpLast), 1),
            },
            Ratios = new()
            {
                // Cross-machine: how much the trigram index beats the LIKE scan on a selective query.
                ["ftsVsScan"] = mFts > 0 ? Round(mLike / mFts, 1) : 0,
                ["usPerRow"] = size > 0 ? Round(mIngest * 1000.0 / size, 2) : 0,
            },
            Spread = new()
            {
                ["ingestMs"] = new() { Min = Round(Min(ingest)), Max = Round(Max(ingest)) },
                ["indexBuildMs"] = new() { Min = Round(Min(index)), Max = Round(Max(index)) },
            },
        };
    }

    // Parallel-vs-serial ingest is a follow-on: the real fan-out win is in the source-file SCAN, not
    // the storage insert this direct-storage runner measures, so it would always read ~1x here. It
    // needs the full file->scan->storage pipeline to measure honestly (see the note in Run()).

    // ---- storage-tier comparison (same log, three engines) ----

    private static List<PerfBenchScenario> RunStorageComparison(long size, int repeats)
    {
        return new List<PerfBenchScenario>
        {
            CompareStorage("inmemory", "In-memory", size, repeats, _ => new InMemoryStorage()),
            CompareStorage("hybrid", "Hybrid", size, repeats, b => new HybridStorage(b)),
            CompareStorage("sqlite", "SQLite", size, repeats, b => new SqliteStorage(b)),
        };
    }

    private static PerfBenchScenario CompareStorage(string id, string label, long size, int repeats, Func<string, ISearchStorage> make)
    {
        var ingest = new List<double>();
        double memMB = 0, diskMB = 0;
        for (int r = 0; r < repeats; r++)
        {
            var dbBase = Path.Combine(Path.GetTempPath(), "perfbench_" + Guid.NewGuid().ToString("N"));
            try
            {
                using var s = make(dbBase);
                var sw = Stopwatch.StartNew();
                s.AddFilteredBatch(Rows(size));
                sw.Stop(); ingest.Add(sw.Elapsed.TotalMilliseconds);
                var (_, _, disk, mem) = s.GetStatistics();
                memMB = Math.Round(mem / 1e6, 1);
                diskMB = Math.Round(disk / 1e6, 1);
            }
            catch { /* an engine may refuse a size — leave its metrics at 0 */ }
            finally { TryDeleteDb(dbBase); }
        }
        return new PerfBenchScenario
        {
            Id = "storage." + id, Kind = "storage", Dataset = "synthetic-log",
            DatasetVersion = SyntheticLogGenerator.DatasetVersion, Rows = size, StorageTierChosen = label,
            Metrics = new() { ["ingestMs"] = Round(Median(ingest)), ["memoryMB"] = memMB, ["diskMB"] = diskMB },
        };
    }

    private static IEnumerable<ISearchResult> Rows(long n)
    {
        for (long i = 0; i < n; i++)
            yield return new Row(SyntheticLogGenerator.Message(i), SyntheticLogGenerator.Time(i));
    }

    // ---- machine / load ----

    /// <summary>Machine specs for a pre-run summary card (no measurement).</summary>
    public static PerfBenchMachine DescribeMachine() => CollectMachine();

    private static PerfBenchApp CollectApp()
    {
        var app = new PerfBenchApp { Runtime = RuntimeInformation.FrameworkDescription, Arch = RuntimeInformation.ProcessArchitecture.ToString() };
        try { app.Version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? ""; } catch { }
#if DEBUG
        app.Configuration = "Debug";
#else
        app.Configuration = "Release";
#endif
        return app;
    }

    private static PerfBenchMachine CollectMachine()
    {
        var m = new PerfBenchMachine { LogicalCores = Environment.ProcessorCount };
        try { m.Os = RuntimeInformation.OSDescription; } catch { }
        try { m.DiskType = "Unknown"; } catch { }
        try { var gc = GC.GetGCMemoryInfo(); m.RamGB = Math.Round(gc.TotalAvailableMemoryBytes / 1e9, 1); } catch { }
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            m.CpuModel = (k?.GetValue("ProcessorNameString") as string)?.Trim() ?? "Unknown";
        }
        catch { m.CpuModel = "Unknown"; }
        return m;
    }

    private static PerfBenchSystemLoad CollectLoad(bool sample)
    {
        var l = new PerfBenchSystemLoad { WdkPresent = DetectWdk() };
        try { var gc = GC.GetGCMemoryInfo(); l.AvailableRamGB = Math.Round(gc.TotalAvailableMemoryBytes / 1e9, 1); } catch { }
        if (sample) l.IdleCpuPercentBefore = SampleSystemCpuPercent();
        return l;
    }

    /// <summary>True when the WDK's <c>tracefmt.exe</c> is present (registry KitsRoot10 → bin\**\x64) —
    /// i.e. this machine could run the WPP decode scenario.</summary>
    private static bool DetectWdk()
    {
        try
        {
            var kits = Microsoft.Win32.Registry.LocalMachine
                .OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots")?
                .GetValue("KitsRoot10") as string;
            if (string.IsNullOrEmpty(kits)) return false;
            var bin = Path.Combine(kits, "bin");
            return Directory.Exists(bin)
                && Directory.EnumerateFiles(bin, "tracefmt.exe", SearchOption.AllDirectories)
                    .Any(p => p.Contains(@"\x64\", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    /// <summary>Best-effort system-wide CPU % over a ~1 s window, summing all processes' CPU time
    /// (no PerformanceCounter dependency). 0 on any failure.</summary>
    private static double SampleSystemCpuPercent()
    {
        try
        {
            double Busy()
            {
                double ms = 0;
                foreach (var p in Process.GetProcesses())
                {
                    try { ms += p.TotalProcessorTime.TotalMilliseconds; } catch { } finally { p.Dispose(); }
                }
                return ms;
            }
            double b0 = Busy();
            var sw = Stopwatch.StartNew();
            System.Threading.Thread.Sleep(1000);
            sw.Stop();
            double busyMs = Busy() - b0;
            double wallMs = sw.Elapsed.TotalMilliseconds * Environment.ProcessorCount;
            return wallMs > 0 ? Math.Round(Math.Clamp(busyMs / wallMs * 100.0, 0, 100), 1) : 0;
        }
        catch { return 0; }
    }

    // ---- helpers ----

    private static string ShortSize(long n) => n >= 1_000_000 ? $"{n / 1_000_000}M" : n >= 1000 ? $"{n / 1000}k" : n.ToString();
    private static double Round(double v, int d = 0) => Math.Round(v, d);
    private static double Median(List<double> xs)
    {
        if (xs.Count == 0) return 0;
        var s = xs.OrderBy(x => x).ToList();
        int m = s.Count / 2;
        return s.Count % 2 == 1 ? s[m] : (s[m - 1] + s[m]) / 2.0;
    }
    private static double Min(List<double> xs) => xs.Count == 0 ? 0 : xs.Min();
    private static double Max(List<double> xs) => xs.Count == 0 ? 0 : xs.Max();

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

    /// <summary>
    /// Background sampler tracking the peak <b>non-benchmark</b> CPU during a run. Every few seconds it
    /// reads the summed CPU time of all processes and of this one; the delta between them is CPU other
    /// apps used, as a % of (wall × cores). The peak of that flags a contended run (which inflates the ms).
    /// </summary>
    private sealed class ForeignCpuSampler : IDisposable
    {
        private readonly System.Threading.Timer _timer;
        private readonly int _cores = Math.Max(1, Environment.ProcessorCount);
        private readonly int _ownPid = Environment.ProcessId;
        private long _lastAllMs, _lastOwnMs, _lastTick;
        public double PeakForeignPercent { get; private set; }

        public ForeignCpuSampler()
        {
            (_lastAllMs, _lastOwnMs) = Snapshot();
            _lastTick = Stopwatch.GetTimestamp();
            _timer = new System.Threading.Timer(_ => Tick(), null, 3000, 3000);
        }

        private (long all, long own) Snapshot()
        {
            long all = 0, own = 0;
            foreach (var p in Process.GetProcesses())
            {
                try { var t = (long)p.TotalProcessorTime.TotalMilliseconds; all += t; if (p.Id == _ownPid) own = t; }
                catch { /* exited / access denied */ }
                finally { p.Dispose(); }
            }
            return (all, own);
        }

        private void Tick()
        {
            try
            {
                var (all, own) = Snapshot();
                long now = Stopwatch.GetTimestamp();
                double wallMs = (now - _lastTick) * 1000.0 / Stopwatch.Frequency;
                double foreignBusy = (all - _lastAllMs) - (own - _lastOwnMs);
                _lastAllMs = all; _lastOwnMs = own; _lastTick = now;
                if (wallMs > 0)
                {
                    double pct = Math.Clamp(foreignBusy / (wallMs * _cores) * 100.0, 0, 100);
                    if (pct > PeakForeignPercent) PeakForeignPercent = pct;
                }
            }
            catch { /* best-effort */ }
        }

        public void Dispose() => _timer.Dispose();
    }

    /// <summary>Synthetic row fed straight to storage — carries the generator's message + timestamp.</summary>
    private sealed class Row : ISearchResult
    {
        private readonly string _m;
        private readonly DateTime _t;
        public Row(string message, DateTime time) { _m = message; _t = time; }
        public DateTime GetLogTime() => _t;
        public string GetMachineName() => "bench";
        public void WriteToConsole() { }
        public Level GetLevel() => Level.Info;
        public string GetUsername() => "u";
        public string GetTaskName() => "t";
        public string GetOpCode() => "";
        public string GetSource() => "synthetic";
        public string GetSearchableData() => _m;
        public string GetMessage() => _m;
        public string GetResultSource() => "perfbench";
    }
}
