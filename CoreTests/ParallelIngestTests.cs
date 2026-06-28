using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using FindNeedleCoreUtils;
using FindNeedlePluginLib;
using FindPluginCore.Implementations.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

/// <summary>
/// Correctness tests for the parallel fan-out ingest (<see cref="ParallelIngestSink"/> +
/// <see cref="SqliteStorage.AddFilteredBatchWithIds"/> + <see cref="SqliteStorage.MergeFilteredFrom"/>).
/// The win (speed) is measured elsewhere on a real .etl; these assert the thing that actually matters for
/// correctness: after the shards merge, the single DB is byte-for-byte equivalent to the serial path —
/// same row count, same scan-order (default ORDER BY Id ASC), and the same rows survive search.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ParallelIngestTests
{
    private readonly List<string> _dbPaths = new();

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        foreach (var db in _dbPaths.Distinct())
            foreach (var p in new[] { db, db + "-wal", db + "-shm", db + "-journal" })
                try { if (File.Exists(p)) File.Delete(p); } catch { }
    }

    private SqliteStorage NewStore()
    {
        var searched = Path.Combine(Path.GetTempPath(), "pingest_" + Guid.NewGuid().ToString("N"));
        _dbPaths.Add(CachedStorage.GetCacheFilePath(searched, ".db"));
        var s = new SqliteStorage(searched);
        s.ClearTables();
        return s;
    }

    /// <summary>A row whose Message encodes its global scan position, so we can assert order after merge.</summary>
    private sealed class SeqResult : ISearchResult
    {
        private readonly int _seq;
        public SeqResult(int seq) { _seq = seq; }
        public DateTime GetLogTime() => new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(_seq);
        public string GetMachineName() => "M";
        public void WriteToConsole() { }
        public Level GetLevel() => Level.Info;
        public string GetUsername() => "U";
        public string GetTaskName() => "T";
        public string GetOpCode() => "O";
        public string GetSource() => "Prov";
        public string GetSearchableData() => "D";
        public string GetMessage() => $"msg-{_seq:D7}";
        public string GetResultSource() => "RS";
    }

    private static List<ISearchResult> Batch(int start, int count)
    {
        var b = new List<ISearchResult>(count);
        for (int i = 0; i < count; i++) b.Add(new SeqResult(start + i));
        return b;
    }

    private List<string> ReadMessagesInIdOrder(SqliteStorage s)
    {
        // The viewer's default page order is ORDER BY Id ASC; read the whole table that way.
        var msgs = new List<string>();
        var page = s.GetFilteredPage(new SqliteStorage.FilterInput(), new SqliteStorage.SortInput(), 0, 1_000_000);
        foreach (var r in page) msgs.Add(r.GetMessage());
        return msgs;
    }

    [TestMethod]
    public void MergeFilteredFrom_WithGlobalIds_PreservesScanOrder()
    {
        // Three shards fed batches OUT of scan order (round-robin style), each batch stamped with its
        // global base Id. After merge, ORDER BY Id ASC must read back 0..N-1 in perfect scan order.
        const int batch = 100, batches = 9; // 900 rows across 3 shards, interleaved
        var shards = new[] { NewStore(), NewStore(), NewStore() };
        for (int bi = 0; bi < batches; bi++)
        {
            int baseId = bi * batch + 1;                 // 1-based global Ids
            shards[bi % 3].AddFilteredBatchWithIds(Batch(bi * batch, batch), baseId);
        }
        var shardPaths = _dbPaths.ToList(); // the 3 shard DBs (target created after)
        foreach (var s in shards) s.Dispose();
        SqliteConnection.ClearAllPools();

        var target = NewStore();
        long merged = target.MergeFilteredFrom(shardPaths);

        Assert.AreEqual(batch * batches, merged, "all rows merged");
        Assert.AreEqual(batch * batches, target.GetFilteredCount(new SqliteStorage.FilterInput()), "count");
        var msgs = ReadMessagesInIdOrder(target);
        for (int i = 0; i < msgs.Count; i++)
            Assert.AreEqual($"msg-{i:D7}", msgs[i], $"row at position {i} must be scan-order seq {i}");
    }

    [TestMethod]
    public void ParallelIngestSink_EndToEnd_MatchesSerial()
    {
        // Same input through (a) serial single-writer insert and (b) the fan-out sink + merge. The merged
        // DB must be equivalent: identical row count, identical scan order, identical search result.
        const int batch = 250, batches = 40; // 10,000 rows
        var input = Enumerable.Range(0, batches).Select(bi => Batch(bi * batch, batch)).ToList();

        // (a) serial baseline
        var serial = NewStore();
        long seq = 0;
        foreach (var b in input) { serial.AddFilteredBatch(b); seq += b.Count; }
        var serialMsgs = ReadMessagesInIdOrder(serial);
        int serialMatch = serial.GetFilteredCount(new SqliteStorage.FilterInput { Message = "msg-0000012" });
        serial.Dispose();

        // (b) fan-out: producer stamps global Ids, 4 shard writers, then merge into a fresh store
        var sink = new ParallelIngestSink(4, CancellationToken.None);
        foreach (var b in input) sink.Add(b);
        var target = NewStore();
        long merged;
        using (sink) merged = sink.CompleteAndMergeInto(target);

        Assert.AreEqual(batch * batches, merged, "fan-out merged the same row count");
        var parMsgs = ReadMessagesInIdOrder(target);
        CollectionAssert.AreEqual(serialMsgs, parMsgs, "fan-out scan order (ORDER BY Id) matches serial");
        Assert.AreEqual(serialMatch, target.GetFilteredCount(new SqliteStorage.FilterInput { Message = "msg-0000012" }),
            "substring search returns the same rows after merge");
        Assert.AreEqual(serialMsgs.Count, target.GetFilteredCount(new SqliteStorage.FilterInput()), "total count");
    }

    [TestMethod]
    public void AddFilteredBatchWithIds_AssignsExplicitSequentialIds()
    {
        // A single shard, one batch at base 5000 → rows must land at Ids 5000..5009 (not autoincrement 1..10).
        var s = NewStore();
        s.AddFilteredBatchWithIds(Batch(0, 10), baseId: 5000);
        var page = s.GetFilteredPage(new SqliteStorage.FilterInput(), new SqliteStorage.SortInput(), 0, 100);
        Assert.AreEqual(10, page.Count);
        // Read raw Ids via the connection to confirm explicit assignment.
        s.Dispose();
        SqliteConnection.ClearAllPools();
        using var conn = new SqliteConnection($"Data Source={_dbPaths.Last()};Mode=ReadOnly");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MIN(Id), MAX(Id), COUNT(*) FROM FilteredResults";
        using var r = cmd.ExecuteReader();
        Assert.IsTrue(r.Read());
        Assert.AreEqual(5000L, r.GetInt64(0), "min Id");
        Assert.AreEqual(5009L, r.GetInt64(1), "max Id");
        Assert.AreEqual(10L, r.GetInt64(2), "count");
    }
}
