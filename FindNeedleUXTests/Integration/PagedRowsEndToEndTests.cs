using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicTextPlugin;
using FindNeedlePluginLib;
using FindNeedlePluginLib.Interfaces;
using findneedle.Implementations;
using FindNeedleUX.Services.PagedLogSource;
using FindPluginCore.PluginSubsystem;
using FindPluginCore.Searching;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Integration;

/// <summary>
/// Exercises the virtualized-row layer the result viewer actually reads through —
/// <see cref="IPagedLogSource"/> over the search's SQLite storage (built exactly like the viewer
/// does via <see cref="PagedLogSourceFactory.Create"/>). This is the "rows" path my plain
/// open-log E2E test skipped: page windows (offset/limit), the partial last page, past-the-end,
/// no cross-page overlap, total/filtered counts, sort, and level filtering — i.e. the bits that
/// make virtualized scrolling tricky. Uses the real PlainTextProcessor + a real .log + a full
/// NuSearchQuery (forced to SQLite, the backend that drives SqlitePagedSource).
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class PagedRowsEndToEndTests
{
    private const int Total = 25;
    private const int ErrorCount = 7;   // i in [0,7)
    private const int WarningCount = 5; // i in [7,12)
    private const int InfoCount = 13;   // i in [12,25)  — distinct counts so the level is unambiguous

    private string _dir = null!;
    private string _logPath = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"FN_rows_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _logPath = Path.Combine(_dir, "rows.log");

        var lines = new List<string>(Total);
        for (int i = 0; i < Total; i++)
        {
            var lvl = i < ErrorCount ? "ERROR" : i < ErrorCount + WarningCount ? "WARNING" : "INFO";
            // Strictly increasing timestamps (day i+1, 01..25) so sort-by-time is verifiable.
            lines.Add($"[2024-02-{(i + 1):D2} 10:00:00] {lvl}: line {i:D2}");
        }
        File.WriteAllLines(_logPath, lines);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static NuSearchQuery RunSqliteSearch(string path)
    {
        var loc = new FolderLocation { path = path };
        loc.SetExtensionProcessorList(new List<IFileExtensionProcessor> { new PlainTextProcessor() });
        var query = new NuSearchQuery { OverrideStorageType = StorageType.SqlLite };
        query.Locations.Add(loc);
        query.RunThrough();
        return query;
    }

    [TestMethod]
    public void PagedSource_OverSqlite_VirtualizedRowAccessIsCorrect()
    {
        var query = RunSqliteSearch(_logPath);
        try
        {
            var storage = query.ResultStorage;
            Assert.IsNotNull(storage, "search should have created SQLite result storage");

            using var source = PagedLogSourceFactory.Create(storage!, fallbackInMemory: null);

            // ----- counts -----
            Assert.AreEqual(Total, source.GetFilteredCount(FilterSpec.Empty), "all rows visible with no filter");

            // ----- page windows -----
            var page0 = source.GetPage(FilterSpec.Empty, SortSpec.None, offset: 0, limit: 10);
            Assert.AreEqual(10, page0.Count);
            // Index is the stable 1-based SQLite Id (load order), not the per-page position — so
            // "sort by Index" actually reorders. Page 0 carries Ids 1..10, page 1 carries 11..20.
            CollectionAssert.AreEqual(Enumerable.Range(1, 10).ToArray(), page0.Select(r => r.Index).ToArray(),
                "first page rows carry the stable 1-based Id (1..10)");

            var page1 = source.GetPage(FilterSpec.Empty, SortSpec.None, offset: 10, limit: 10);
            Assert.AreEqual(10, page1.Count);
            CollectionAssert.AreEqual(Enumerable.Range(11, 10).ToArray(), page1.Select(r => r.Index).ToArray());

            // partial last page (20..24 → 5 rows)
            var lastPage = source.GetPage(FilterSpec.Empty, SortSpec.None, offset: 20, limit: 10);
            Assert.AreEqual(5, lastPage.Count, "the last page is partial");

            // past the end → empty, no throw
            Assert.AreEqual(0, source.GetPage(FilterSpec.Empty, SortSpec.None, offset: 1000, limit: 10).Count);

            // pages don't overlap and together cover distinct rows
            var firstTwenty = page0.Concat(page1).Select(r => r.Message).ToList();
            Assert.AreEqual(20, firstTwenty.Distinct().Count(), "no row appears on two pages");

            // ----- sort -----
            var desc = source.GetPage(FilterSpec.Empty, new SortSpec("Time", Descending: true), 0, Total);
            Assert.AreEqual(Total, desc.Count);
            for (int i = 1; i < desc.Count; i++)
                Assert.IsTrue(desc[i - 1].LogTime >= desc[i].LogTime, "descending time sort is monotonic");
            Assert.IsTrue(desc[0].Message.Contains("line 24"), "newest row sorts first");

            // ----- level filter (derive the stored level string from the data, don't guess) -----
            var levelCounts = source.GetLevelCounts(FilterSpec.Empty);
            Assert.AreEqual(Total, levelCounts.Values.Sum(), "level counts cover every row");
            var errorLevel = levelCounts.First(kv => kv.Value == ErrorCount).Key;

            var errorFilter = new FilterSpec("", "", "", "", "", errorLevel, null, null);
            Assert.AreEqual(ErrorCount, source.GetFilteredCount(errorFilter), "filtered count matches the error rows");
            var errorPage = source.GetPage(errorFilter, SortSpec.None, 0, 100);
            Assert.AreEqual(ErrorCount, errorPage.Count);
            Assert.IsTrue(errorPage.All(r => r.Level == errorLevel), "every filtered row is the requested level");
        }
        finally
        {
            query.DisposeStorage(); // release the SQLite file lock
        }
    }
}
