using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using FindPluginCore.Implementations.Storage;
using FindNeedleCoreUtils;
using FindNeedlePluginLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

/// <summary>
/// Search-latency measurements: load a large result set, then search for one particular line and
/// measure how long it takes — both via the LIKE fallback (no index) and via the FTS5 trigram index.
/// This is the latency that makes per-keystroke "live" search lag on big logs (and what the viewer's
/// adaptive "Enter-to-search" mode reacts to).
///
/// The big variants are [Performance] (opt-in: <c>--filter TestCategory=Performance</c>) because the
/// FTS index build dominates wall-clock at multi-million-row scale. The 250k variant is a fast guard
/// that still proves the find-one-line path and the LIKE→FTS speedup.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SearchLatencyBenchmark
{
    private const string Needle = "UNIQUE-NEEDLE-7F3A2B9C";
    private readonly List<string> _dbPaths = new();

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var p in _dbPaths)
            try { if (File.Exists(p)) File.Delete(p); } catch { }
    }

    private SqliteStorage NewSqlite()
    {
        var searchedFile = Path.Combine(Path.GetTempPath(), "latbench_" + Guid.NewGuid().ToString("N"));
        _dbPaths.Add(CachedStorage.GetCacheFilePath(searchedFile, ".db"));
        return new SqliteStorage(searchedFile);
    }

    /// <summary>N rows, exactly one of which contains <see cref="Needle"/>. Streamed (no big list).</summary>
    private static IEnumerable<ISearchResult> Rows(int n, int needleAt)
    {
        for (int i = 0; i < n; i++)
            yield return new R(i == needleAt
                ? $"[2026-01-01 00:00:00] INFO: {Needle} the one line we are looking for, row {i}"
                : $"[2026-01-01 00:00:00] INFO: ordinary application log line number {i} doing routine work");
    }

    private static int Count(SqliteStorage s, string term)
        => s.GetFilteredCount(new SqliteStorage.FilterInput { Search = term });

    [TestMethod]
    [TestCategory("Performance")]
    [Timeout(600_000)]
    public void Search_5M_FindOneLine() => RunScenario(5_000_000);

    [TestMethod]
    [TestCategory("Performance")]
    [Timeout(180_000)]
    public void Search_1M_FindOneLine() => RunScenario(1_000_000);

    /// <summary>Fast, always-on guard: proves find-one-line correctness + the LIKE→FTS speedup.</summary>
    [TestMethod]
    [Timeout(120_000)]
    public void Search_250k_FindOneLine() => RunScenario(250_000);

    private void RunScenario(int n)
    {
        int needleAt = n / 2;
        using var s = NewSqlite();

        var ingest = Stopwatch.StartNew();
        s.AddFilteredBatch(Rows(n, needleAt));
        ingest.Stop();

        // ---- Search the unique line via the LIKE fallback (index not built yet) ----
        var likeSw = Stopwatch.StartNew();
        int likeHits = Count(s, Needle);
        likeSw.Stop();
        Assert.AreEqual(1, likeHits, "LIKE search should find exactly the one needle line");

        // ---- Build the FTS index, then search again (FTS path) ----
        var buildSw = Stopwatch.StartNew();
        s.BuildSearchIndex();
        buildSw.Stop();
        Assert.IsTrue(s.IsSearchIndexBuilt, "index should be built");

        var ftsSw = Stopwatch.StartNew();
        int ftsHits = Count(s, Needle);
        ftsSw.Stop();
        Assert.AreEqual(1, ftsHits, "FTS search should find exactly the one needle line");

        // A common term (in every row) — the worst case for "count all matches".
        var ftsCommonSw = Stopwatch.StartNew();
        int common = Count(s, "INFO");
        ftsCommonSw.Stop();
        Assert.AreEqual(n, common, "every row contains 'INFO'");

        Console.WriteLine(
            $"[{n:N0} rows] ingest={ingest.ElapsedMilliseconds:N0}ms  " +
            $"index-build={buildSw.ElapsedMilliseconds:N0}ms  " +
            $"find-one(LIKE)={likeSw.ElapsedMilliseconds:N0}ms  " +
            $"find-one(FTS)={ftsSw.ElapsedMilliseconds:N0}ms  " +
            $"count-common(FTS)={ftsCommonSw.ElapsedMilliseconds:N0}ms");

        // The point of the index: a single-line lookup should be fast (sub-second), and faster than
        // the LIKE scan it replaces. (LIKE on a huge log is exactly what makes live search lag.)
        Assert.IsTrue(ftsSw.ElapsedMilliseconds < 1000,
            $"FTS find-one should be sub-second, was {ftsSw.ElapsedMilliseconds}ms");
        Assert.IsTrue(ftsSw.ElapsedMilliseconds <= likeSw.ElapsedMilliseconds,
            $"FTS ({ftsSw.ElapsedMilliseconds}ms) should not be slower than LIKE ({likeSw.ElapsedMilliseconds}ms)");
    }

    private sealed class R : ISearchResult
    {
        private readonly string _m;
        public R(string m) { _m = m; }
        public DateTime GetLogTime() => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public string GetMachineName() => "M";
        public void WriteToConsole() { }
        public Level GetLevel() => Level.Info;
        public string GetUsername() => "u";
        public string GetTaskName() => "t";
        public string GetOpCode() => "";
        public string GetSource() => "s";
        public string GetSearchableData() => _m;
        public string GetMessage() => _m;
        public string GetResultSource() => "rs";
    }
}
