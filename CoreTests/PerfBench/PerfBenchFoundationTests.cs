using System;
using System.IO;
using System.Linq;
using FindPluginCore.Diagnostics.PerfBench;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests.PerfBench;

/// <summary>
/// Foundation checks for the performance-benchmark artifact + data generator (build steps 1–2): the
/// result schema round-trips, and the synthetic log generator is byte-identical run-to-run with the
/// token distribution the runner depends on. Fast + deterministic — CI-runnable.
/// </summary>
[TestClass]
[TestCategory("PerfBench")]
public class PerfBenchFoundationTests
{
    // ---- schema (step 1) ----

    [TestMethod]
    public void Result_RoundTrips_AndIsCamelCase_NullsOmitted()
    {
        var r = new PerfBenchResult
        {
            RunId = "abc", TimestampUtc = "2026-07-25T22:00:00Z", Preset = "quick",
            Machine = { CpuModel = "Test CPU", LogicalCores = 8, RamGB = 16 },
            Scenarios =
            {
                new PerfBenchScenario
                {
                    Id = "engine.text.1M", Kind = "engine", Rows = 1_000_000,
                    Cold = new() { ["ingestMs"] = 8200, ["indexBuildMs"] = 3100 },
                    Ratios = { ["ftsVsScan"] = 22.8, ["usPerRow"] = 9.6 },
                    // StorageTierChosen / SkipReason left null → must not appear in JSON
                },
            },
            PrimaryMetrics = { "ftsVsScan", "parallelSpeedup" },
        };

        var json = r.ToJson();
        StringAssert.Contains(json, "\"benchmarkVersion\": 1", "camelCase property names");
        StringAssert.Contains(json, "\"ingestMs\": 8200");
        Assert.IsFalse(json.Contains("skipReason"), "null values must be omitted");
        Assert.IsFalse(json.Contains("storageTierChosen"), "null values must be omitted");

        var back = PerfBenchResult.FromJson(json);
        Assert.AreEqual(1, back.Scenarios.Count);
        Assert.AreEqual("engine.text.1M", back.Scenarios[0].Id);
        Assert.AreEqual(8200, back.Scenarios[0].Cold!["ingestMs"]);
        Assert.AreEqual(22.8, back.Scenarios[0].Ratios["ftsVsScan"]);
        Assert.AreEqual("Test CPU", back.Machine.CpuModel);
    }

    // ---- generator (step 2) ----

    [TestMethod]
    public void Generator_IsByteIdentical_AcrossRuns()
    {
        var a = TempFile();
        var b = TempFile();
        try
        {
            SyntheticLogGenerator.Write(a, 3000);
            SyntheticLogGenerator.Write(b, 3000);
            CollectionAssert.AreEqual(File.ReadAllBytes(a), File.ReadAllBytes(b),
                "same row count must produce byte-identical output");
        }
        finally { Del(a); Del(b); }
    }

    [TestMethod]
    public void Generator_RowCount_And_TokenDistribution()
    {
        var f = TempFile();
        try
        {
            const long rows = 150_000; // spans 3 rare-token plants (every 50k)
            SyntheticLogGenerator.Write(f, rows);
            var lines = File.ReadAllLines(f);

            Assert.AreEqual(rows, lines.Length, "one line per row");
            Assert.IsTrue(lines.All(l => l.Contains(SyntheticLogGenerator.CommonToken)),
                "common token on every line (worst-case query target)");

            long rare = lines.Count(l => l.Contains(SyntheticLogGenerator.RareTokenPrefix));
            Assert.AreEqual(SyntheticLogGenerator.RareTokenCount(rows), rare,
                "rare token cadence must match RareTokenCount (selective query target)");

            StringAssert.StartsWith(lines[0], "[2025-01-01 00:00:00] ", "bracketed fixed-epoch timestamp");
            Assert.IsTrue(lines.Any(l => l.Contains("] ERROR:")) && lines.Any(l => l.Contains("] WARN:")),
                "level distribution present (so level filtering is meaningful)");
        }
        finally { Del(f); }
    }

