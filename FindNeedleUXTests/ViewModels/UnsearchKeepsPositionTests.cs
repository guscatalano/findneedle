using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FindNeedleCoreUtils;
using FindNeedlePluginLib;
using FindNeedleUX;
using FindNeedleUX.Pages.NativeResultViewer;
using FindNeedleUX.Services.PagedLogSource;
using FindPluginCore.Implementations.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Regression tests for "un-searching resets the viewer": open a large file, search for a word whose
/// match sits deep in the log (page 5), then clear the search to read the lines AROUND the match —
/// the viewer must land on the page containing that match, not reset to page 1. Drives the exact
/// path the search box uses (SetSearchTextDeferred + ApplyFiltersAsync).
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
public class UnsearchKeepsPositionTests
{
    private static NativeResultsPageViewModel NewVmWith(IList<LogLine> rows)
    {
        var vm = new NativeResultsPageViewModel();
        vm.SetSourceForTests(new InMemoryPagedSource(rows));
        return vm;
    }

    /// <summary>n plain rows; the rows at <paramref name="needleAt"/> contain the token "needle".</summary>
    private static List<LogLine> Rows(int n, params int[] needleAt)
    {
        var set = new HashSet<int>(needleAt);
        var list = new List<LogLine>(n);
        for (int i = 0; i < n; i++)
            list.Add(new LogLine(new R(set.Contains(i) ? $"needle row {i}" : $"plain row {i}"), i));
        return list;
    }

    // ----- the reported bug (in-memory backend) -----

    [TestMethod]
    public async Task ClearSearch_ReturnsToPageContainingTheMatch()
    {
        // 1000 rows, page size 100 (the default): row 450 lives on page 5.
        var vm = NewVmWith(Rows(1000, 450));

        Assert.IsTrue(vm.SetSearchTextDeferred("needle"));
        await vm.ApplyFiltersAsync(CancellationToken.None);
        Assert.AreEqual(1, vm.TotalFilteredCount, "search narrows to the single match");
        Assert.AreEqual(1, vm.CurrentPage);

        // "Unsearch" — clear the box the same way the UI does.
        Assert.IsTrue(vm.SetSearchTextDeferred(""));
        await vm.ApplyFiltersAsync(CancellationToken.None);

        Assert.AreEqual(1000, vm.TotalFilteredCount, "cleared search shows every row again");
        Assert.AreEqual(5, vm.CurrentPage,
            "clearing the search must land on the page that holds the match (row 450 → page 5), not reset to page 1");
        Assert.IsTrue(vm.Results.Any(r => r.RowId == 450),
            "the matched row must be visible so the user can read the lines around it");
    }

    [TestMethod]
    public async Task ClearSearch_AnchorsToSelectedMatch_WhenMultipleMatches()
    {
        // Matches on pages 2, 5 and 8; the user selected the one on page 8.
        var vm = NewVmWith(Rows(1000, 150, 450, 750));

        vm.SetSearchTextDeferred("needle");
        await vm.ApplyFiltersAsync(CancellationToken.None);
        Assert.AreEqual(3, vm.TotalFilteredCount);

        vm.SelectedRowId = 750; // what the grid selection pushes to the VM

        vm.SetSearchTextDeferred("");
        await vm.ApplyFiltersAsync(CancellationToken.None);

        Assert.AreEqual(8, vm.CurrentPage, "the selected match (row 750) is on page 8");
        Assert.IsTrue(vm.Results.Any(r => r.RowId == 750));
    }

    [TestMethod]
    public async Task ClearSearch_NoSelection_AnchorsToFirstVisibleMatch()
    {
        var vm = NewVmWith(Rows(1000, 150, 450, 750));

        vm.SetSearchTextDeferred("needle");
        await vm.ApplyFiltersAsync(CancellationToken.None);

        // Nothing selected: the top row of the filtered page (row 150, page 2) is the anchor.
        vm.SetSearchTextDeferred("");
        await vm.ApplyFiltersAsync(CancellationToken.None);

        Assert.AreEqual(2, vm.CurrentPage);
        Assert.IsTrue(vm.Results.Any(r => r.RowId == 150));
    }

    [TestMethod]
    public async Task ClearSearch_RespectsActiveSort_WhenLocatingTheAnchor()
    {
        var vm = NewVmWith(Rows(1000, 450));
        vm.ApplySort("Index", descending: true); // row 450 is now at ordinal 549 → page 6

        vm.SetSearchTextDeferred("needle");
        await vm.ApplyFiltersAsync(CancellationToken.None);
        Assert.AreEqual(1, vm.TotalFilteredCount);

        vm.SetSearchTextDeferred("");
        await vm.ApplyFiltersAsync(CancellationToken.None);

        Assert.AreEqual(6, vm.CurrentPage, "position must be computed under the active sort order");
        Assert.IsTrue(vm.Results.Any(r => r.RowId == 450));
    }

