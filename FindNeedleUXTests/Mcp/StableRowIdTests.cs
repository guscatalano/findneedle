using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicTextPlugin;
using FindNeedlePluginLib;
using FindNeedlePluginLib.Interfaces;
using findneedle.Implementations;
using FindNeedleUX;
using FindNeedleUX.Services.PagedLogSource;
using FindPluginCore.PluginSubsystem;
using FindPluginCore.Searching;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Mcp;

/// <summary>
/// Verifies the stable row id the MCP server hands agents: <see cref="LogLine.RowId"/> resolves back
/// to the same row via <see cref="IPagedLogSource.GetByRowId"/>, independent of sort, for both the
/// SQLite-backed source (RowId = SQLite Id) and the in-memory source (RowId = load-order position).
/// </summary>
[TestClass]
[TestCategory("Mcp")]
[DoNotParallelize]
public class StableRowIdTests
{
    private string _dir;
    private string _logPath;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"FN_rowid_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _logPath = Path.Combine(_dir, "rows.log");
        var lines = new List<string>();
        for (int i = 0; i < 20; i++)
            lines.Add($"[2024-03-{(i + 1):D2} 10:00:00] INFO: line {i:D2}");
        File.WriteAllLines(_logPath, lines);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    [TestMethod]
    public void Sqlite_RowId_RoundTripsAndIsStableAcrossSort()
    {
        var loc = new FolderLocation { path = _logPath };
        loc.SetExtensionProcessorList(new List<IFileExtensionProcessor> { new PlainTextProcessor() });
        var query = new NuSearchQuery { OverrideStorageType = StorageType.SqlLite };
        query.Locations.Add(loc);
        query.RunThrough();
        try
        {
            using var source = PagedLogSourceFactory.Create(query.ResultStorage, fallbackInMemory: null);

            var asc = source.GetPage(FilterSpec.Empty, new SortSpec("Time", Descending: false), 0, 100);
            Assert.IsTrue(asc.Count >= 20, "all rows present");

            var sample = asc[5];
            Assert.IsTrue(sample.RowId >= 0, "SQLite-backed rows carry a real stable id");

            // round-trip: fetch by id returns the same row regardless of paging context.
            var fetched = source.GetByRowId(sample.RowId);
            Assert.IsNotNull(fetched, "GetByRowId finds the row");
            Assert.AreEqual(sample.Message, fetched.Message, "same row content");
            Assert.AreEqual(sample.RowId, fetched.RowId);

            // stability: the same logical row keeps its id under a different sort.
            var desc = source.GetPage(FilterSpec.Empty, new SortSpec("Time", Descending: true), 0, 100);
            var same = desc.First(r => r.Message == sample.Message);
            Assert.AreEqual(sample.RowId, same.RowId, "RowId is independent of sort order");

            Assert.IsNull(source.GetByRowId(long.MaxValue), "unknown id returns null");
        }
        finally { query.DisposeStorage(); }
    }

    [TestMethod]
    public void InMemory_RowId_FallsBackToLoadOrderAndRoundTrips()
    {
        var rows = Enumerable.Range(0, 5)
            .Select(i => (ISearchResult)new FakeResult($"line {i}"))
            .ToList();
        using var source = new InMemoryPagedSource(rows.Select((r, i) => new LogLine(r, i)).ToList());

        var page = source.GetPage(FilterSpec.Empty, SortSpec.None, 0, 100);
        Assert.AreEqual(3, page[3].RowId, "in-memory RowId falls back to the load-order index");

        var fetched = source.GetByRowId(3);
        Assert.IsNotNull(fetched);
        Assert.AreEqual("line 3", fetched.Message);
        Assert.IsNull(source.GetByRowId(999), "unknown id returns null");
    }
}
