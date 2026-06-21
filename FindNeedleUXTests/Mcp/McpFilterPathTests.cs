using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FindNeedleCoreUtils;
using FindNeedlePluginLib;
using FindNeedleUX;
using FindNeedleUX.Pages.NativeResultViewer;
using FindNeedleUX.Services.PagedLogSource;
using FindPluginCore.Implementations.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Mcp;

/// <summary>
/// Locks in the exact path the MCP <c>search</c>/<c>set_filter</c> tools drive: the controller calls
/// <see cref="NativeResultsPageViewModel.SetFiltersBulk"/> for the count and
/// <see cref="NativeResultsPageViewModel.GetRows"/> for the page (what <c>get_page</c> returns). Both
/// must reflect the same filter. Regression guard for "the filter doesn't apply via MCP".
/// </summary>
[TestClass]
[TestCategory("Mcp")]
[DoNotParallelize]
public class McpFilterPathTests
{
    private readonly List<string> _dbPaths = new();

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var p in _dbPaths)
            try { if (File.Exists(p)) File.Delete(p); } catch { }
    }

    private SqliteStorage NewSqliteWith(IEnumerable<ISearchResult> rows)
    {
        var searchedFile = Path.Combine(Path.GetTempPath(), "mcpfilter_" + Guid.NewGuid().ToString("N"));
        _dbPaths.Add(CachedStorage.GetCacheFilePath(searchedFile, ".db"));
        var s = new SqliteStorage(searchedFile);
        s.AddFilteredBatch(rows.ToList());
        return s;
    }

    // 30 rows: 10 contain "needle".
    private static List<R> NeedleRows()
    {
        var rows = new List<R>();
        for (int i = 0; i < 30; i++)
            rows.Add(new R(i % 3 == 0 ? $"uid{i} needle row {i}" : $"uid{i} plain row {i}"));
        return rows;
    }

    [TestMethod]
    public void SetFiltersBulk_ThenGetRows_InMemory_BothFiltered()
    {
        var rows = NeedleRows();
        var vm = new NativeResultsPageViewModel();
        vm.SetSourceForTests(new InMemoryPagedSource(rows.Select((r, i) => new LogLine(r, i)).ToList()));

        int count = vm.SetFiltersBulk("needle", null, null, null, null, null, null, null);

        Assert.AreEqual(10, count, "SetFiltersBulk count (what set_filter returns) must be filtered");
        Assert.AreEqual(10, vm.TotalFilteredCount);
        var page = vm.GetRows(0, 100); // what get_page returns
        Assert.AreEqual(10, page.Count, "get_page rows must be filtered to match");
        Assert.IsTrue(page.All(r => r.SearchableData.Contains("needle")));
    }

    [TestMethod]
    public void SetFiltersBulk_ThenGetRows_Sqlite_BothFiltered()
    {
        var rows = NeedleRows();
        using var sqlite = NewSqliteWith(rows);
        sqlite.BuildSearchIndex();
        var vm = new NativeResultsPageViewModel();
        vm.SetSourceForTests(PagedLogSourceFactory.Create(sqlite, fallbackInMemory: null));

        int count = vm.SetFiltersBulk("needle", null, null, null, null, null, null, null);

        Assert.AreEqual(10, count, "SQLite-backed filter count must narrow");
        var page = vm.GetRows(0, 100);
        Assert.AreEqual(10, page.Count);
        Assert.IsTrue(page.All(r => r.SearchableData.Contains("needle")));
    }

    [TestMethod]
    public void GetRows_HonorsSmallLimit()
    {
        var rows = NeedleRows();
        var vm = new NativeResultsPageViewModel();
        vm.SetSourceForTests(new InMemoryPagedSource(rows.Select((r, i) => new LogLine(r, i)).ToList()));

        // get_page with a small limit must return exactly that many rows (not the page size).
        Assert.AreEqual(5, vm.GetRows(0, 5).Count);
        Assert.AreEqual(12, vm.GetRows(0, 12).Count);
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
