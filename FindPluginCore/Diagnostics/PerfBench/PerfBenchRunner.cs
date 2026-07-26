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
        result.Notes.Add("v1 engine scenarios only (ingest/index/search). Viewer, decode, parallel-ingest, "
                       + "time-scope and storage-tier scenarios are follow-on.");

        // Own the process-global storage flags for the duration; restore after.
        bool priorDisableFts = SqliteStorage.DisableFtsForMeasurement;
        SqliteStorage.DisableFtsForMeasurement = false; // we WANT the FTS index built
        try
        {
            foreach (var size in engineSizes)
                result.Scenarios.Add(RunEngine(size, repeats));
        }
        finally
        {
            SqliteStorage.DisableFtsForMeasurement = priorDisableFts;
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
        var l = new PerfBenchSystemLoad { WdkPresent = false }; // decode scenarios (WDK) are follow-on
        try { var gc = GC.GetGCMemoryInfo(); l.AvailableRamGB = Math.Round(gc.TotalAvailableMemoryBytes / 1e9, 1); } catch { }
        if (sample) l.IdleCpuPercentBefore = SampleSystemCpuPercent();
        return l;
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
