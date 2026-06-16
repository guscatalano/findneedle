using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FindNeedleCoreUtils;
using FindNeedlePluginLib;
using FindNeedleUX;
using FindNeedleUX.Services.PagedLogSource;
using FindPluginCore.Implementations.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Integration;

/// <summary>
/// Search-correctness matrix: the same query must return the SAME set of rows across the three real
/// search paths the viewer can use —
///   • in-memory matching (InMemoryPagedSource),
///   • SQLite without the FTS index (LIKE scan),
///   • SQLite with the FTS5 trigram index built.
/// Beyond "same count" (already covered by SearchIndexTests), this asserts the same *rows*, and pins
/// down the matching semantics that are easy to get subtly wrong: case-insensitivity, matching a
/// field other than Message, literal handling of LIKE wildcards (%/_), short (&lt;3 char) terms that
/// bypass the trigram index, and AND-combination of a text search with a level filter.
/// </summary>
[TestClass]
[TestCategory("Integration")]
[DoNotParallelize]
public class SearchCorrectnessTests
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
        var searchedFile = Path.Combine(Path.GetTempPath(), "smatrix_" + Guid.NewGuid().ToString("N"));
        _dbPaths.Add(CachedStorage.GetCacheFilePath(searchedFile, ".db"));
        var s = new SqliteStorage(searchedFile);
        s.AddFilteredBatch(rows.ToList());
        return s;
    }

    private static InMemoryPagedSource InMemory(IReadOnlyList<ISearchResult> rows)
        => new(rows.Select((r, i) => new LogLine(r, i)).ToList());

    /// <summary>The set of rows a source returns for a filter, keyed by their (unique) SearchableData.</summary>
    private static HashSet<string> Hits(IPagedLogSource src, FilterSpec f)
        => src.GetPage(f, SortSpec.None, 0, 100_000).Select(r => r.SearchableData).ToHashSet();

    private static FilterSpec Search(string term) => new(term, "", "", "", "", "", null, null);

    [TestMethod]
    public void SearchSemantics_AreIdenticalAcrossBackends()
    {
        // Each row's SearchableData is a unique key. Tokens are placed deliberately to exercise the
        // semantics; levels are all Info and terms are non-numeric so the in-memory extras (Level /
        // Index matching, which SQLite doesn't do) can't cause a spurious divergence.
        var rows = new List<Row>
        {
            new("uid_case Something Error happened", source: "prov"),   // case: 'error' vs 'Error'
            new("uid_mf plain text", source: "NEEDLEPROV"),             // token only in Source field
            new("uid_pct batch 50% done", source: "prov"),             // literal percent
            new("uid_decoy batch 5000 done", source: "prov"),          // must NOT match '50%'
            new("uid_short buzz word", source: "prov"),                // 'zz' for a <3-char term
        };
        for (int i = 0; i < 8; i++) rows.Add(new Row($"uid_f{i} filler line {i}", source: "prov"));

        using var sqlite = NewSqliteWith(rows);
        using var inMem = InMemory(rows);
        using var like = PagedLogSourceFactory.Create(sqlite, fallbackInMemory: null); // index not built → LIKE
        // ... build the FTS index, then a fresh source that uses it.
        sqlite.BuildSearchIndex();
        Assert.IsTrue(sqlite.IsSearchIndexBuilt, "FTS index should be built");
        using var fts = PagedLogSourceFactory.Create(sqlite, fallbackInMemory: null);

        var all = rows.Select(r => r.GetSearchableData()).ToHashSet();

        var cases = new (string term, HashSet<string> expected)[]
        {
            ("error",      new() { "uid_case Something Error happened" }),       // case-insensitive
            ("needleprov", new() { "uid_mf plain text" }),                       // multi-field (Source)
            ("50%",        new() { "uid_pct batch 50% done" }),                  // literal %, decoy excluded
            ("zz",         new() { "uid_short buzz word" }),                     // short term (LIKE even w/ index)
            ("",           all),                                                 // empty → everything
            ("nomatchxyz", new()),                                              // no matches anywhere
        };

        foreach (var (term, expected) in cases)
        {
            var f = Search(term);
            var im = Hits(inMem, f);
            var lk = Hits(like, f);
            var ft = Hits(fts, f);

            Assert.IsTrue(im.SetEquals(expected), $"in-memory '{term}': [{string.Join(" | ", im)}]");
            Assert.IsTrue(lk.SetEquals(expected), $"sqlite-LIKE '{term}': [{string.Join(" | ", lk)}]");
            Assert.IsTrue(ft.SetEquals(expected), $"sqlite-FTS '{term}': [{string.Join(" | ", ft)}]");
        }
    }

    [TestMethod]
    public void CombinedFilters_SearchAndLevel_AreAnded()
    {
        // 'shared' appears in 5 Error rows and 5 Info rows; 5 more Info rows lack it.
        var rows = new List<Row>();
        for (int i = 0; i < 5; i++) rows.Add(new Row($"uid_e{i} shared error row {i}", level: Level.Error));
        for (int i = 0; i < 5; i++) rows.Add(new Row($"uid_i{i} shared info row {i}",  level: Level.Info));
        for (int i = 0; i < 5; i++) rows.Add(new Row($"uid_o{i} other info row {i}",   level: Level.Info));

        using var sqlite = NewSqliteWith(rows);
        sqlite.BuildSearchIndex();
        using var inMem = InMemory(rows);
        using var ftsSrc = PagedLogSourceFactory.Create(sqlite, fallbackInMemory: null);

        // search 'shared' AND level Error → only the 5 error rows that contain 'shared'.
        var combined = new FilterSpec("shared", "", "", "", "", Level.Error.ToString(), null, null);
        var expected = rows.Where(r => r.GetSearchableData().Contains("shared") && r.GetLevel() == Level.Error)
                           .Select(r => r.GetSearchableData()).ToHashSet();
        Assert.AreEqual(5, expected.Count, "sanity: 5 error rows contain 'shared'");

        Assert.IsTrue(Hits(inMem, combined).SetEquals(expected), "in-memory: search AND level");
        Assert.IsTrue(Hits(ftsSrc, combined).SetEquals(expected), "sqlite-FTS: search AND level");

        // 'shared' alone (no level) → all 10 rows that contain it.
        var searchOnly = Search("shared");
        Assert.AreEqual(10, Hits(inMem, searchOnly).Count, "in-memory: search alone matches both levels");
        Assert.AreEqual(10, Hits(ftsSrc, searchOnly).Count, "sqlite-FTS: search alone matches both levels");
    }

    /// <summary>Fake result with controllable Source / Message / SearchableData and Level.</summary>
    private sealed class Row : ISearchResult
    {
        private readonly string _searchable;
        private readonly string _source;
        private readonly Level _level;
        public Row(string searchable, string source = "prov", Level level = Level.Info)
        {
            _searchable = searchable; _source = source; _level = level;
        }
        public DateTime GetLogTime() => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public string GetMachineName() => "M";
        public void WriteToConsole() { }
        public Level GetLevel() => _level;
        public string GetUsername() => "u";
        public string GetTaskName() => "t";
        public string GetOpCode() => "";
        public string GetSource() => _source;
        public string GetSearchableData() => _searchable;
        public string GetMessage() => _searchable;       // mirror searchable so Message search agrees
        public string GetResultSource() => "rs";
    }
}
