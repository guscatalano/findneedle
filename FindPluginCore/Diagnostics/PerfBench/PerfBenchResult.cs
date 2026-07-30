#nullable enable
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FindPluginCore.Diagnostics.PerfBench;

/// <summary>
/// The performance-benchmark result artifact (the "findneedle.perfbench/v1" contract). This is the
/// single source of truth: the runner fills it, the JSON is what users submit, and the HTML report is
/// rendered from it. See <c>docs/perf-benchmark-design.md</c>.
///
/// Design invariants: ratios are the cross-machine comparison; milliseconds are per-machine context;
/// <see cref="BenchmarkVersion"/> gates comparability (only compare within a version); and the artifact
/// contains ONLY hardware + timings — never log content, file paths, or usernames.
/// </summary>
public sealed class PerfBenchResult
{
    public string Schema { get; set; } = "findneedle.perfbench/v1";

    /// <summary>Frozen scenario-set version. Submissions are only comparable within one value.</summary>
    public int BenchmarkVersion { get; set; } = 1;

    /// <summary>Opaque run identifier (never PII).</summary>
    public string RunId { get; set; } = "";
    public string TimestampUtc { get; set; } = "";
    public double DurationOfRunSec { get; set; }

    public PerfBenchApp App { get; set; } = new();
    public PerfBenchMachine Machine { get; set; } = new();
    public PerfBenchConfig Config { get; set; } = new();
    public PerfBenchSystemLoad SystemLoad { get; set; } = new();

    public string Preset { get; set; } = "quick";
    public int Repeats { get; set; } = 3;

    public List<PerfBenchScenario> Scenarios { get; set; } = new();

    /// <summary>Ratio keys the aggregation charts by default.</summary>
    public List<string> PrimaryMetrics { get; set; } = new();

    /// <summary>Free-form run notes (e.g. "WPP decode skipped: tracefmt/WDK not found").</summary>
    public List<string> Notes { get; set; } = new();

    /// <summary>
    /// Optional CPU-profile result (the separate "Profile workload" mode). Populated only by
    /// <c>PerfBenchRunner.RunProfile</c> — sampling inflates timing, so it never rides along with a
    /// timed run. Each entry is a code path and the share of on-CPU samples it took while loading +
    /// searching <see cref="ProfileRows"/> lines.
    /// </summary>
    public List<PerfBenchHotFrame> HotMethods { get; set; } = new();

    /// <summary>Rows the profiled workload processed (0 when no profile was run).</summary>
    public long ProfileRows { get; set; }

    /// <summary>Human note about the profile (sample count, capture caveats). Null when no profile ran.</summary>
    public string? ProfileNote { get; set; }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);
    public static PerfBenchResult FromJson(string json) => JsonSerializer.Deserialize<PerfBenchResult>(json, Json)!;
}

/// <summary>App/build identity — no machine or user info.</summary>
public sealed class PerfBenchApp
{
    public string Version { get; set; } = "";
    public string GitSha { get; set; } = "";
    public string Configuration { get; set; } = "";
    public string Runtime { get; set; } = "";
    public string Arch { get; set; } = "";
}

/// <summary>Hardware only — deliberately NO hostname / username / paths.</summary>
public sealed class PerfBenchMachine
{
    public string CpuModel { get; set; } = "";
    public int LogicalCores { get; set; }
    public int PhysicalCores { get; set; }
    public double RamGB { get; set; }
    public string Os { get; set; } = "";
    public string DiskType { get; set; } = "Unknown";
    public bool OnBattery { get; set; }
}

/// <summary>The knobs that change results, recorded so runs are apples-to-apples.</summary>
public sealed class PerfBenchConfig
{
    public string StorageTier { get; set; } = "auto";
    public bool ParallelIngest { get; set; } = true;
    public bool FtsEnabled { get; set; } = true;
    public bool BackgroundIndex { get; set; } = true;
    public int PageSize { get; set; } = 5000;
}

/// <summary>Ambient load, so a busy machine's inflated milliseconds are interpretable.</summary>
public sealed class PerfBenchSystemLoad
{
    /// <summary>System-wide CPU % sampled over ~1–2 s before the run.</summary>
    public double IdleCpuPercentBefore { get; set; }
    /// <summary>Non-FindNeedle CPU % observed during the run (best-effort).</summary>
    public double PeakForeignCpuPercentDuring { get; set; }
    public double AvailableRamGB { get; set; }
    /// <summary>tracefmt/WDK available → the WPP decode scenario ran.</summary>
    public bool WdkPresent { get; set; }
}

/// <summary>
/// One scenario's result. Heterogeneous by <see cref="Kind"/> — engine scenarios fill
/// <see cref="Cold"/>/<see cref="Warm"/>; single-pass scenarios (parallel/scope/decode/viewer) fill
/// <see cref="Metrics"/>. All ms values live in these dictionaries so the schema stays one shape;
/// <see cref="Ratios"/> are the cross-machine numbers.
/// </summary>
public sealed class PerfBenchScenario
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";          // engine | decode | viewer
    public string Dataset { get; set; } = "";
    public int DatasetVersion { get; set; }
    public long Rows { get; set; }
    public string? StorageTierChosen { get; set; }

    public string Status { get; set; } = "ok";       // ok | skipped
    public string? SkipReason { get; set; }

    /// <summary>Engine cold pass (fresh cache), keyed e.g. ingestMs/indexBuildMs/searchSelectiveMs. Null otherwise.</summary>
    public Dictionary<string, double>? Cold { get; set; }
    /// <summary>Engine warm pass (reopen). Null otherwise.</summary>
    public Dictionary<string, double>? Warm { get; set; }
    /// <summary>Single-pass metrics (serialMs/parallelMs/fullMs/scopedMs/decodeMs/firstPageMs/...).</summary>
    public Dictionary<string, double> Metrics { get; set; } = new();
    /// <summary>Cross-machine ratios (ftsVsScan/parallelSpeedup/scopeSpeedup/usPerRow/keptFraction).</summary>
    public Dictionary<string, double> Ratios { get; set; } = new();
    /// <summary>Min/max of a metric across the N repeats (median-of-N spread).</summary>
    public Dictionary<string, PerfBenchMinMax>? Spread { get; set; }
}

public sealed class PerfBenchMinMax
{
    public double Min { get; set; }
    public double Max { get; set; }
}

/// <summary>
/// One hot code path from the CPU-sampling profile: a method (or native module) and the percentage of
/// on-CPU samples it was the innermost frame for, while the profiled workload ran. Purely code identity
/// — no log content.
/// </summary>
public sealed class PerfBenchHotFrame
{
    public string Method { get; set; } = "";
    public double Percent { get; set; }
    public int Samples { get; set; }
    /// <summary>"managed" or "native" — native frames are usually the SQLite engine / OS doing I/O.</summary>
    public string Kind { get; set; } = "managed";
    /// <summary>The frame's module/assembly (e.g. "Microsoft.Data.Sqlite", "e_sqlite3", "FindPluginCore").</summary>
    public string Module { get; set; } = "";
    /// <summary>Plain-language bucket from <see cref="Module"/>: SQLite / ETW decode / FindNeedle / .NET runtime / native / other.</summary>
    public string Category { get; set; } = "";
}