    [TestMethod]
    public async Task ClearSearch_AnchorHiddenByRemainingFilter_FallsBackToPageOne()
    {
        var vm = NewVmWith(Rows(1000, 450));

        // A column filter that the needle row does NOT pass stays active after the search clears.
        vm.SetFiltersBulk("needle", null, null, "plain", null, null, null, null);
        vm.SelectedRowId = 450;

        vm.SetSearchTextDeferred("");
        await vm.ApplyFiltersAsync(CancellationToken.None);

        Assert.AreEqual(1, vm.CurrentPage, "an anchor filtered out of the new view falls back to page 1");
    }

    [TestMethod]
    public async Task ApplyingASearch_StillResetsToPageOne()
    {
        var vm = NewVmWith(Rows(1000, 450));

        // Land on page 5 by searching and clearing (the flow above)…
        vm.SetSearchTextDeferred("needle");
        await vm.ApplyFiltersAsync(CancellationToken.None);
        vm.SetSearchTextDeferred("");
        await vm.ApplyFiltersAsync(CancellationToken.None);
        Assert.AreEqual(5, vm.CurrentPage);

        // …then a NEW search must start from page 1 of its results, as before.
        vm.SetSearchTextDeferred("plain");
        await vm.ApplyFiltersAsync(CancellationToken.None);
        Assert.AreEqual(1, vm.CurrentPage, "applying a search still shows its first page of matches");
    }

    // ----- same scenario through the SQLite backend (what a large file actually uses) -----

    private readonly List<string> _dbPaths = new();

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var p in _dbPaths)
            try { if (File.Exists(p)) File.Delete(p); } catch { }
    }

    private SqliteStorage NewSqliteWith(int n, params int[] needleAt)
    {
        var set = new HashSet<int>(needleAt);
        var searchedFile = Path.Combine(Path.GetTempPath(), "unsearch_" + Guid.NewGuid().ToString("N"));
        _dbPaths.Add(CachedStorage.GetCacheFilePath(searchedFile, ".db"));
        var s = new SqliteStorage(searchedFile);
        var rows = new List<ISearchResult>(n);
        for (int i = 0; i < n; i++)
            rows.Add(new R(set.Contains(i) ? $"needle row {i}" : $"plain row {i}"));
        s.AddFilteredBatch(rows);
        return s;
    }

    [TestMethod]
    public async Task ClearSearch_Sqlite_ReturnsToPageContainingTheMatch()
    {
        using var sqlite = NewSqliteWith(1000, 450);
        sqlite.BuildSearchIndex();
        var vm = new NativeResultsPageViewModel();
        vm.SetSourceForTests(PagedLogSourceFactory.Create(sqlite, fallbackInMemory: null));

        vm.SetSearchTextDeferred("needle");
        await vm.ApplyFiltersAsync(CancellationToken.None);
        Assert.AreEqual(1, vm.TotalFilteredCount);
        long matchRowId = vm.Results.Single().RowId; // SQLite assigns its own stable Id

        vm.SetSearchTextDeferred("");
        await vm.ApplyFiltersAsync(CancellationToken.None);

        Assert.AreEqual(1000, vm.TotalFilteredCount);
        Assert.AreEqual(5, vm.CurrentPage,
            "SQLite-backed viewer must also land on the page holding the match after un-searching");
        Assert.IsTrue(vm.Results.Any(r => r.RowId == matchRowId));
    }

    [TestMethod]
    public async Task ClearSearch_Sqlite_WithSort_LandsOnAnchorsSortedPage()
    {
        using var sqlite = NewSqliteWith(1000, 450);
        sqlite.BuildSearchIndex();
        var vm = new NativeResultsPageViewModel();
        vm.SetSourceForTests(PagedLogSourceFactory.Create(sqlite, fallbackInMemory: null));
        vm.ApplySort("Index", descending: true); // load order reversed → match at ordinal 549 → page 6

        vm.SetSearchTextDeferred("needle");
        await vm.ApplyFiltersAsync(CancellationToken.None);
        long matchRowId = vm.Results.Single().RowId;

        vm.SetSearchTextDeferred("");
        await vm.ApplyFiltersAsync(CancellationToken.None);

        Assert.AreEqual(6, vm.CurrentPage);
        Assert.IsTrue(vm.Results.Any(r => r.RowId == matchRowId));
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
