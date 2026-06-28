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

    private static void CreateContentlessFts(SqliteConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "CREATE VIRTUAL TABLE fts USING fts5(Message, content='', tokenize='trigram');";
        cmd.ExecuteNonQuery();
    }

    /// <summary>Index rows [lo,hi) read from the main DB into this connection's contentless fts (rowid=Id).</summary>
    private static void BuildShardFromMain(SqliteConnection ftsConn, string mainPath, long lo, long hi)
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
        ins.CommandText = "INSERT INTO fts(rowid, Message) VALUES(@r,@m)";
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
