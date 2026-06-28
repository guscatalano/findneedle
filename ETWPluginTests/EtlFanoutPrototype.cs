using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using FindNeedleCoreUtils;
using FindNeedlePluginLib;
using FindPluginCore.Implementations.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Diagnostics.Tracing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ETWPluginTests;

/// <summary>
/// PROTOTYPE (measurement only): wire the row-sharding fan-out end-to-end through the REAL ETL decode +
/// REAL SqliteStorage insert, and compare to the serial baseline — to confirm the modelled ~3x before any
/// production wiring, and to surface real-world issues (producer rate, backpressure, the merge of real
/// 21-column storage DBs).
///
/// SERIAL : decode → wrap (ETLLogLine) → SqliteStorage.AddFilteredBatch (one writer).
/// PARALLEL: producer decodes+wraps on its thread (TraceEvent objects are only valid in-callback) and
///           feeds a bounded channel; N consumers each drain it into their own shard SqliteStorage; then
///           the shards merge into one DB via INSERT…SELECT. Queries would then run on the single merged DB.
/// FTS is disabled here (DisableFtsForMeasurement) so we isolate the ingest insert.
/// </summary>
[TestClass]
[TestCategory("Performance")]
[TestCategory("SkipCI")]
public class EtlFanoutPrototype
{
    public TestContext TestContext { get; set; }
    private readonly List<string> _cacheDbs = new();

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        SqliteStorage.DisableFtsForMeasurement = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FINDNEEDLE_DISABLE_FTS"));
        foreach (var f in _cacheDbs)
            foreach (var p in new[] { f, f + "-wal", f + "-shm", f + "-journal" })
                try { if (File.Exists(p)) File.Delete(p); } catch { }
    }

    private SqliteStorage NewShard(string tag)
    {
        var searched = Path.Combine(Path.GetTempPath(), $"fanout_{tag}_{Guid.NewGuid():N}");
        _cacheDbs.Add(CachedStorage.GetCacheFilePath(searched, ".db"));
        var s = new SqliteStorage(searched);
        s.ClearTables();
        return s;
    }

    private static string FindEtl()
    {
        var env = Environment.GetEnvironmentVariable("FINDNEEDLE_ETL");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            var cand = Path.Combine(dir, "LargeSamples", "large-5M.etl");
            if (File.Exists(cand)) return cand;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

    [TestMethod]
    [Timeout(600_000)]
    public void Fanout_EndToEnd_vs_Serial()
    {
        var etl = FindEtl();
        if (etl == null) { Assert.Inconclusive("No ETL (set FINDNEEDLE_ETL or place LargeSamples\\large-5M.etl)."); return; }
        int shards = int.TryParse(Environment.GetEnvironmentVariable("FINDNEEDLE_SPIKE_SHARDS"), out var s) && s > 0 ? s : 8;
        SqliteStorage.DisableFtsForMeasurement = true;
        SqliteStorage.FtsShardThreshold = int.MaxValue;
        TestContext.WriteLine($"file: {Path.GetFileName(etl)} ({new FileInfo(etl).Length / 1024.0 / 1024.0:N0} MB), shards={shards}");

        // ---- SERIAL baseline: decode → wrap → single-writer insert ----
        long serialCount = 0;
        var serial = NewShard("serial");
        var swSerial = Stopwatch.StartNew();
        DecodeWrapped(etl, batch => { serial.AddFilteredBatch(batch); serialCount += batch.Count; });
        swSerial.Stop();
        serial.Dispose();
        TestContext.WriteLine($"SERIAL decode+wrap+insert : {swSerial.ElapsedMilliseconds,7:N0} ms   ({serialCount:N0} rows, {Rate(serialCount, swSerial.ElapsedMilliseconds)})");

        // ---- PARALLEL: producer decode+wrap → bounded channel → N consumer shard writers ----
        var shardStores = Enumerable.Range(0, shards).Select(k => NewShard($"s{k}")).ToArray();
        var shardDbs = _cacheDbs.Skip(1).Take(shards).ToArray(); // the shard cache DBs (after 'serial')
        var ch = Channel.CreateBounded<List<ISearchResult>>(new BoundedChannelOptions(4 * shards) { SingleWriter = true });
        var consumers = Enumerable.Range(0, shards).Select(k => Task.Run(async () =>
        {
            await foreach (var batch in ch.Reader.ReadAllAsync())
                shardStores[k].AddFilteredBatch(batch);   // each consumer owns one shard
        })).ToArray();

        var swPar = Stopwatch.StartNew();
        long prodCount = 0;
        DecodeWrapped(etl, batch => { ch.Writer.WriteAsync(batch).AsTask().GetAwaiter().GetResult(); prodCount += batch.Count; });
        ch.Writer.Complete();
        Task.WaitAll(consumers);
        long phaseMs = swPar.ElapsedMilliseconds; // decode+wrap+parallel-insert (overlapped)

        // ---- Merge shards → one DB via INSERT…SELECT (the queryable result) ----
        var finalSearched = Path.Combine(Path.GetTempPath(), $"fanout_final_{Guid.NewGuid():N}");
        var finalDb = CachedStorage.GetCacheFilePath(finalSearched, ".db");
        _cacheDbs.Add(finalDb);
        foreach (var st in shardStores) st.Dispose();   // release file handles before ATTACH
        SqliteConnection.ClearAllPools();
        var swMerge = Stopwatch.StartNew();
        long mergedCount = MergeShards(shardDbs[0], shardDbs, finalDb);
        swMerge.Stop();
        long total = phaseMs + swMerge.ElapsedMilliseconds;

        TestContext.WriteLine($"PARALLEL phase (decode+wrap+insert, overlapped) : {phaseMs,7:N0} ms   ({prodCount:N0} rows)");
        TestContext.WriteLine($"MERGE shards→1 (INSERT…SELECT)                  : {swMerge.ElapsedMilliseconds,7:N0} ms");
        TestContext.WriteLine($"PARALLEL+MERGE total                           : {total,7:N0} ms   vs SERIAL {swSerial.ElapsedMilliseconds:N0} ms   →  {(double)swSerial.ElapsedMilliseconds / Math.Max(1, total):F2}x");

        Assert.AreEqual(serialCount, prodCount, "produced row count matches serial");
        Assert.AreEqual(serialCount, mergedCount, "merged row count matches serial");
    }

    /// <summary>Decode the .etl, wrap each event into an ETLLogLine on THIS thread (TraceEvent is only
    /// valid in-callback), batch to 5000, and hand each batch to <paramref name="onBatch"/>.</summary>
    private static void DecodeWrapped(string etl, Action<List<ISearchResult>> onBatch)
    {
        var batch = new List<ISearchResult>(5000);
        using var source = new ETWTraceEventSource(etl);
        void H(TraceEvent e)
        {
            batch.Add(new ETLLogLine(e));
            if (batch.Count >= 5000) { onBatch(batch); batch = new List<ISearchResult>(5000); }
        }
        source.Dynamic.All += H;
        source.Kernel.All += H;
        source.Process();
        if (batch.Count > 0) onBatch(batch);
    }

    /// <summary>Create the final DB with the same FilteredResults schema as <paramref name="schemaFrom"/>,
    /// ATTACH each shard, and INSERT…SELECT all rows (re-assigning Id) into it. Returns the merged count.</summary>
    private static long MergeShards(string schemaFrom, string[] shardDbs, string finalDb)
    {
        using var fin = new SqliteConnection($"Data Source={finalDb}");
        fin.Open();
        using (var p = fin.CreateCommand()) { p.CommandText = "PRAGMA journal_mode=MEMORY; PRAGMA synchronous=OFF; PRAGMA temp_store=MEMORY; PRAGMA cache_size=-65536;"; p.ExecuteNonQuery(); }

        // Recreate the FilteredResults table from a shard's schema.
        string createSql;
        using (var src = new SqliteConnection($"Data Source={schemaFrom};Mode=ReadOnly"))
        {
            src.Open();
            using var q = src.CreateCommand();
            q.CommandText = "SELECT sql FROM sqlite_master WHERE name='FilteredResults' AND type='table'";
            createSql = (string)q.ExecuteScalar();
        }
        using (var c = fin.CreateCommand()) { c.CommandText = createSql; c.ExecuteNonQuery(); }

        // Column list excluding the autoincrement Id (let the merged table assign fresh ids).
        var cols = new List<string>();
        using (var c = fin.CreateCommand())
        {
            c.CommandText = "PRAGMA table_info(FilteredResults)";
            using var r = c.ExecuteReader();
            while (r.Read()) { var name = r.GetString(1); if (!string.Equals(name, "Id", StringComparison.OrdinalIgnoreCase)) cols.Add(name); }
        }
        string colList = string.Join(",", cols);

        using var tx = fin.BeginTransaction();
        for (int k = 0; k < shardDbs.Length; k++)
        {
            using (var a = fin.CreateCommand()) { a.CommandText = $"ATTACH DATABASE '{shardDbs[k].Replace("'", "''")}' AS sh{k};"; a.ExecuteNonQuery(); }
            using (var ins = fin.CreateCommand()) { ins.Transaction = tx; ins.CommandText = $"INSERT INTO main.FilteredResults ({colList}) SELECT {colList} FROM sh{k}.FilteredResults"; ins.ExecuteNonQuery(); }
        }
        tx.Commit();

        using var cnt = fin.CreateCommand();
        cnt.CommandText = "SELECT COUNT(*) FROM FilteredResults";
        return Convert.ToInt64(cnt.ExecuteScalar());
    }

    private static string Rate(long n, long ms) => ms > 0 ? $"{n * 1000L / ms:N0} rows/s" : "n/a";
}
