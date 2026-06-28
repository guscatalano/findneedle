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
/// SPIKE (measurement only — does not touch the production search path): prove that the FTS trigram
/// build can be parallelized by sharding the index into N separate DB files built concurrently, and
/// that a MATCH across the shards returns the same rowids as a single index. This is format-agnostic —
/// it indexes already-normalized rows, so it would apply to evtx/etl/zip/text alike.
///
/// Rows live in one "main" DB (paging/sort/count would stay there, unchanged). Each shard is a
/// CONTENTLESS fts5 table (stores only the inverted index + rowid, not the text) in its own DB file, so
/// the N builds have independent writers and run in parallel. MATCH returns rowids; the real row is then
/// fetched from the main table by Id — exactly how the production query already uses the FTS.
///
/// Tunable via env: FINDNEEDLE_SPIKE_ROWS (default 2,000,000), FINDNEEDLE_SPIKE_SHARDS (default 4).
/// </summary>
[TestClass]
[TestCategory("Performance")]
public class FtsShardSpike
{
    private readonly List<string> _files = new();
    public TestContext TestContext { get; set; }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in _files) { try { if (File.Exists(f)) File.Delete(f); } catch { } }
    }

    private string NewDbPath(string tag)
    {
        var p = Path.Combine(Path.GetTempPath(), $"ftsspike_{tag}_{Guid.NewGuid():N}.db");
        _files.Add(p);
        return p;
    }

    private static void Pragmas(SqliteConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=MEMORY; PRAGMA synchronous=OFF; PRAGMA temp_store=MEMORY; PRAGMA cache_size=-65536;";
        cmd.ExecuteNonQuery();
    }

    [TestMethod]
    [Timeout(900_000)]
    public void ParallelShardBuild_IsFasterAndMatchesSingleIndex()
    {
        int rows = EnvInt("FINDNEEDLE_SPIKE_ROWS", 2_000_000);
        int shards = EnvInt("FINDNEEDLE_SPIKE_SHARDS", 4);
        // A term that occurs in exactly one row's message (i printed in "line number {i}"), within range.
        int needle = rows / 2 + 7;

        // ---- Build the rows-only main DB (this is what production paging/sort/count read from) ----
        var mainPath = NewDbPath("main");
        var genSw = Stopwatch.StartNew();
        using (var main = new SqliteConnection($"Data Source={mainPath}"))
        {
            main.Open();
            Pragmas(main);
            using (var c = main.CreateCommand()) { c.CommandText = "CREATE TABLE main(Id INTEGER PRIMARY KEY, Message TEXT);"; c.ExecuteNonQuery(); }
            using var tx = main.BeginTransaction();
            using var ins = main.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO main(Id, Message) VALUES(@id,@m)";
            var pid = ins.CreateParameter(); pid.ParameterName = "@id"; ins.Parameters.Add(pid);
            var pm = ins.CreateParameter(); pm.ParameterName = "@m"; ins.Parameters.Add(pm);
            ins.Prepare();
            for (int i = 0; i < rows; i++)
            {
                pid.Value = i;
                pm.Value = $"[2026-01-01 00:00:00] INFO: scroll test message line number {i} with some extra padding text to reach a realistic width";
                ins.ExecuteNonQuery();
            }
            tx.Commit();
        }
        genSw.Stop();
        TestContext.WriteLine($"rows={rows:N0} shards={shards} | main DB built in {genSw.ElapsedMilliseconds:N0} ms");

        // ---- Baseline: one contentless FTS index, built single-threaded from the main DB ----
        var singlePath = NewDbPath("single");
        var singleSw = Stopwatch.StartNew();
        using (var fts = new SqliteConnection($"Data Source={singlePath}"))
        {
            fts.Open();
            Pragmas(fts);
            CreateContentlessFts(fts);
            BuildShardFromMain(fts, mainPath, 0, rows); // whole range
        }
        singleSw.Stop();
        var singleHits = MatchRowids(singlePath, needle);
        TestContext.WriteLine($"SINGLE  build: {singleSw.ElapsedMilliseconds,8:N0} ms   (hits={singleHits.Count})");

        // ---- Parallel: N shards, each its own DB file + writer, built concurrently ----
        var shardPaths = Enumerable.Range(0, shards).Select(k => NewDbPath($"shard{k}")).ToArray();
        int chunk = (rows + shards - 1) / shards;
        var parSw = Stopwatch.StartNew();
        Parallel.For(0, shards, k =>
        {
            long lo = (long)k * chunk;
            long hi = Math.Min(lo + chunk, rows);
            using var fts = new SqliteConnection($"Data Source={shardPaths[k]}");
            fts.Open();
            Pragmas(fts);
            CreateContentlessFts(fts);
            BuildShardFromMain(fts, mainPath, lo, hi); // reads its range from the main DB (concurrent readers OK)
        });
        parSw.Stop();

        // Union the per-shard MATCH rowids (production would ATTACH + UNION; C#-union proves equivalence).
        var shardHits = new HashSet<long>();
        foreach (var sp in shardPaths) shardHits.UnionWith(MatchRowids(sp, needle));
        TestContext.WriteLine($"PARALLEL build: {parSw.ElapsedMilliseconds,8:N0} ms   (hits={shardHits.Count})  " +
            $"speedup={(double)singleSw.ElapsedMilliseconds / Math.Max(1, parSw.ElapsedMilliseconds):F2}x");

        // ---- Correctness: the sharded index must return exactly the same rowids as the single index ----
        Assert.IsTrue(singleHits.SetEquals(shardHits),
            $"sharded MATCH rowids differ from single index (single={singleHits.Count}, sharded={shardHits.Count})");
        Assert.IsTrue(singleHits.Contains(needle), $"the needle row {needle} should match");
        Assert.AreEqual(1, singleHits.Count, "needle term should be unique to one row");

        // The point of the spike: parallel should be meaningfully faster on a big set.
        if (rows >= 1_000_000)
            Assert.IsTrue(parSw.ElapsedMilliseconds < singleSw.ElapsedMilliseconds,
                $"parallel ({parSw.ElapsedMilliseconds} ms) should beat single ({singleSw.ElapsedMilliseconds} ms)");
    }

    /// <summary>
    /// SPIKE: prove the QUERY side of sharding — ATTACH N shard DBs to the main connection and union the
    /// per-shard MATCH, then run the real filtered/sorted/paged query (the production shape:
    /// <c>SELECT … FROM main WHERE Id IN (&lt;fts rowids&gt;) ORDER BY … LIMIT … OFFSET …</c>) plus the
    /// pager COUNT. Assert the sharded results are byte-identical to a single-index query across pages and
    /// both sort directions, and measure latency. This de-risks the query-layer change before productionizing.
    /// </summary>
    [TestMethod]
    [Timeout(900_000)]
    public void ShardedQuery_MatchesSingleIndex_AcrossPagesSortsAndCount()
    {
        int rows = EnvInt("FINDNEEDLE_SPIKE_ROWS", 2_000_000);
        int shards = EnvInt("FINDNEEDLE_SPIKE_SHARDS", 4);
        int needle = rows / 2 + 7;

        // main DB (rows) — paging/sort/count run here in production, unchanged.
        var mainPath = NewDbPath("qmain");
        BuildMain(mainPath, rows);

        // single index (baseline) + N parallel shards.
        var singlePath = NewDbPath("qsingle");
        using (var fts = new SqliteConnection($"Data Source={singlePath}")) { fts.Open(); Pragmas(fts); CreateContentlessFts(fts, "ftsS"); BuildShardFromMain(fts, mainPath, 0, rows, "ftsS"); }
        var shardPaths = Enumerable.Range(0, shards).Select(k => NewDbPath($"qshard{k}")).ToArray();
        int chunk = (rows + shards - 1) / shards;
        Parallel.For(0, shards, k =>
        {
            long lo = (long)k * chunk, hi = Math.Min(lo + chunk, rows);
            using var fts = new SqliteConnection($"Data Source={shardPaths[k]}");
            fts.Open(); Pragmas(fts); CreateContentlessFts(fts, $"fts{k}"); BuildShardFromMain(fts, mainPath, lo, hi, $"fts{k}");
        });

        using var q = new SqliteConnection($"Data Source={mainPath}");
        q.Open();
        Attach(q, singlePath, "single");
        for (int k = 0; k < shards; k++) Attach(q, shardPaths[k], $"s{k}");

        // Production-shaped FTS rowid subqueries: one index vs UNION ALL across shards (Id ranges are
        // disjoint, so ALL has no dupes and is cheaper than UNION).
        // FTS5 MATCH needs the bare table name (no schema qualifier, no alias), so each shard's table has
        // a globally-unique name (ftsS / fts0 / fts1 …); FROM picks the attached schema, MATCH names the table.
        string singleSub = "SELECT rowid FROM single.ftsS WHERE ftsS MATCH @q";
        string shardSub = string.Join(" UNION ALL ", Enumerable.Range(0, shards).Select(k => $"SELECT rowid FROM s{k}.fts{k} WHERE fts{k} MATCH @q"));

        // "scroll test" is in every row (exercises COUNT=all + deep paging); the needle hits exactly one row.
        foreach (var term in new[] { "scroll test", $"line number {needle} " })
        {
            long cSingle = CountMatch(q, singleSub, term);
            long cShard = CountMatch(q, shardSub, term);
            Assert.AreEqual(cSingle, cShard, $"COUNT mismatch for '{term}' (single={cSingle}, sharded={cShard})");

            foreach (var desc in new[] { false, true })
                foreach (var off in new[] { 0, 100, (int)Math.Max(0, cSingle - 50) })
                {
                    var a = PageMatch(q, singleSub, term, 50, off, desc);
                    var b = PageMatch(q, shardSub, term, 50, off, desc);
                    CollectionAssert.AreEqual(a, b, $"page mismatch term='{term}' desc={desc} off={off}");
                }
            TestContext.WriteLine($"term='{term}' count={cSingle:N0} — single==sharded across pages & sorts ✓");
        }

        // Latency: a first page and a deep page over the all-matching term, via the sharded union.
        var warm = PageMatch(q, shardSub, "scroll test", 100, 0, false);
        var sw1 = Stopwatch.StartNew(); _ = PageMatch(q, shardSub, "scroll test", 100, 0, false); sw1.Stop();
        long deep = Math.Max(0, rows - 100);
        var sw2 = Stopwatch.StartNew(); _ = PageMatch(q, shardSub, "scroll test", 100, (int)deep, false); sw2.Stop();
        TestContext.WriteLine($"sharded query latency: first page {sw1.ElapsedMilliseconds} ms, deep page (offset {deep:N0}) {sw2.ElapsedMilliseconds} ms");
        Assert.IsTrue(warm.Count == 100, "first page should return a full page");
    }

    private void BuildMain(string mainPath, int rows)
    {
        using var main = new SqliteConnection($"Data Source={mainPath}");
        main.Open();
        Pragmas(main);
        using (var c = main.CreateCommand()) { c.CommandText = "CREATE TABLE main(Id INTEGER PRIMARY KEY, Message TEXT);"; c.ExecuteNonQuery(); }
        using var tx = main.BeginTransaction();
        using var ins = main.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = "INSERT INTO main(Id, Message) VALUES(@id,@m)";
        var pid = ins.CreateParameter(); pid.ParameterName = "@id"; ins.Parameters.Add(pid);
        var pm = ins.CreateParameter(); pm.ParameterName = "@m"; ins.Parameters.Add(pm);
        ins.Prepare();
        for (int i = 0; i < rows; i++)
        {
            pid.Value = i;
            pm.Value = $"[2026-01-01 00:00:00] INFO: scroll test message line number {i} with some extra padding text to reach a realistic width";
            ins.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static void Attach(SqliteConnection conn, string path, string alias)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"ATTACH DATABASE '{path.Replace("'", "''")}' AS {alias};";
        cmd.ExecuteNonQuery();
    }

    private static long CountMatch(SqliteConnection conn, string ftsSub, string term)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM main WHERE Id IN ({ftsSub})";
        cmd.Parameters.AddWithValue("@q", $"\"{term}\"");
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static List<long> PageMatch(SqliteConnection conn, string ftsSub, string term, int limit, int offset, bool desc)
    {
        var list = new List<long>(limit);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT Id FROM main WHERE Id IN ({ftsSub}) ORDER BY Id {(desc ? "DESC" : "ASC")} LIMIT @lim OFFSET @off";
        cmd.Parameters.AddWithValue("@q", $"\"{term}\"");
        cmd.Parameters.AddWithValue("@lim", limit);
        cmd.Parameters.AddWithValue("@off", offset);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetInt64(0));
        return list;
    }

    // A unique table name per shard so the bare-name MATCH operand is unambiguous once the shards are
    // ATTACHed together (FTS5 MATCH won't take a schema-qualified name or an alias).
    private static void CreateContentlessFts(SqliteConnection c, string name = "fts")
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"CREATE VIRTUAL TABLE {name} USING fts5(Message, content='', tokenize='trigram');";
        cmd.ExecuteNonQuery();
    }

    /// <summary>Index rows [lo,hi) read from the main DB into this connection's contentless fts (rowid=Id).</summary>
    private static void BuildShardFromMain(SqliteConnection ftsConn, string mainPath, long lo, long hi, string ftsTable = "fts")
    {
        using var src = new SqliteConnection($"Data Source={mainPath};Mode=ReadOnly");
        src.Open();
        using var read = src.CreateCommand();
        read.CommandText = "SELECT Id, Message FROM main WHERE Id >= @lo AND Id < @hi";
        read.Parameters.AddWithValue("@lo", lo);
        read.Parameters.AddWithValue("@hi", hi);

        using var tx = ftsConn.BeginTransaction();
        using var ins = ftsConn.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = $"INSERT INTO {ftsTable}(rowid, Message) VALUES(@r,@m)";
        var pr = ins.CreateParameter(); pr.ParameterName = "@r"; ins.Parameters.Add(pr);
        var pm = ins.CreateParameter(); pm.ParameterName = "@m"; ins.Parameters.Add(pm);
        ins.Prepare();
        using (var r = read.ExecuteReader())
            while (r.Read())
            {
                pr.Value = r.GetInt64(0);
                pm.Value = r.GetString(1);
                ins.ExecuteNonQuery();
            }
        tx.Commit();
    }

    private static HashSet<long> MatchRowids(string ftsPath, int needle)
    {
        var set = new HashSet<long>();
        using var c = new SqliteConnection($"Data Source={ftsPath};Mode=ReadOnly");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT rowid FROM fts WHERE fts MATCH @q";
        cmd.Parameters.AddWithValue("@q", $"\"line number {needle} \""); // quoted phrase → trigram substring
        using var r = cmd.ExecuteReader();
        while (r.Read()) set.Add(r.GetInt64(0));
        return set;
    }

    private static int EnvInt(string name, int dflt)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var v) && v > 0 ? v : dflt;
}
