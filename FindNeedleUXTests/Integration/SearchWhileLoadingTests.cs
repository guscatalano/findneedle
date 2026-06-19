using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BasicTextPlugin;
using FindNeedlePluginLib;
using FindNeedleUX.Services.PagedLogSource;
using FindPluginCore.Implementations.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Integration;

/// <summary>
/// Regression for "search while the log is still progressively loading." A producer streams batches
/// into the SQLite store (as a streaming search does) while the test concurrently runs filtered
/// queries (count + page) against the same store via the streaming paged source — exactly what the
/// viewer does when you type a search mid-load. Must not deadlock, throw, or return corrupt results.
/// CI-runnable: synthetic rows, no UI.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class SearchWhileLoadingTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"FN_swl_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    [TestMethod]
    public void SearchDuringStreamingLoad_NoDeadlockOrCorruption()
    {
        var searchedFile = Path.Combine(_dir, "src.log");
        File.WriteAllText(searchedFile, "seed"); // just seeds the cache-db path
        using var storage = new SqliteStorage(searchedFile);
        var source = PagedLogSourceFactory.CreateStreaming(storage);

        const int total = 60_000;
        const int batchSize = 500;

        // Producer: stream rows into storage in batches, then signal completion (like a real search).
        var producer = Task.Run(() =>
        {
            var batch = new List<ISearchResult>(batchSize);
            for (int i = 1; i <= total; i++)
            {
                batch.Add(new PlainTextSearchResult
                {
                    LineNumber = i,
                    Text = $"[2024-01-15 09:00:00] event {i} error",
                    SourceFile = searchedFile,
                });
                if (batch.Count == batchSize) { storage.AddFilteredBatch(batch); batch = new List<ISearchResult>(batchSize); }
            }
            if (batch.Count > 0) storage.AddFilteredBatch(batch);
            source.MarkLoadingComplete();
        });

        // Reader: hammer the store with filtered searches while it's being written.
        var search = FilterSpec.Empty with { Search = "error" };
        int reads = 0;
        while (!producer.IsCompleted)
        {
            int count = source.GetFilteredCount(search);
            var page = source.GetPage(search, SortSpec.None, 0, 100);
            Assert.IsTrue(count >= 0 && count <= total, $"count {count} out of range mid-load");
            Assert.IsTrue(page.Count <= 100, "page over-sized");
            reads++;
        }

        producer.GetAwaiter().GetResult(); // surface any producer exception (e.g. concurrent-write crash)

        Assert.IsTrue(reads > 0, "should have searched during the load");
        Assert.AreEqual(total, source.GetFilteredCount(FilterSpec.Empty), "all rows present after load");
        Assert.AreEqual(total, source.GetFilteredCount(search), "every row matches the search term");
        Assert.AreEqual(0, source.GetFilteredCount(FilterSpec.Empty with { Search = "no_such_term_zzz" }),
            "a non-matching search returns 0");
    }
}
