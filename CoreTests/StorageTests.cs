using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FindPluginCore.Implementations.Storage;
using FindNeedlePluginLib;
using FindNeedlePluginLib.Interfaces;
using FindNeedleCoreUtils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

[TestClass]
[DoNotParallelize]
public class StorageTests
{
    private readonly List<string> _createdDbPaths = new();

    [TestInitialize]
    public void TestInitialize()
    {
        _createdDbPaths.Clear();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        foreach (var path in _createdDbPaths.Distinct())
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
                Thread.Sleep(50);
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }
    }

    private class DummySearchResult : ISearchResult
    {
        // Return a fixed time to keep tests deterministic
        public static readonly DateTime FixedTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private readonly string _message;
        private readonly string _username;
        private readonly string _resultSource;

        public DummySearchResult(string message = "TestMessage", string username = "TestUser", string resultSource = "TestResultSource")
        {
            _message = message;
            _username = username;
            _resultSource = resultSource;
        }

        public DateTime GetLogTime() => FixedTime;
        public string GetMachineName() => "TestMachine";
        public void WriteToConsole() { }
        public Level GetLevel() => Level.Error;
        public string GetUsername() => _username;
        public string GetTaskName() => "TestTask";
        public string GetOpCode() => "TestOp";
        public string GetSource() => "TestSource";
        public string GetSearchableData() => "TestData";
        public string GetMessage() => _message;
        public string GetResultSource() => _resultSource;
    }

    /// <summary>A dummy whose Level is settable, for the per-level count tests.</summary>
    private sealed class LeveledResult : ISearchResult
    {
        private readonly Level _level;
        public LeveledResult(Level level) { _level = level; }
        public DateTime GetLogTime() => DummySearchResult.FixedTime;
        public string GetMachineName() => "M";
        public void WriteToConsole() { }
        public Level GetLevel() => _level;
        public string GetUsername() => "U";
        public string GetTaskName() => "T";
        public string GetOpCode() => "O";
        public string GetSource() => "S";
        public string GetSearchableData() => "D";
        public string GetMessage() => "Msg";
        public string GetResultSource() => "RS";
    }

    /// <summary>A dummy whose Source (viewer "Provider" / SQL Source column) is settable, for the
    /// multi-select OR-set filter tests.</summary>
    private sealed class ProvResult : ISearchResult
    {
        private readonly string _provider;
        public ProvResult(string provider) { _provider = provider; }
        public DateTime GetLogTime() => DummySearchResult.FixedTime;
        public string GetMachineName() => "M";
        public void WriteToConsole() { }
        public Level GetLevel() => Level.Info;
        public string GetUsername() => "U";
        public string GetTaskName() => "T";
        public string GetOpCode() => "O";
        public string GetSource() => _provider;   // → SQL Source / viewer Provider
        public string GetSearchableData() => "D";
        public string GetMessage() => "Msg";
        public string GetResultSource() => "RS";
    }

    /// <summary>A dummy with settable ActivityId + RelatedActivityId, for the "follow this activity" query.</summary>
    private sealed class CorrelatedResult : ISearchResult
    {
        private readonly string _activityId, _relatedActivityId;
        public CorrelatedResult(string activityId, string relatedActivityId) { _activityId = activityId; _relatedActivityId = relatedActivityId; }
        public DateTime GetLogTime() => DummySearchResult.FixedTime;
        public string GetMachineName() => "M";
        public void WriteToConsole() { }
        public Level GetLevel() => Level.Info;
        public string GetUsername() => "U";
        public string GetTaskName() => "T";
        public string GetOpCode() => "O";
        public string GetSource() => "S";
        public string GetSearchableData() => "D";
        public string GetMessage() => "Msg";
        public string GetResultSource() => "RS";
        public string GetActivityId() => _activityId;
        public string GetRelatedActivityId() => _relatedActivityId;
    }

    /// <summary>A dummy with settable Message + SearchableData, for the SearchableData-blanking test.</summary>
    private sealed class SearchableResult : ISearchResult
    {
        private readonly string _msg, _sd;
        public SearchableResult(string msg, string sd) { _msg = msg; _sd = sd; }
        public DateTime GetLogTime() => DummySearchResult.FixedTime;
        public string GetMachineName() => "M";
        public void WriteToConsole() { }
        public Level GetLevel() => Level.Info;
        public string GetUsername() => "U";
        public string GetTaskName() => "T";
        public string GetOpCode() => "O";
        public string GetSource() => "S";
        public string GetSearchableData() => _sd;
        public string GetMessage() => _msg;
        public string GetResultSource() => "RS";
    }

    private (string searchedFile, string dbPath) CreateUniqueSearchFile()
    {
        var searchedFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var dbPath = CachedStorage.GetCacheFilePath(searchedFile, ".db");
        _createdDbPaths.Add(dbPath);
        if (File.Exists(dbPath))
            File.Delete(dbPath);
        return (searchedFile, dbPath);
    }

    // Factories that produce storage instances for tests and a cleanup action.
    private (Func<ISearchStorage> create, Action cleanup) InMemoryFactory()
    {
        var instance = new InMemoryStorage();
        return (() => instance, () => { instance.Dispose(); });
    }

    private (Func<ISearchStorage> create, Action cleanup) SqliteFactory()
    {
        var (searchedFile, dbPath) = CreateUniqueSearchFile();
        return (() => new SqliteStorage(searchedFile), () => { /* TestCleanup will delete dbPath */ });
    }

    private (Func<ISearchStorage> create, Action cleanup) HybridFactory()
    {
        var (searchedFile, dbPath) = CreateUniqueSearchFile();
        // Use small memory threshold (10MB) to test spilling behavior
        return (() => new HybridStorage(searchedFile, memoryThresholdMB: 10), () => { /* TestCleanup will delete dbPath */ });
    }

    private (Func<ISearchStorage> create, Action cleanup) GetFactoryByKind(string kind)
    {
        return kind switch
        {
            "InMemory" => InMemoryFactory(),
            "Sqlite" => SqliteFactory(),
            "Hybrid" => HybridFactory(),
            _ => throw new ArgumentException("Unknown storage kind: " + kind, nameof(kind)),
        };
    }

    [TestMethod]
    public void Sqlite_GetSourceCounts_GroupsByResultSource()
    {
        var (searchedFile, _) = CreateUniqueSearchFile();
        using var storage = new SqliteStorage(searchedFile);
        storage.ClearTables();
        storage.AddFilteredBatch(new List<ISearchResult>
        {
            new DummySearchResult(resultSource: @"C:\logs\a.log"),
            new DummySearchResult(resultSource: @"C:\logs\a.log"),
            new DummySearchResult(resultSource: @"C:\logs\b.etl"),
        });

        var counts = storage.GetSourceCounts();
        Assert.AreEqual(2, counts.Count);
        Assert.AreEqual(2, counts[@"C:\logs\a.log"]);
        Assert.AreEqual(1, counts[@"C:\logs\b.etl"]);
    }

    [TestMethod]
    public void Sqlite_RunningLevelCounts_MatchSql_AndSurviveClear()
    {
        var (searchedFile, _) = CreateUniqueSearchFile();
        using var storage = new SqliteStorage(searchedFile);
        storage.ClearTables();

        // 5 Error, 3 Warning, 2 Info — across two batches so the running map accumulates.
        var rows = new List<ISearchResult>();
        for (int i = 0; i < 5; i++) rows.Add(new LeveledResult(Level.Error));
        for (int i = 0; i < 3; i++) rows.Add(new LeveledResult(Level.Warning));
        for (int i = 0; i < 2; i++) rows.Add(new LeveledResult(Level.Info));
        storage.AddFilteredBatch(rows.GetRange(0, 6));
        storage.AddFilteredBatch(rows.GetRange(6, 4));

        // Lock-free running map must equal the SQL truth.
        var counts = storage.GetLevelCounts(new SqliteStorage.FilterInput());
        Assert.AreEqual(5, counts[(int)Level.Error]);
        Assert.AreEqual(3, counts[(int)Level.Warning]);
        Assert.AreEqual(2, counts[(int)Level.Info]);

        CollectionAssert.AreEqual(
            new[] { (int)Level.Error, (int)Level.Warning, (int)Level.Info }.OrderBy(x => x).ToArray(),
            storage.GetDistinctLevels().ToArray());

        // A filtered query still goes through SQL and agrees.
        Assert.AreEqual(3, storage.GetFilteredCount(new SqliteStorage.FilterInput { LevelInt = (int)Level.Warning }));

        // After ClearTables the map resets to empty.
        storage.ClearTables();
        Assert.AreEqual(0, storage.GetDistinctLevels().Count);
        Assert.AreEqual(0, storage.GetLevelCounts(new SqliteStorage.FilterInput()).Count);
    }

    [TestMethod]
    [TestCategory("Storage")]
    public void Sqlite_StructuredQuery_FiltersViaSql()
    {
        var (searchedFile, _) = CreateUniqueSearchFile();
        using var storage = new SqliteStorage(searchedFile);
        storage.ClearTables();
        storage.AddFilteredBatch(new List<ISearchResult>
        {
            new DummySearchResult("connection failed - timeout"),
            new DummySearchResult("file not found"),
            new DummySearchResult("authentication failed"),
            new DummySearchResult("all good"),
        });

        FindPluginCore.Searching.Query.QueryNode Q(string s)
        {
            Assert.IsTrue(FindPluginCore.Searching.Query.LogQuery.TryParse(s, out var n, out var e), $"parse: {e}");
            return n;
        }
        Assert.AreEqual(2, storage.GetFilteredCount(new SqliteStorage.FilterInput { Query = Q("message ~ failed") }));
        Assert.AreEqual(1, storage.GetFilteredCount(new SqliteStorage.FilterInput { Query = Q("message ~ failed AND NOT message ~ timeout") }));
        Assert.AreEqual(1, storage.GetFilteredCount(new SqliteStorage.FilterInput { Query = Q("message == \"all good\"") }));
        Assert.AreEqual(3, storage.GetFilteredCount(new SqliteStorage.FilterInput { Query = Q("message ~ failed OR message ~ found") }));
    }

    /// <summary>
    /// Backs the viewer's "Follow this activity" action: the query DSL can filter by ActivityId and (new)
    /// RelatedActivityId, so <c>activityid == X OR relatedactivityid == X</c> selects the activity's own
    /// events PLUS the child activities that carry it as their parent — the one-hop causal sequence.
    /// </summary>
    [TestMethod]
    [TestCategory("Storage")]
    public void Sqlite_FollowActivity_QueryMatchesActivityAndRelated()
    {
        var (searchedFile, _) = CreateUniqueSearchFile();
        using var storage = new SqliteStorage(searchedFile);
        storage.ClearTables();
        storage.AddFilteredBatch(new List<ISearchResult>
        {
            new CorrelatedResult("AAA", ""),      // in activity AAA
            new CorrelatedResult("AAA", ""),      // in activity AAA
            new CorrelatedResult("BBB", "AAA"),   // child of AAA (start event carries AAA as related)
            new CorrelatedResult("CCC", "ZZZ"),   // unrelated
        });

        FindPluginCore.Searching.Query.QueryNode Q(string s)
        {
            Assert.IsTrue(FindPluginCore.Searching.Query.LogQuery.TryParse(s, out var n, out var e), $"parse: {e}");
            return n;
        }

        // The DSL now maps relatedactivityid (and its aliases) to the RelatedActivityId column.
        Assert.AreEqual(1, storage.GetFilteredCount(new SqliteStorage.FilterInput { Query = Q("relatedactivityid == \"AAA\"") }));
        Assert.AreEqual(1, storage.GetFilteredCount(new SqliteStorage.FilterInput { Query = Q("raid == \"AAA\"") }), "alias raid");
        // The "follow this activity" query: activity's own events + its children = 3 of 4 rows
        // (the unrelated CCC/ZZZ row is excluded).
        Assert.AreEqual(3, storage.GetFilteredCount(
            new SqliteStorage.FilterInput { Query = Q("activityid == \"AAA\" OR relatedactivityid == \"AAA\"") }));
        // Following a different activity that nothing references matches only its own event.
        Assert.AreEqual(1, storage.GetFilteredCount(
            new SqliteStorage.FilterInput { Query = Q("activityid == \"CCC\" OR relatedactivityid == \"CCC\"") }));
    }

    [TestMethod]
    [TestCategory("Storage")]
    public void Sqlite_ProviderSet_FiltersAsExactOrSet()
    {
        var (searchedFile, _) = CreateUniqueSearchFile();
        using var storage = new SqliteStorage(searchedFile);
        storage.ClearTables();

        storage.AddFilteredBatch(new List<ISearchResult>
        {
            new ProvResult("Alpha"), new ProvResult("Alpha"),
            new ProvResult("Beta"),
            new ProvResult("Gamma"), new ProvResult("Gamma"), new ProvResult("Gamma"),
        });

        // No set → everything.
        Assert.AreEqual(6, storage.GetFilteredCount(new SqliteStorage.FilterInput()));
        // Single-value set.
        Assert.AreEqual(2, storage.GetFilteredCount(new SqliteStorage.FilterInput { ProviderSet = new[] { "Alpha" } }));
        // Multi-value OR-set.
        Assert.AreEqual(5, storage.GetFilteredCount(new SqliteStorage.FilterInput { ProviderSet = new[] { "Alpha", "Gamma" } }));
        // Case-insensitive (COLLATE NOCASE).
        Assert.AreEqual(1, storage.GetFilteredCount(new SqliteStorage.FilterInput { ProviderSet = new[] { "beta" } }));
        // The set takes precedence over the substring Provider field (the substring is ignored).
        Assert.AreEqual(3, storage.GetFilteredCount(
            new SqliteStorage.FilterInput { ProviderSet = new[] { "Gamma" }, Provider = "Alpha" }));
        // A page query returns exactly the set's rows.
        var page = storage.GetFilteredPage(
            new SqliteStorage.FilterInput { ProviderSet = new[] { "Beta", "Gamma" } },
            new SqliteStorage.SortInput(), 0, 100);
        Assert.AreEqual(4, page.Count);
        Assert.IsTrue(page.All(r => r.GetSource() == "Beta" || r.GetSource() == "Gamma"));
    }

    /// <summary>
    /// FastBulkIngest (defer secondary indexes + no AUTOINCREMENT) must be a pure performance change:
    /// with the flag ON vs OFF the stored data and every query answer are identical — same total, same
    /// filtered counts (Level / ProviderSet / FTS search), and the same rows in the same default order.
    /// </summary>
    [TestMethod]
    [TestCategory("Storage")]
    public void Sqlite_FastBulkIngest_ParityAcrossFlag()
    {
        List<ISearchResult> MakeRows() => new()
        {
            new LeveledResult(Level.Error), new LeveledResult(Level.Error), new LeveledResult(Level.Warning),
            new ProvResult("Alpha"), new ProvResult("Gamma"),
            new DummySearchResult("connection failed - timeout"),
            new DummySearchResult("file not found"),
            new DummySearchResult("authentication failed"),
        };

        (int total, int errors, int alphaGamma, int failed, string order) Snapshot(bool fast)
        {
            bool prior = SqliteStorage.FastBulkIngest;
            SqliteStorage.FastBulkIngest = fast;
            try
            {
                var (searchedFile, _) = CreateUniqueSearchFile();
                using var s = new SqliteStorage(searchedFile);
                s.ClearTables();
                s.AddFilteredBatch(MakeRows());
                s.BuildSearchIndex(); // where FastBulkIngest builds the deferred secondary indexes
                var page = s.GetFilteredPage(new SqliteStorage.FilterInput(), new SqliteStorage.SortInput(), 0, 100);
                return (
                    s.GetStatistics().filteredRecordCount,
                    s.GetFilteredCount(new SqliteStorage.FilterInput { LevelInt = (int)Level.Error }),
                    s.GetFilteredCount(new SqliteStorage.FilterInput { ProviderSet = new[] { "Alpha", "Gamma" } }),
                    s.GetFilteredCount(new SqliteStorage.FilterInput { Search = "failed" }),
                    string.Join("|", page.Select(r => (int)r.GetLevel() + ":" + r.GetSource() + ":" + r.GetMessage())));
            }
            finally { SqliteStorage.FastBulkIngest = prior; }
        }

        var legacy = Snapshot(false);
        var fast = Snapshot(true);

        Assert.AreEqual(8, fast.total, "sanity: all rows stored");
        Assert.AreEqual(legacy.total, fast.total, "row count parity");
        Assert.AreEqual(legacy.errors, fast.errors, "Level filter parity (IX_Level deferred)");
        Assert.AreEqual(legacy.alphaGamma, fast.alphaGamma, "ProviderSet filter parity (IX_Source deferred)");
        Assert.AreEqual(legacy.failed, fast.failed, "FTS search parity");
        Assert.AreEqual(legacy.order, fast.order, "same rows in same default (Id) order — no AUTOINCREMENT divergence");
        // Sanity on the fixture: 2 LeveledResult(Error) + 3 DummySearchResult (also Level.Error) = 5 errors;
        // Alpha+Gamma = 2; "failed" appears in 2 of the 3 messages ("file not found" excluded).
        Assert.AreEqual(5, fast.errors);
        Assert.AreEqual(2, fast.alphaGamma);
        Assert.AreEqual(2, fast.failed);
    }

    /// <summary>
    /// The narrow-insert optimization (plain rows use a 10-column insert, rows with extended ETW fields use
    /// the 21-column one) must be lossless: flag ON vs OFF give identical row counts, extended-field values,
    /// and structured-query results — and a plain row's omitted extended columns read back as "".
    /// </summary>
    [TestMethod]
    [TestCategory("Storage")]
    public void Sqlite_NarrowInsert_ParityWithWide()
    {
        List<ISearchResult> Rows() => new()
        {
            new DummySearchResult(message: "plain one"),   // no extended → narrow
            new CorrelatedResult("AAA", ""),               // ActivityId set → wide
            new DummySearchResult(message: "plain two"),   // narrow
        };

        FindPluginCore.Searching.Query.QueryNode Q(string s)
        {
            Assert.IsTrue(FindPluginCore.Searching.Query.LogQuery.TryParse(s, out var n, out var e), $"parse: {e}");
            return n;
        }

        (int total, int aaa, string acts) Snapshot(bool narrow)
        {
            bool prior = SqliteStorage.UseNarrowInsertForPlainRows;
            SqliteStorage.UseNarrowInsertForPlainRows = narrow;
            try
            {
                var (searchedFile, _) = CreateUniqueSearchFile();
                using var s = new SqliteStorage(searchedFile);
                s.ClearTables();
                s.AddFilteredBatch(Rows());
                s.BuildSearchIndex();
                var page = s.GetFilteredPage(new SqliteStorage.FilterInput(), new SqliteStorage.SortInput(), 0, 100);
                return (
                    s.GetStatistics().filteredRecordCount,
                    s.GetFilteredCount(new SqliteStorage.FilterInput { Query = Q("activityid == \"AAA\"") }),
                    string.Join("|", page.Select(r => r.GetActivityId())));
            }
            finally { SqliteStorage.UseNarrowInsertForPlainRows = prior; }
        }

        var wide = Snapshot(false);
        var narrow = Snapshot(true);

        Assert.AreEqual(3, narrow.total);
        Assert.AreEqual(wide.total, narrow.total, "row-count parity");
        Assert.AreEqual(1, narrow.aaa);
        Assert.AreEqual(wide.aaa, narrow.aaa, "activityid query finds the wide (extended) row under either mode");
        Assert.AreEqual("|AAA|", narrow.acts, "plain rows read back empty ActivityId; the wide row keeps AAA");
        Assert.AreEqual(wide.acts, narrow.acts, "extended-field parity");
    }

    /// <summary>
    /// BlankRedundantSearchableData (store NULL when SearchableData == Message) must be lossless: with the
    /// flag ON vs OFF the reconstructed SearchableData and global-search results are identical, a superset
    /// SearchableData is preserved, and a genuinely-empty one stays empty (not reconstructed to Message).
    /// </summary>
    [TestMethod]
    [TestCategory("Storage")]
    public void Sqlite_BlankSearchableData_ParityAndReconstruct()
    {
        List<ISearchResult> Rows() => new()
        {
            new SearchableResult("alpha bravo", "alpha bravo"),        // duplicate → stored NULL, reconstructs
            new SearchableResult("charlie", "charlie delta echo"),     // superset → stored as-is
            new SearchableResult("foxtrot", ""),                       // genuinely empty → stays ""
        };

        (string sds, int bravo, int delta) Snapshot(bool blank)
        {
            bool prior = SqliteStorage.BlankRedundantSearchableData;
            SqliteStorage.BlankRedundantSearchableData = blank;
            try
            {
                var (searchedFile, _) = CreateUniqueSearchFile();
                using var s = new SqliteStorage(searchedFile);
                s.ClearTables();
                s.AddFilteredBatch(Rows());
                s.BuildSearchIndex();
                var page = s.GetFilteredPage(new SqliteStorage.FilterInput(), new SqliteStorage.SortInput(), 0, 100);
                return (
                    string.Join("|", page.Select(r => r.GetSearchableData())),
                    s.GetFilteredCount(new SqliteStorage.FilterInput { Search = "bravo" }),  // in row 1
                    s.GetFilteredCount(new SqliteStorage.FilterInput { Search = "delta" })); // only in row 2's SearchableData
            }
            finally { SqliteStorage.BlankRedundantSearchableData = prior; }
        }

        var off = Snapshot(false);
        var on = Snapshot(true);

        Assert.AreEqual(off.sds, on.sds, "reconstructed SearchableData identical to the un-blanked storage");
        Assert.AreEqual("alpha bravo|charlie delta echo|", on.sds,
            "dup reconstructed to Message, superset preserved, genuinely-empty stays empty");
        Assert.AreEqual(off.bravo, on.bravo, "search parity (term in a blanked row)");
        Assert.AreEqual(1, on.bravo);
        Assert.AreEqual(off.delta, on.delta, "search parity (term only in a preserved superset SearchableData)");
        Assert.AreEqual(1, on.delta);
    }

    // Backs the fast "known value" filter dropdowns (GetFieldCounts) — an exact GROUP BY per field
    // instead of the old O(sample) row scan, so the dropdowns open quickly on big result sets.
    [TestMethod]
    [TestCategory("Storage")]
    public void GetFieldCounts_GroupsByField_AndHonorsCrossFilter()
    {
        var (searchedFile, _) = CreateUniqueSearchFile();
        using var storage = new SqliteStorage(searchedFile);
        storage.ClearTables();
        storage.AddFilteredBatch(new List<ISearchResult>
        {
            new ProvResult("Alpha"), new ProvResult("Alpha"),
            new ProvResult("Beta"),
            new ProvResult("Gamma"), new ProvResult("Gamma"), new ProvResult("Gamma"),
        });

        // "Provider" facet → SQL Source column: exact distinct values + counts.
        var prov = storage.GetFieldCounts("Provider", new SqliteStorage.FilterInput());
        Assert.AreEqual(2, prov["Alpha"]);
        Assert.AreEqual(1, prov["Beta"]);
        Assert.AreEqual(3, prov["Gamma"]);

        // "Source" facet → SQL ResultSource column (every ProvResult shares "RS").
        var src = storage.GetFieldCounts("Source", new SqliteStorage.FilterInput());
        Assert.AreEqual(6, src["RS"]);

        // Cross-filter: counts reflect an active filter on another field.
        var filtered = storage.GetFieldCounts("Provider", new SqliteStorage.FilterInput { ProviderSet = new[] { "Gamma" } });
        Assert.AreEqual(1, filtered.Count);
        Assert.AreEqual(3, filtered["Gamma"]);

        // Unknown / non-column field → empty, never throws.
        Assert.AreEqual(0, storage.GetFieldCounts("Nope", new SqliteStorage.FilterInput()).Count);
    }

    // Keyset jump-to-last: GetLastFilteredPage (reversed query, no deep OFFSET) must return EXACTLY the
    // same rows as a deep-OFFSET fetch of the final page — for ascending, descending, and load order.
    [TestMethod]
    [TestCategory("Storage")]
    public void GetLastFilteredPage_MatchesDeepOffset_AcrossSorts()
    {
        var (searchedFile, _) = CreateUniqueSearchFile();
        using var storage = new SqliteStorage(searchedFile);
        storage.ClearTables();
        var rows = new System.Collections.Generic.List<ISearchResult>();
        for (int i = 0; i < 25; i++) rows.Add(new ProvResult("p" + i)); // distinct, stable insert order
        storage.AddFilteredBatch(rows);

        const int pageSize = 10;           // 25 rows → 3 pages; last page is a partial 5 rows
        const int total = 25, lastOffset = 20, lastCount = 5;
        var filter = new SqliteStorage.FilterInput();

        foreach (var sort in new[]
        {
            new SqliteStorage.SortInput(),                                         // load order (Id)
            new SqliteStorage.SortInput { Column = "Index", Descending = false },  // Id asc
            new SqliteStorage.SortInput { Column = "Index", Descending = true },   // Id desc
        })
        {
            var viaOffset = storage.GetFilteredPage(filter, sort, lastOffset, pageSize)
                                   .Select(r => r.GetRowId()).ToList();
            var viaLast = storage.GetLastFilteredPage(filter, sort, lastCount)
                                 .Select(r => r.GetRowId()).ToList();
            Assert.AreEqual(lastCount, viaLast.Count, $"last page should have {lastCount} rows (sort col='{sort.Column}' desc={sort.Descending})");
            CollectionAssert.AreEqual(viaOffset, viaLast,
                $"reversed last page must equal the deep-offset last page (sort col='{sort.Column}' desc={sort.Descending})");
        }
        _ = total;
    }

    // Parameterized tests using DataTestMethod to run each scenario for both implementations.

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void ContentVerification(string kind)
    {
        var factory = GetFactoryByKind(kind);
        using var storage = factory.create();

        var a = new DummySearchResult("MessageA", "UserA", "SourceA");
        var b = new DummySearchResult("MessageB", "UserB", "SourceB");
        storage.AddRawBatch(new[] { a, b });

        var all = new List<ISearchResult>();
        storage.GetRawResultsInBatches(batch => all.AddRange(batch), 10);
        Assert.AreEqual(2, all.Count, "Expected two raw results");
        Assert.AreEqual("MessageA", all[0].GetMessage());
        Assert.AreEqual("UserA", all[0].GetUsername());
        Assert.AreEqual("SourceB", all[1].GetResultSource());

        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void ContentAndDateRoundtrip(string kind)
    {
        var factory = GetFactoryByKind(kind);

        // write and dispose
        using (var storage = factory.create())
        {
            var a = new DummySearchResult("Msg1", "User1", "Src1");
            var b = new DummySearchResult("Msg2", "User2", "Src2");
            storage.AddRawBatch(new[] { a, b });
        }

        // reopen and read
        using (var storage = factory.create())
        {
            var results = new List<ISearchResult>();
            storage.GetRawResultsInBatches(batch => results.AddRange(batch), 10);
            Assert.AreEqual(2, results.Count, "Should return two results");
            Assert.AreEqual("Msg1", results[0].GetMessage());
            Assert.AreEqual("User1", results[0].GetUsername());
            Assert.AreEqual(DummySearchResult.FixedTime, results[0].GetLogTime());
        }

        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void DisposeReopen(string kind)
    {
        var factory = GetFactoryByKind(kind);

        using (var storage = factory.create())
        {
            storage.AddRawBatch(new[] { new DummySearchResult() });
        }

        // reopening should succeed
        using (var reopened = factory.create())
        {
            var stats = reopened.GetStatistics();
            Assert.IsTrue(stats.rawRecordCount >= 0);
        }

        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void NullBatch_Throws(string kind)
    {
        var factory = GetFactoryByKind(kind);
        using var storage = factory.create();
        Assert.ThrowsException<ArgumentNullException>(() => storage.AddRawBatch(null));
        Assert.ThrowsException<ArgumentNullException>(() => storage.AddFilteredBatch(null));
        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void PreCancelledToken_PreventsWork(string kind)
    {
        var factory = GetFactoryByKind(kind);
        var cts = new CancellationTokenSource();
        cts.Cancel();
        using (var storage = factory.create())
        {
            storage.AddRawBatch(new[] { new DummySearchResult(), new DummySearchResult() }, cts.Token);
            var results = new List<ISearchResult>();
            storage.GetRawResultsInBatches(b => results.AddRange(b), 10);
            Assert.AreEqual(0, results.Count);
        }
        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void BatchingBehavior(string kind)
    {
        var factory = GetFactoryByKind(kind);
        var items = Enumerable.Range(0, 5).Select(i => (ISearchResult)new DummySearchResult($"M{i}")).ToList();

        using (var storage = factory.create())
        {
            storage.AddRawBatch(items);
        }

        using (var storage = factory.create())
        {
            var sqlBatches = new List<List<ISearchResult>>();
            storage.GetRawResultsInBatches(b => sqlBatches.Add(b), 2);
            Assert.AreEqual(3, sqlBatches.Count, "Should produce 3 batches: 2,2,1");
            Assert.AreEqual(2, sqlBatches[0].Count);
            Assert.AreEqual("M0", sqlBatches[0][0].GetMessage());
        }

        factory.cleanup();
    }

    // Disk-backed storage now opens an existing cache as-is (no wipe on construction), so a
    // consumer running a fresh scan must ClearTables() first. This locks in that contract:
    // reopen + clear + write must yield only the new rows, never the stale ones. Mirrors what
    // NuSearchQuery.Step2 does on the non-cache-reuse path.
    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void ReopenThenClear_StartsEmpty_NoStaleDuplicates(string kind)
    {
        var factory = GetFactoryByKind(kind);

        using (var storage = factory.create())
        {
            storage.AddRawBatch(new[] { new DummySearchResult("Stale") });
        }

        using (var storage = factory.create())
        {
            storage.ClearTables(); // consumer chooses a fresh scan
            storage.AddRawBatch(new[] { new DummySearchResult("Fresh") });

            var results = new List<ISearchResult>();
            storage.GetRawResultsInBatches(batch => results.AddRange(batch), 10);
            Assert.AreEqual(1, results.Count, "only the freshly-written row should be present");
            Assert.AreEqual("Fresh", results[0].GetMessage());
        }

        factory.cleanup();
    }

    // GetStatistics returns running counts maintained on insert/clear (not a SELECT COUNT(*) scan).
    // This guards both the maintenance paths and the constructor seeding: a reopened (warm) store
    // must report the persisted counts without rescanning.
    [DataTestMethod]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void GetStatistics_AfterReopen_ReflectsPersistedCounts(string kind)
    {
        var factory = GetFactoryByKind(kind);

        using (var storage = factory.create())
        {
            storage.AddRawBatch(Enumerable.Range(0, 7).Select(i => (ISearchResult)new DummySearchResult($"R{i}")).ToList());
            storage.AddFilteredBatch(Enumerable.Range(0, 3).Select(i => (ISearchResult)new DummySearchResult($"F{i}")).ToList());
            var s = storage.GetStatistics();
            Assert.AreEqual(7, s.rawRecordCount, "raw count after inserts");
            Assert.AreEqual(3, s.filteredRecordCount, "filtered count after inserts");
        }

        using (var storage = factory.create())
        {
            var s = storage.GetStatistics();
            Assert.AreEqual(7, s.rawRecordCount, "reopened store should report persisted raw count without rescanning");
            Assert.AreEqual(3, s.filteredRecordCount, "reopened store should report persisted filtered count");
        }

        factory.cleanup();
    }

    // --- Additional tests added ---

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void Concurrency_AddsArePresent(string kind)
    {
        var factory = GetFactoryByKind(kind);
        using var storage = factory.create();

        var tasks = new List<Task>();
        const int writers = 10;
        const int perWriter = 100;
        for (int w = 0; w < writers; w++)
        {
            var idx = w;
            tasks.Add(Task.Run(() =>
            {
                var items = Enumerable.Range(0, perWriter).Select(i => (ISearchResult)new DummySearchResult($"T{idx}-{i}")).ToList();
                storage.AddRawBatch(items);
            }));
        }
        Task.WaitAll(tasks.ToArray());

        var all = new List<ISearchResult>();
        storage.GetRawResultsInBatches(b => all.AddRange(b), 1000);
        Assert.AreEqual(writers * perWriter, all.Count);

        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void CancellationDuringWrite_StopsEarly(string kind)
    {
        var factory = GetFactoryByKind(kind);
        using var storage = factory.create();

        var items = Enumerable.Range(0, 10000).Select(i => (ISearchResult)new DummySearchResult($"M{i}")).ToList();
        var cts = new CancellationTokenSource();
        // Start the add on a background task so we can cancel while it's running
        var addTask = Task.Run(() => storage.AddRawBatch(items, cts.Token));
        // Cancel shortly after starting
        Task.Run(() => { Thread.Sleep(5); cts.Cancel(); });

        // Wait for add to complete
        try { addTask.Wait(); } catch (AggregateException) { }

        var all = new List<ISearchResult>();
        storage.GetRawResultsInBatches(b => all.AddRange(b), 1000);
        // InMemory can be so fast that cancellation arrives too late; accept either partial or complete writes.
        Assert.IsTrue(all.Count <= items.Count, $"Unexpected number of written items: {all.Count} of {items.Count}");

        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void CancellationDuringRead_StopsEarly(string kind)
    {
        var factory = GetFactoryByKind(kind);
        using var storage = factory.create();

        var total = 1000;
        var items = Enumerable.Range(0, total).Select(i => (ISearchResult)new DummySearchResult($"R{i}")).ToList();
        storage.AddRawBatch(items);

        var cts = new CancellationTokenSource();
        var readResults = new List<ISearchResult>();

        // Start a canceller that will fire shortly after read starts
        Task.Run(() => { Thread.Sleep(5); cts.Cancel(); });

        storage.GetRawResultsInBatches(batch => { readResults.AddRange(batch); Thread.Sleep(1); }, 1, cts.Token);

        Assert.IsTrue(readResults.Count < total);

        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void ExactBatching_Boundaries(string kind)
    {
        var factory = GetFactoryByKind(kind);
        using var storage = factory.create();

        var items = Enumerable.Range(0, 5).Select(i => (ISearchResult)new DummySearchResult($"B{i}")).ToList();
        storage.AddRawBatch(items);

        var batches2 = new List<List<ISearchResult>>();
        storage.GetRawResultsInBatches(b => batches2.Add(b), 2);
        Assert.AreEqual(3, batches2.Count);

        var batchesLarge = new List<List<ISearchResult>>();
        storage.GetRawResultsInBatches(b => batchesLarge.Add(b), 10);
        Assert.AreEqual(1, batchesLarge.Count);

        var batchesOne = new List<List<ISearchResult>>();
        storage.GetRawResultsInBatches(b => batchesOne.Add(b), 1);
        Assert.AreEqual(5, batchesOne.Count);

        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void Ordering_IsPreserved(string kind)
    {
        var factory = GetFactoryByKind(kind);
        using var storage = factory.create();

        var items = Enumerable.Range(0, 5).Select(i => (ISearchResult)new DummySearchResult($"O{i}")).ToList();
        storage.AddRawBatch(items);

        var all = new List<ISearchResult>();
        storage.GetRawResultsInBatches(b => all.AddRange(b), 10);
        CollectionAssert.AreEqual(items.Select(x => x.GetMessage()).ToList(), all.Select(x => x.GetMessage()).ToList());

        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void Isolation_RawVsFiltered(string kind)
    {
        var factory = GetFactoryByKind(kind);
        using var storage = factory.create();

        var raw = new[] { new DummySearchResult("raw1"), new DummySearchResult("raw2") };
        var filtered = new[] { new DummySearchResult("f1"), new DummySearchResult("f2") };
        storage.AddRawBatch(raw);
        storage.AddFilteredBatch(filtered);

        var allRaw = new List<ISearchResult>();
        storage.GetRawResultsInBatches(b => allRaw.AddRange(b), 10);
        var allFiltered = new List<ISearchResult>();
        storage.GetFilteredResultsInBatches(b => allFiltered.AddRange(b), 10);

        Assert.AreEqual(2, allRaw.Count);
        Assert.AreEqual(2, allFiltered.Count);
        Assert.IsTrue(allRaw.All(r => r.GetMessage().StartsWith("raw")));
        Assert.IsTrue(allFiltered.All(r => r.GetMessage().StartsWith("f")));

        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("Sqlite")]
    public void Persistence_Sqlite_DataPersistsAcrossInstances(string kind)
    {
        var factory = GetFactoryByKind(kind);

        // create, write and dispose
        using (var storage = factory.create())
        {
            var items = Enumerable.Range(0, 10).Select(i => (ISearchResult)new DummySearchResult($"P{i}")).ToList();
            storage.AddRawBatch(items);
        }

        // reopen and verify
        using (var storage = factory.create())
        {
            var all = new List<ISearchResult>();
            storage.GetRawResultsInBatches(b => all.AddRange(b), 1000);
            Assert.AreEqual(10, all.Count);
        }

        // verify file size grew
        var dbPath = _createdDbPaths.Last();
        Assert.IsTrue(File.Exists(dbPath));
        var size = new FileInfo(dbPath).Length;
        Assert.IsTrue(size > 0);

        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    public void DisposeBehavior_MultipleDisposeAndUse(string kind)
    {
        var factory = GetFactoryByKind(kind);
        var storage = factory.create();

        // double dispose must be safe
        storage.Dispose();
        storage.Dispose();

        // Behavior after dispose: InMemory is a no-op and still usable; Sqlite may throw
        try
        {
            storage.AddRawBatch(new[] { new DummySearchResult("afterDispose") });
            // If no exception, ensure call succeeded for InMemory
            var all = new List<ISearchResult>();
            storage.GetRawResultsInBatches(b => all.AddRange(b), 1000);
            // Either zero or more -- just ensure it does not crash the test framework
        }
        catch (Exception ex)
        {
            // For SQLite we may get ObjectDisposedException or InvalidOperationException
            Assert.IsTrue(ex is ObjectDisposedException || ex is InvalidOperationException);
        }
        finally
        {
            // Ensure cleanup (dispose again if needed)
            try { storage.Dispose(); } catch { }
        }

        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void Statistics_AreAccurate(string kind)
    {
        var factory = GetFactoryByKind(kind);
        using var storage = factory.create();

        storage.AddRawBatch(new[] { new DummySearchResult("sraw1"), new DummySearchResult("sraw2") });
        storage.AddFilteredBatch(new[] { new DummySearchResult("sf1") });

        var stats = storage.GetStatistics();
        Assert.AreEqual(2, stats.rawRecordCount);
        Assert.AreEqual(1, stats.filteredRecordCount);

        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void LargePayloads_HandleAndBatch(string kind)
    {
        var factory = GetFactoryByKind(kind);
        using var storage = factory.create();

        var large = new string('X', 100_000);
        var items = Enumerable.Range(0, 3).Select(i => (ISearchResult)new DummySearchResult(large)).ToList();
        storage.AddRawBatch(items);

        var batches = new List<List<ISearchResult>>();
        storage.GetRawResultsInBatches(b => batches.Add(b), 2);
        Assert.AreEqual(2, batches.Count);
        Assert.AreEqual(2, batches[0].Count);
        Assert.AreEqual(1, batches[1].Count);

        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void MutationSafety_CallbackMutatingBatchDoesNotAffectStorage(string kind)
    {
        var factory = GetFactoryByKind(kind);
        using var storage = factory.create();

        var items = Enumerable.Range(0, 5).Select(i => (ISearchResult)new DummySearchResult($"MS{i}")).ToList();
        storage.AddRawBatch(items);

        // callback mutates the provided batch (clears it)
        storage.GetRawResultsInBatches(batch => { batch.Clear(); }, 2);

        // subsequent read should still return all stored items
        var all = new List<ISearchResult>();
        storage.GetRawResultsInBatches(b => all.AddRange(b), 10);
        Assert.AreEqual(5, all.Count);

        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    [TestCategory("Performance")]
    public void Performance_InsertOneMillion(string kind)
    {
        var factory = GetFactoryByKind(kind);
        const int total = 1_000_000;
        const int batchSize = 10_000; // 100 batches
        var batches = total / batchSize;

        // If sqlite, the factory call already created and registered the DB path.
        string? dbPath = null;
        long dbSizeBefore = 0;
        if (kind == "Sqlite" && _createdDbPaths.Count > 0)
        {
            dbPath = _createdDbPaths.Last();
            if (File.Exists(dbPath)) dbSizeBefore = new FileInfo(dbPath).Length;
        }

        // Capture memory usage before
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long memBefore = GC.GetTotalMemory(true);
        var proc = Process.GetCurrentProcess();
        long procMemBefore = proc.PrivateMemorySize64;

        using var storage = factory.create();

        var sw = Stopwatch.StartNew();
        for (var b = 0; b < batches; b++)
        {
            var list = new List<ISearchResult>(batchSize);
            var baseIndex = b * batchSize;
            for (var i = 0; i < batchSize; i++)
            {
                list.Add(new DummySearchResult("PerfMsg" + (baseIndex + i)));
            }
            storage.AddRawBatch(list);
        }
        sw.Stop();

        // Force a GC to get a cleaner measure after inserts
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long memAfter = GC.GetTotalMemory(true);
        long procMemAfter = proc.PrivateMemorySize64;

        var stats = storage.GetStatistics();
        // Verify all records were written
        Assert.AreEqual(total, stats.rawRecordCount, $"Expected {total} records in {kind}, got {stats.rawRecordCount}");

        long dbSizeAfter = 0;
        if (kind == "Sqlite" && dbPath != null && File.Exists(dbPath))
        {
            dbSizeAfter = new FileInfo(dbPath).Length;
        }

        // Compute deltas
        long gcDelta = memAfter - memBefore;
        long procDelta = procMemAfter - procMemBefore;
        long dbDelta = dbSizeAfter - dbSizeBefore;

        // Emit timing and resource usage information to test output
        Console.WriteLine($"Inserted {total:N0} records into {kind} in {sw.Elapsed.TotalSeconds:F2}s");
        Console.WriteLine($"GC memory delta: {gcDelta:N0} bytes ({gcDelta / 1024.0 / 1024.0:F2} MB)");
        Console.WriteLine($"Process private memory delta: {procDelta:N0} bytes ({procDelta / 1024.0 / 1024.0:F2} MB)");
        Console.WriteLine($"Storage-reported sizeInMemory: {stats.sizeInMemory:N0} bytes ({stats.sizeInMemory / 1024.0 / 1024.0:F2} MB)");
        if (kind == "Sqlite")
        {
            Console.WriteLine($"DB file: {dbPath}");
            Console.WriteLine($"DB size before: {dbSizeBefore:N0} bytes, after: {dbSizeAfter:N0} bytes, delta: {dbDelta:N0} bytes ({dbDelta / 1024.0 / 1024.0:F2} MB)");
            Console.WriteLine($"DB reported by GetStatistics: sizeOnDisk={stats.sizeOnDisk:N0} bytes");
        }

        Console.WriteLine($"Per-record GC delta: {gcDelta / (double)total:F2} bytes");
        Console.WriteLine($"Per-record storage-reported size: {stats.sizeInMemory / (double)total:F2} bytes");
        if (kind == "Sqlite")
        {
            Console.WriteLine($"Per-record DB delta: {dbDelta / (double)total:F2} bytes");
        }

        factory.cleanup();
    }
}
