using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using FindNeedlePluginLib;
using FindNeedlePluginLib.Interfaces;
using FindNeedleUX.Services.PagedLogSource;
using FindPluginCore.Implementations.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Performance;

/// <summary>
/// Measures how fast the result viewer's virtualized rows load while scrolling — i.e. the latency
/// of <see cref="IPagedLogSource.GetPage"/> over a large SQLite-backed result set, which is what
/// the DataGrid / DataTables pull as the user scrolls. Reports initial-load latency, sequential
/// scroll latency, and deep-jump (scroll-to-bottom) latency; deep offsets are the interesting case
/// because SQLite OFFSET scans and discards the skipped rows.
///
/// [TestCategory("Performance")] → local-only (excluded from the CI gate). Timing varies wildly by
/// disk/AV, so the asserts are deterministic correctness + a generous ceiling that only trips on a
/// catastrophic regression; the real numbers are in the console output (run with
/// `dotnet test --filter TestCategory=Performance -l "console;verbosity=detailed"`).
/// </summary>
[TestClass]
[TestCategory("Performance")]
[DoNotParallelize]
public class RowScrollingPerformanceTests
{
    private const int RowCount = 50_000;
    private const int PageSize = 100;          // a viewport-ish fetch window
    private const int PerPageCeilingMs = 5_000; // catastrophic-regression guard, not a real SLA

    public TestContext TestContext { get; set; } = null!;

    private string _dbPath = null!;
    private SqliteStorage _storage = null!;

    private sealed class Row : ISearchResult
    {
        private static readonly DateTime Base = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private readonly int _i;
        public Row(int i) => _i = i;
        public DateTime GetLogTime() => Base.AddSeconds(_i);   // strictly increasing → sortable
        public Level GetLevel() => (Level)(_i % 5);
        public string GetMessage() => $"row {_i:D6} lorem ipsum dolor sit amet";
        public string GetSearchableData() => GetMessage();
        public string GetMachineName() => "M1";
        public string GetUsername() => "u";
        public string GetTaskName() => "task";
        public string GetOpCode() => "op";
        public string GetSource() => "src";
        public string GetResultSource() => "rows.log";
        public void WriteToConsole() { }
    }

    [TestInitialize]
    public void Setup()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"FN_scroll_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "scroll");
        _storage = new SqliteStorage(_dbPath);

        var writeSw = Stopwatch.StartNew();
        const int chunk = 5000;
        for (int start = 0; start < RowCount; start += chunk)
        {
            var batch = new List<ISearchResult>(chunk);
            for (int i = start; i < Math.Min(start + chunk, RowCount); i++) batch.Add(new Row(i));
            _storage.AddFilteredBatch(batch, CancellationToken.None);
        }
        writeSw.Stop();
        TestContext.WriteLine($"Populated {RowCount:N0} rows in {writeSw.ElapsedMilliseconds:N0} ms");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { _storage?.Dispose(); } catch { }
        try
        {
            var dir = Path.GetDirectoryName(_dbPath);
            if (dir != null && Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch { }
    }

    private void Report(string label, List<double> ms)
    {
        ms.Sort();
        double P(double q) => ms[(int)Math.Min(ms.Count - 1, Math.Max(0, Math.Round(q * (ms.Count - 1))))];
        TestContext.WriteLine(
            $"{label,-22} n={ms.Count,4}  min={ms.First(),7:F1}  median={P(0.5),7:F1}  " +
            $"p95={P(0.95),7:F1}  max={ms.Last(),7:F1} ms");
    }

    [TestMethod]
    public void Scrolling_And_LoadSpeed_OverLargeResultSet()
    {
        using var source = new SqlitePagedSource(_storage, ownsStorage: false);
        var noFilter = FilterSpec.Empty;
        var byTime = new SortSpec("Time", Descending: false);

        // ----- initial load: total count + first page -----
        var sw = Stopwatch.StartNew();
        int total = source.GetFilteredCount(noFilter);
        var countMs = sw.Elapsed.TotalMilliseconds;
        Assert.AreEqual(RowCount, total, "count must reflect every row");

        sw.Restart();
        var firstPage = source.GetPage(noFilter, byTime, 0, PageSize);
        var firstPageMs = sw.Elapsed.TotalMilliseconds;
        Assert.AreEqual(PageSize, firstPage.Count);
        TestContext.WriteLine($"Initial load: count={countMs:F1} ms, first page={firstPageMs:F1} ms");

        // ----- sequential scroll from the top (sorted, as a user would) -----
        var seq = new List<double>();
        for (int offset = 0; offset + PageSize <= 5_000; offset += PageSize) // first ~50 pages
        {
            sw.Restart();
            var page = source.GetPage(noFilter, byTime, offset, PageSize);
            seq.Add(sw.Elapsed.TotalMilliseconds);
            Assert.AreEqual(PageSize, page.Count, $"sequential page at {offset} should be full");
            Assert.IsTrue(sw.ElapsedMilliseconds < PerPageCeilingMs, $"page at {offset} took {sw.ElapsedMilliseconds} ms");
        }
        Report("sequential scroll", seq);

        // ----- deep jumps (scroll-to-bottom / random seeks): the OFFSET-scan worst case -----
        var deepOffsets = new[] { total / 4, total / 2, (3 * total) / 4, total - PageSize }
            .Select(o => Math.Max(0, o)).ToArray();
        var deep = new List<double>();
        foreach (var offset in deepOffsets)
        {
            sw.Restart();
            var page = source.GetPage(noFilter, byTime, offset, PageSize);
            var ms = sw.Elapsed.TotalMilliseconds;
            deep.Add(ms);
            Assert.AreEqual(PageSize, page.Count, $"deep page at {offset} should be full");
            Assert.IsTrue(ms < PerPageCeilingMs, $"deep page at {offset} took {ms:F0} ms (> {PerPageCeilingMs} ms ceiling)");
            TestContext.WriteLine($"deep jump offset={offset,8:N0}  {ms,7:F1} ms");
        }
        Report("deep jumps", deep);
    }
}
