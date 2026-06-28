using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

/// <summary>
/// SPIKE (measurement only — does not touch production): evaluate ROW sharding — splitting the row
/// store across N SQLite DB files written in parallel (N writers beat SQLite's single-writer insert
/// floor), then querying via ATTACH + UNION ALL. Answers the two make-or-break questions before any
/// production work:
///   1. Does parallel insert across N DBs actually beat a single writer, and by how much? (Inserts are
///      more I/O/btree-bound than the CPU-bound FTS build, so this may scale far less than FTS sharding.)
///   2. Is the merged query (UNION ALL + WHERE + ORDER BY + LIMIT/OFFSET + COUNT) correct vs a single
///      DB, and fast enough — especially deep pagination, where OFFSET scans the whole union?
///
/// Contiguous Id ranges per shard preserve global "LoadOrder" (ORDER BY Id == original order).
/// Tunable: FINDNEEDLE_SPIKE_ROWS (default 2,000,000), FINDNEEDLE_SPIKE_SHARDS (default 4).
/// </summary>
[TestClass]
[TestCategory("Performance")]
public class RowShardSpike
{
    private readonly List<string> _files = new();
    public TestContext TestContext { get; set; }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in _files) { try { if (File.Exists(f)) File.Delete(f); } catch { } }
    }

    private string NewDb(string tag)
    {
        var p = Path.Combine(Path.GetTempPath(), $"rowshard_{tag}_{Guid.NewGuid():N}.db");
        _files.Add(p);
        return p;
    }

    private static void Pragmas(SqliteConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=MEMORY; PRAGMA synchronous=OFF; PRAGMA temp_store=MEMORY; PRAGMA cache_size=-65536;";
        cmd.ExecuteNonQuery();
    }

    private static void CreateTable(SqliteConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "CREATE TABLE T(Id INTEGER PRIMARY KEY, Ticks INTEGER, Level INTEGER, Message TEXT);";
        cmd.ExecuteNonQuery();
    }

    /// <summary>Insert ids [lo,hi) into table T of <paramref name="conn"/> (one writer, one transaction).</summary>
    private static void InsertRange(SqliteConnection conn, long lo, long hi)
    {
        using var tx = conn.BeginTransaction();
        using var ins = conn.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = "INSERT INTO T(Id,Ticks,Level,Message) VALUES(@id,@t,@l,@m)";
        var pid = ins.CreateParameter(); pid.ParameterName = "@id"; ins.Parameters.Add(pid);
        var pt = ins.CreateParameter(); pt.ParameterName = "@t"; ins.Parameters.Add(pt);
        var pl = ins.CreateParameter(); pl.ParameterName = "@l"; ins.Parameters.Add(pl);
        var pm = ins.CreateParameter(); pm.ParameterName = "@m"; ins.Parameters.Add(pm);
        ins.Prepare();
        for (long i = lo; i < hi; i++)
        {
            pid.Value = i;
            pt.Value = i;                  // monotonic "time"
            pl.Value = (int)(i % 6);       // levels 0..5
            pm.Value = $"[2026-01-01] INFO: event {i} processed for request req-{i % 9973} on node-{i % 64}";
            ins.ExecuteNonQuery();
        }
        tx.Commit();
    }

    [TestMethod]
    [Timeout(900_000)]
    public void RowSharding_ParallelInsert_AndMergedQuery()
    {
        int rows = EnvInt("FINDNEEDLE_SPIKE_ROWS", 2_000_000);
        int shards = EnvInt("FINDNEEDLE_SPIKE_SHARDS", 4);

        // ---- Single DB: one-writer insert ----
        var singlePath = NewDb("single");
        var swS = Stopwatch.StartNew();
        using (var c = new SqliteConnection($"Data Source={singlePath}"))
        {
            c.Open(); Pragmas(c); CreateTable(c);
            InsertRange(c, 0, rows);
        }
        swS.Stop();
        TestContext.WriteLine($"rows={rows:N0} shards={shards}");
        TestContext.WriteLine($"SINGLE insert : {swS.ElapsedMilliseconds,7:N0} ms   ({Rate(rows, swS.ElapsedMilliseconds)})");

        // ---- Sharded: N DBs written in parallel, contiguous Id ranges ----
        var shardPaths = Enumerable.Range(0, shards).Select(k => NewDb($"s{k}")).ToArray();
        long per = (rows + shards - 1) / shards;
        var swP = Stopwatch.StartNew();
        Parallel.For(0, shards, k =>
        {
            long lo = k * per, hi = Math.Min(lo + per, rows);
            using var c = new SqliteConnection($"Data Source={shardPaths[k]}");
            c.Open(); Pragmas(c); CreateTable(c);
            InsertRange(c, lo, hi);
        });
        swP.Stop();
        TestContext.WriteLine($"PARALLEL ins  : {swP.ElapsedMilliseconds,7:N0} ms   ({Rate(rows, swP.ElapsedMilliseconds)})   speedup={(double)swS.ElapsedMilliseconds / Math.Max(1, swP.ElapsedMilliseconds):F2}x");

        // ---- Alternative architecture: parallel-insert into shards, then MERGE into ONE DB via
        //      INSERT…SELECT (SQLite-internal row copy, no managed binding). Keeps queries single-DB
        //      fast while parallelising the expensive managed bind. The merge cost decides viability. ----
        var mergedPath = NewDb("merged");
        var swM = Stopwatch.StartNew();
        using (var c = new SqliteConnection($"Data Source={mergedPath}"))
        {
            c.Open(); Pragmas(c); CreateTable(c);
            for (int k = 0; k < shards; k++)
                using (var a = c.CreateCommand()) { a.CommandText = $"ATTACH DATABASE '{shardPaths[k].Replace("'", "''")}' AS s{k};"; a.ExecuteNonQuery(); }
            using var tx = c.BeginTransaction();
            for (int k = 0; k < shards; k++)
                using (var ins = c.CreateCommand()) { ins.Transaction = tx; ins.CommandText = $"INSERT INTO main.T SELECT * FROM s{k}.T"; ins.ExecuteNonQuery(); }
            tx.Commit();
        }
        swM.Stop();
        long parMerge = swP.ElapsedMilliseconds + swM.ElapsedMilliseconds;
        TestContext.WriteLine($"MERGE shards->1: {swM.ElapsedMilliseconds,7:N0} ms   →  parallel+merge total {parMerge:N0} ms vs single {swS.ElapsedMilliseconds:N0} ms   (net {(double)swS.ElapsedMilliseconds / Math.Max(1, parMerge):F2}x, then queries stay single-DB)");
        using (var mc = new SqliteConnection($"Data Source={mergedPath};Mode=ReadOnly")) { mc.Open(); Assert.AreEqual((long)rows, Count(mc, "T", null), "merged-into-one DB row count"); }

        // ---- Query: single DB vs ATTACH+UNION ALL across shards ----
        using var qs = new SqliteConnection($"Data Source={singlePath};Mode=ReadOnly"); qs.Open();
        using var qm = new SqliteConnection($"Data Source={shardPaths[0]}"); qm.Open();
        for (int k = 1; k < shards; k++)
            using (var a = qm.CreateCommand()) { a.CommandText = $"ATTACH DATABASE '{shardPaths[k].Replace("'", "''")}' AS s{k};"; a.ExecuteNonQuery(); }
        // s0 is the main schema of qm ("main"); s1..s(N-1) attached. Build the union over all shards.
        string union = "SELECT Id,Ticks,Level,Message FROM main.T"
            + string.Concat(Enumerable.Range(1, shards - 1).Select(k => $" UNION ALL SELECT Id,Ticks,Level,Message FROM s{k}.T"));

        // Correctness: total + filtered count, and several pages across sort orders, must match single DB.
        long cS = Count(qs, "T", null), cM = Count(qm, $"({union})", null);
        Assert.AreEqual(rows, cS, "single count"); Assert.AreEqual(cS, cM, "merged count must match single");
        long fS = Count(qs, "T", "Level=2"), fM = Count(qm, $"({union})", "Level=2");
        Assert.AreEqual(fS, fM, "merged filtered count must match single");

        foreach (var (orderBy, desc) in new[] { ("Id", false), ("Id", true), ("Ticks", false) })
            foreach (var off in new[] { 0, 1000, rows - 50 })
            {
                var a = Page(qs, "T", null, orderBy, desc, 50, off);
                var b = Page(qm, $"({union})", null, orderBy, desc, 50, off);
                CollectionAssert.AreEqual(a, b, $"page mismatch order={orderBy} desc={desc} off={off}");
            }
        // Filtered + sorted page too.
        CollectionAssert.AreEqual(Page(qs, "T", "Level=2", "Id", false, 50, 500),
                                  Page(qm, $"({union})", "Level=2", "Id", false, 50, 500), "filtered page mismatch");
        TestContext.WriteLine("merged query == single across count/filter/pages/sorts ✓");

        // Latency: count, first page, deep page (offset scans the whole union), filtered page.
        var t1 = Time(() => Count(qm, $"({union})", null));
        var t2 = Time(() => Page(qm, $"({union})", null, "Id", false, 100, 0));
        var t3 = Time(() => Page(qm, $"({union})", null, "Id", false, 100, rows - 100));
        var t4 = Time(() => Page(qm, $"({union})", "Level=2", "Ticks", true, 100, 0));
        TestContext.WriteLine($"merged latency: count {t1} ms · first page {t2} ms · deep page (off {rows - 100:N0}) {t3} ms · filtered+sorted {t4} ms");
    }

    private static long Count(SqliteConnection c, string from, string where)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {from}" + (where != null ? $" WHERE {where}" : "");
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static List<long> Page(SqliteConnection c, string from, string where, string orderBy, bool desc, int limit, long offset)
    {
        var list = new List<long>(limit);
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT Id FROM {from}" + (where != null ? $" WHERE {where}" : "")
            + $" ORDER BY {orderBy} {(desc ? "DESC" : "ASC")}, Id {(desc ? "DESC" : "ASC")} LIMIT @lim OFFSET @off";
        cmd.Parameters.AddWithValue("@lim", limit);
        cmd.Parameters.AddWithValue("@off", offset);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetInt64(0));
        return list;
    }

    // ---- Wide-row variant: 21 columns with ETL-realistic sizes (long message + JSON StructuredData),
    //      so the insert cost matches production (~128k rows/s) rather than the trivial 4-col table.
    //      Validates that parallel-insert + merge-back scaling holds for the real ETL row shape. ----
    private static void CreateWideTable(SqliteConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "CREATE TABLE T(Id INTEGER PRIMARY KEY, LogTime TEXT, MachineName TEXT, Level INTEGER, " +
            "Username TEXT, TaskName TEXT, OpCode TEXT, Source TEXT, SearchableData TEXT, Message TEXT, ResultSource TEXT, " +
            "ProcessId TEXT, ThreadId TEXT, ActivityId TEXT, EventId TEXT, Keywords TEXT, RelatedActivityId TEXT, " +
            "Channel TEXT, ProviderGuid TEXT, RecordId TEXT, ProcessName TEXT, StructuredData TEXT);";
        cmd.ExecuteNonQuery();
    }

    private static void InsertWideRange(SqliteConnection conn, long lo, long hi)
    {
        using var tx = conn.BeginTransaction();
        using var ins = conn.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = "INSERT INTO T VALUES(@Id,@LogTime,@MachineName,@Level,@Username,@TaskName,@OpCode,@Source," +
            "@SearchableData,@Message,@ResultSource,@ProcessId,@ThreadId,@ActivityId,@EventId,@Keywords,@RelatedActivityId," +
            "@Channel,@ProviderGuid,@RecordId,@ProcessName,@StructuredData)";
        SqliteParameter P(string n) { var p = ins.CreateParameter(); p.ParameterName = n; ins.Parameters.Add(p); return p; }
        var pid = P("@Id"); var plt = P("@LogTime"); var pmn = P("@MachineName"); var plv = P("@Level");
        var pun = P("@Username"); var ptn = P("@TaskName"); var poc = P("@OpCode"); var psr = P("@Source");
        var psd = P("@SearchableData"); var pmsg = P("@Message"); var prs = P("@ResultSource"); var ppid = P("@ProcessId");
        var ptid = P("@ThreadId"); var pact = P("@ActivityId"); var pev = P("@EventId"); var pkw = P("@Keywords");
        var prel = P("@RelatedActivityId"); var pch = P("@Channel"); var ppg = P("@ProviderGuid"); var prec = P("@RecordId");
        var ppn = P("@ProcessName"); var pstr = P("@StructuredData");
        ins.Prepare();
        for (long i = lo; i < hi; i++)
        {
            pid.Value = i;
            plt.Value = "2026-01-01T00:00:00.0000000Z";
            pmn.Value = "MACHINE01"; plv.Value = (int)(i % 6); pun.Value = ""; ptn.Value = "MyProvider/MyTask";
            poc.Value = "Info"; psr.Value = "Microsoft-Windows-MyProvider"; prs.Value = "ETW: trace.etl";
            var msg = $"MyProvider/MyTask == requestId=\"req-{i % 9973}\" durationMs=\"{i % 1500}\" status=\"200\" node=\"node-{i % 64}.dc{i % 8}\" user=\"user{i % 4096}\"";
            pmsg.Value = msg; psd.Value = msg; // SearchableData==Message (deduped at FTS, but stored both here)
            ppid.Value = (i % 60000).ToString("X"); ptid.Value = (i % 9000).ToString("X");
            pact.Value = ""; pev.Value = (i % 200).ToString(); pkw.Value = ""; prel.Value = ""; pch.Value = "";
            ppg.Value = "{11111111-2222-3333-4444-555555555555}"; prec.Value = ""; ppn.Value = "myservice.exe";
            pstr.Value = $"{{\"requestId\":\"req-{i % 9973}\",\"durationMs\":\"{i % 1500}\",\"status\":\"200\",\"node\":\"node-{i % 64}.dc{i % 8}\",\"user\":\"user{i % 4096}\"}}";
            ins.ExecuteNonQuery();
        }
        tx.Commit();
    }

    [TestMethod]
    [Timeout(900_000)]
    public void WideRow_ParallelInsert_AndMerge_EtlShape()
    {
        int rows = EnvInt("FINDNEEDLE_SPIKE_ROWS", 5_000_000);
        int shards = EnvInt("FINDNEEDLE_SPIKE_SHARDS", 8);

        var singlePath = NewDb("wsingle");
        var swS = Stopwatch.StartNew();
        using (var c = new SqliteConnection($"Data Source={singlePath}")) { c.Open(); Pragmas(c); CreateWideTable(c); InsertWideRange(c, 0, rows); }
        swS.Stop();
        TestContext.WriteLine($"WIDE rows={rows:N0} shards={shards}");
        TestContext.WriteLine($"SINGLE wide insert : {swS.ElapsedMilliseconds,7:N0} ms   ({Rate(rows, swS.ElapsedMilliseconds)})");

        var shardPaths = Enumerable.Range(0, shards).Select(k => NewDb($"ws{k}")).ToArray();
        long per = (rows + shards - 1) / shards;
        var swP = Stopwatch.StartNew();
        Parallel.For(0, shards, k =>
        {
            long lo = k * per, hi = Math.Min(lo + per, rows);
            using var c = new SqliteConnection($"Data Source={shardPaths[k]}");
            c.Open(); Pragmas(c); CreateWideTable(c); InsertWideRange(c, lo, hi);
        });
        swP.Stop();

        var mergedPath = NewDb("wmerged");
        var swM = Stopwatch.StartNew();
        using (var c = new SqliteConnection($"Data Source={mergedPath}"))
        {
            c.Open(); Pragmas(c); CreateWideTable(c);
            for (int k = 0; k < shards; k++)
                using (var a = c.CreateCommand()) { a.CommandText = $"ATTACH DATABASE '{shardPaths[k].Replace("'", "''")}' AS s{k};"; a.ExecuteNonQuery(); }
            using var tx = c.BeginTransaction();
            for (int k = 0; k < shards; k++)
                using (var insm = c.CreateCommand()) { insm.Transaction = tx; insm.CommandText = $"INSERT INTO main.T SELECT * FROM s{k}.T"; insm.ExecuteNonQuery(); }
            tx.Commit();
        }
        swM.Stop();
        long parMerge = swP.ElapsedMilliseconds + swM.ElapsedMilliseconds;
        TestContext.WriteLine($"PARALLEL wide ins  : {swP.ElapsedMilliseconds,7:N0} ms   ({Rate(rows, swP.ElapsedMilliseconds)})   speedup={(double)swS.ElapsedMilliseconds / Math.Max(1, swP.ElapsedMilliseconds):F2}x");
        TestContext.WriteLine($"MERGE wide shards→1: {swM.ElapsedMilliseconds,7:N0} ms   →  parallel+merge total {parMerge:N0} ms vs single {swS.ElapsedMilliseconds:N0} ms   (net {(double)swS.ElapsedMilliseconds / Math.Max(1, parMerge):F2}x, queries stay single-DB)");
        using (var mc = new SqliteConnection($"Data Source={mergedPath};Mode=ReadOnly")) { mc.Open(); Assert.AreEqual((long)rows, Count(mc, "T", null), "merged wide row count"); }
    }

    private static long Time(Action a) { var sw = Stopwatch.StartNew(); a(); sw.Stop(); return sw.ElapsedMilliseconds; }
    private static string Rate(long rows, long ms) => ms > 0 ? $"{rows * 1000L / ms:N0} rows/s" : "n/a";
    private static int EnvInt(string n, int d) => int.TryParse(Environment.GetEnvironmentVariable(n), out var v) && v > 0 ? v : d;
}