    [TestMethod]
    public void Generator_UsesLf_NoCrLf_NoBom()
    {
        var f = TempFile();
        try
        {
            SyntheticLogGenerator.Write(f, 10);
            var bytes = File.ReadAllBytes(f);
            Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "no UTF-8 BOM");
            Assert.IsFalse(bytes.Contains((byte)'\r'), "LF only — no CR (cross-platform determinism)");
        }
        finally { Del(f); }
    }

    // ---- runner (step 3) ----

    [TestMethod]
    public void Runner_ProducesPopulatedResult()
    {
        var r = PerfBenchRunner.Run(new long[] { 5000 }, repeats: 1, preset: "test", sampleLoad: false);

        Assert.AreEqual("test", r.Preset);
        Assert.AreEqual(1, r.Scenarios.Count);
        var sc = r.Scenarios[0];
        Assert.AreEqual("engine.text.5k", sc.Id);
        Assert.AreEqual(5000, sc.Rows);
        Assert.IsNotNull(sc.Cold);
        Assert.IsTrue(sc.Cold!.ContainsKey("ingestMs") && sc.Cold["ingestMs"] >= 0);
        Assert.IsTrue(sc.Cold.ContainsKey("indexBuildMs"));
        Assert.IsTrue(sc.Cold.ContainsKey("searchSelectiveMs"));
        Assert.IsTrue(sc.Ratios.ContainsKey("ftsVsScan"), "cross-machine ratio present");
        Assert.IsTrue(sc.Ratios.ContainsKey("usPerRow"));
        Assert.IsTrue(r.Machine.LogicalCores > 0, "machine specs captured");

        // The whole thing serializes as the submission artifact.
        var json = r.ToJson();
        StringAssert.Contains(json, "engine.text.5k");
        Assert.IsNotNull(PerfBenchResult.FromJson(json));
    }

    // ---- report renderer (step 4) ----

    [TestMethod]
    public void Report_RendersSelfContainedHtml()
    {
        var r = PerfBenchRunner.Run(new long[] { 5000 }, repeats: 1, preset: "test", sampleLoad: false);
        var html = PerfBenchReport.RenderHtml(r);

        StringAssert.StartsWith(html, "<!doctype html>");
        StringAssert.Contains(html, "FindNeedle Performance Benchmark"); // <title>
        StringAssert.Contains(html, "5k-line log");                       // plain-labelled details row
        StringAssert.Contains(html, "Faster than no index");              // plain ratio tile
        StringAssert.Contains(html, "Find one specific line");            // plain search label
        // Self-contained: no external resources of any kind.
        Assert.IsFalse(html.Contains("<script"), "no scripts");
        Assert.IsFalse(html.Contains("<link"), "no external stylesheets");
        Assert.IsFalse(html.Contains("src="), "no external assets");
        Assert.IsFalse(html.Contains("href="), "no external links");
    }

    [TestMethod]
    public void Report_RendersStorageEngineComparison()
    {
        // A result carrying the three "storage" scenarios the runner emits at >=100k rows: the same log
        // loaded in-memory / hybrid / SQLite. The report must table them head-to-head so a reader can see
        // the load-speed / RAM / disk tradeoff that drives FindNeedle's automatic engine choice.
        var r = new PerfBenchResult { Preset = "test" };
        void Storage(string tier, double ingestMs, double memMB, double diskMB) =>
            r.Scenarios.Add(new PerfBenchScenario
            {
                Id = $"storage.{tier}", Kind = "storage", Rows = 250_000, StorageTierChosen = tier,
                Metrics = { ["ingestMs"] = ingestMs, ["memoryMB"] = memMB, ["diskMB"] = diskMB },
            });
        Storage("In-memory", 152, 28.4, 0);
        Storage("Hybrid", 157, 28.4, 0.1);
        Storage("SQLite", 1419, 0, 59.6);

        var html = PerfBenchReport.RenderHtml(r);

        StringAssert.Contains(html, "Storage engines compared");
        StringAssert.Contains(html, "250k-line");
        foreach (var tier in new[] { "In-memory", "Hybrid", "SQLite" })
            StringAssert.Contains(html, tier, $"{tier} row present in the comparison table");
        StringAssert.Contains(html, "59.6 MB", "SQLite disk figure rendered");
        StringAssert.Contains(html, "28.4 MB", "in-memory RAM figure rendered");
    }

    // ---- profiler (separate "where the time goes" mode) ----

    [TestMethod]
    public void Report_RendersHotMethodsSection()
    {
        // A profile-only result: no timing scenarios, just the hot code paths the profiler found.
        var r = new PerfBenchResult
        {
            Preset = "profile", ProfileRows = 1_000_000,
            ProfileNote = "4,000 CPU samples on the workload thread.",
            HotMethods =
            {
                new PerfBenchHotFrame { Method = "SqliteDataReader.NextResult", Percent = 64.0, Samples = 2560, Kind = "managed" },
                new PerfBenchHotFrame { Method = "e_sqlite3 (native)", Percent = 18.5, Samples = 740, Kind = "native" },
                new PerfBenchHotFrame { Method = "SqliteParameterCollection.Bind", Percent = 12.0, Samples = 480, Kind = "managed" },
            },
        };

        var html = PerfBenchReport.RenderHtml(r);

        StringAssert.Contains(html, "Where the time goes");
        StringAssert.Contains(html, "code-path profile");            // profile-mode intro
        StringAssert.Contains(html, "SqliteDataReader.NextResult");
        StringAssert.Contains(html, "64%");                          // top frame percent
        StringAssert.Contains(html, "native");                       // native tag rendered
        StringAssert.Contains(html, "4,000 CPU samples");            // profile note
        // The top frame's bar is full width; a smaller frame's is proportionally shorter.
        StringAssert.Contains(html, "width:100%");
        // Still self-contained.
        Assert.IsFalse(html.Contains("<script"), "no scripts");
        Assert.IsFalse(html.Contains("src="), "no external assets");
    }

    [TestMethod]
    [TestCategory("SkipCI")]
    [TestCategory("Performance")]
    [Timeout(180000)]
    public void Profiler_CapturesWorkloadHotMethods()
    {
        // Real in-proc EventPipe sampling over the SQLite load+search workload. Excluded from CI
        // (needs the diagnostics IPC endpoint + a few seconds of CPU); a smoke test that the profiler
        // attributes samples to at least one method and stays self-consistent.
        var r = PerfBenchRunner.RunProfile(300_000);

        Assert.AreEqual("profile", r.Preset);
        Assert.AreEqual(300_000, r.ProfileRows);
        Assert.IsTrue(r.HotMethods.Count > 0, "profiler must find at least one hot code path: " + r.ProfileNote);
        Assert.IsTrue(r.HotMethods.All(f => f.Percent >= 0 && f.Percent <= 100), "percentages in range");
        Assert.IsTrue(r.HotMethods.Sum(f => f.Percent) <= 100.5, "top-N shares can't exceed 100%");
        Assert.IsFalse(string.IsNullOrEmpty(r.ProfileNote));

        var html = PerfBenchReport.RenderHtml(r);
        StringAssert.Contains(html, "Where the time goes");
    }

    private static string TempFile() => Path.Combine(Path.GetTempPath(), $"perfbench_{Guid.NewGuid():N}.log");
    private static void Del(string p) { try { File.Delete(p); } catch { } }
}
