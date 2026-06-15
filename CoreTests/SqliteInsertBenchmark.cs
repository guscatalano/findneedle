using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using FindPluginCore.Implementations.Storage;
using FindNeedleCoreUtils;
using FindNeedlePluginLib;
using FindNeedlePluginLib.Interfaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

/// <summary>
/// GUI-free micro-benchmark isolating storage insert throughput. The UI tests showed ~45s to ingest
/// 200k rows into SQLite while in-memory did it in ~0.3s, and that FTS was NOT the cause. This runs
/// the same insert path directly (no scan, no double-write, no UI) so we can see SQLite's real
/// per-row insert cost and iterate on it with a fast feedback loop (dotnet test, seconds).
/// </summary>
[TestClass]
[DoNotParallelize]
public class SqliteInsertBenchmark
{
    public TestContext TestContext { get; set; } = null!;

    private const int RowCount = 200_000;

    private readonly List<string> _dbPaths = new();

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var p in _dbPaths)
            try { if (File.Exists(p)) File.Delete(p); } catch { }
        SqliteStorage.DisableFtsForMeasurement =
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FINDNEEDLE_DISABLE_FTS"));
    }

    /// <summary>Realistic ~120-char, unique-per-row log lines so string lengths match real logs.</summary>
    private static List<ISearchResult> MakeRows(int n)
    {
        var list = new List<ISearchResult>(n);
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < n; i++)
            list.Add(new BenchResult(
                t.AddSeconds(i),
                $"[{t.AddSeconds(i):yyyy-MM-dd HH:mm:ss}] INFO: scroll test message line number {i} with some extra padding text to reach a realistic width"));
        return list;
    }

    private SqliteStorage NewSqlite()
    {
        var searchedFile = Path.Combine(Path.GetTempPath(), "bench_" + Guid.NewGuid().ToString("N"));
        _dbPaths.Add(CachedStorage.GetCacheFilePath(searchedFile, ".db"));
        return new SqliteStorage(searchedFile);
    }

    private long TimeInsert(ISearchStorage storage, List<ISearchResult> rows)
    {
        var sw = Stopwatch.StartNew();
        storage.AddFilteredBatch(rows);
        sw.Stop();
        return sw.ElapsedMilliseconds;
    }

    private static string Rate(int rows, long ms) => ms > 0 ? $"{rows * 1000L / ms:N0} rows/s" : "n/a";

    [TestMethod]
    [TestCategory("Performance")]
    [Timeout(300000)]
    public void Insert_Throughput_Baseline()
    {
        var rows = MakeRows(RowCount);

        using (var mem = new InMemoryStorage())
        {
            var ms = TimeInsert(mem, rows);
            TestContext.WriteLine($"InMemory        : {ms,7:N0} ms   ({Rate(RowCount, ms)})");
        }

        SqliteStorage.DisableFtsForMeasurement = false;
        using (var sql = NewSqlite())
        {
            var ms = TimeInsert(sql, rows);
            TestContext.WriteLine($"SQLite (FTS on) : {ms,7:N0} ms   ({Rate(RowCount, ms)})");
        }

        SqliteStorage.DisableFtsForMeasurement = true;
        using (var sql = NewSqlite())
        {
            var ms = TimeInsert(sql, rows);
            TestContext.WriteLine($"SQLite (no FTS) : {ms,7:N0} ms   ({Rate(RowCount, ms)})");
        }
    }

    // ---- Direct strategy comparison (bypasses SqliteStorage to isolate the insert mechanism) ----

    private readonly record struct Row(long Ticks, string Time, string Message);

    private static List<Row> MakeRawRows(int n)
    {
        var list = new List<Row>(n);
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < n; i++)
        {
            var ts = t.AddSeconds(i);
            list.Add(new Row(ts.Ticks, ts.ToString("o"),
                $"[{ts:yyyy-MM-dd HH:mm:ss}] INFO: scroll test message line number {i} with some extra padding text to reach a realistic width"));
        }
        return list;
    }

    private Microsoft.Data.Sqlite.SqliteConnection OpenBenchDb(bool integerTime)
    {
        var path = Path.Combine(Path.GetTempPath(), "benchraw_" + Guid.NewGuid().ToString("N") + ".db");
        _dbPaths.Add(path);
        var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
        conn.Open();
        using (var p = conn.CreateCommand())
        {
            p.CommandText = "PRAGMA journal_mode=MEMORY; PRAGMA synchronous=OFF; PRAGMA temp_store=MEMORY; PRAGMA cache_size=-65536;";
            p.ExecuteNonQuery();
        }
        using (var c = conn.CreateCommand())
        {
            c.CommandText = $"CREATE TABLE T (LogTime {(integerTime ? "INTEGER" : "TEXT")}, MachineName TEXT, Level INTEGER, Username TEXT, TaskName TEXT, OpCode TEXT, Source TEXT, SearchableData TEXT, Message TEXT, ResultSource TEXT);";
            c.ExecuteNonQuery();
        }
        return conn;
    }

    [TestMethod]
    [TestCategory("Performance")]
    [Timeout(300000)]
    public void Insert_Strategies_Compare()
    {
        var rows = MakeRawRows(RowCount);

        // A) Current approach: 500-row multi-VALUES, named @p params, reused, one transaction.
        using (var conn = OpenBenchDb(integerTime: false))
        {
            const int chunk = 500, cols = 10;
            var sw = Stopwatch.StartNew();
            using (var tx = conn.BeginTransaction())
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                var sb = new System.Text.StringBuilder("INSERT INTO T VALUES ");
                var ps = new Microsoft.Data.Sqlite.SqliteParameter[chunk * cols];
                for (int r = 0; r < chunk; r++)
                {
                    if (r > 0) sb.Append(',');
                    sb.Append('(');
                    for (int c = 0; c < cols; c++) { if (c > 0) sb.Append(','); var n = "@p" + (r * cols + c); sb.Append(n); ps[r * cols + c] = cmd.Parameters.Add(n, c == 2 ? Microsoft.Data.Sqlite.SqliteType.Integer : Microsoft.Data.Sqlite.SqliteType.Text); }
                    sb.Append(')');
                }
                cmd.CommandText = sb.ToString();
                cmd.Prepare();
                int i = 0;
                for (; i + chunk <= rows.Count; i += chunk)
                {
                    for (int r = 0; r < chunk; r++) BindRow(ps, r * cols, rows[i + r], intTime: false);
                    cmd.ExecuteNonQuery();
                }
                tx.Commit(); // (ignore <chunk tail for the benchmark; RowCount is a multiple of 500)
            }
            sw.Stop();
            TestContext.WriteLine($"A multi-row(500) named   : {sw.ElapsedMilliseconds,7:N0} ms   ({Rate(RowCount, sw.ElapsedMilliseconds)})");
        }

        // B) Single-row prepared, reused params, one transaction.
        using (var conn = OpenBenchDb(integerTime: false))
        {
            var sw = Stopwatch.StartNew();
            using (var tx = conn.BeginTransaction())
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO T VALUES (@a,@b,@c,@d,@e,@f,@g,@h,@i,@j)";
                var ps = new Microsoft.Data.Sqlite.SqliteParameter[10];
                string[] names = { "@a", "@b", "@c", "@d", "@e", "@f", "@g", "@h", "@i", "@j" };
                for (int c = 0; c < 10; c++) ps[c] = cmd.Parameters.Add(names[c], c == 2 ? Microsoft.Data.Sqlite.SqliteType.Integer : Microsoft.Data.Sqlite.SqliteType.Text);
                cmd.Prepare();
                foreach (var row in rows) { BindRow(ps, 0, row, intTime: false); cmd.ExecuteNonQuery(); }
                tx.Commit();
            }
            sw.Stop();
            TestContext.WriteLine($"B single-row reused      : {sw.ElapsedMilliseconds,7:N0} ms   ({Rate(RowCount, sw.ElapsedMilliseconds)})");
        }

        // C) Single-row reused, LogTime stored as INTEGER ticks (skip the per-row ToString("o")).
        using (var conn = OpenBenchDb(integerTime: true))
        {
            var sw = Stopwatch.StartNew();
            using (var tx = conn.BeginTransaction())
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO T VALUES (@a,@b,@c,@d,@e,@f,@g,@h,@i,@j)";
                var ps = new Microsoft.Data.Sqlite.SqliteParameter[10];
                string[] names = { "@a", "@b", "@c", "@d", "@e", "@f", "@g", "@h", "@i", "@j" };
                ps[0] = cmd.Parameters.Add("@a", Microsoft.Data.Sqlite.SqliteType.Integer);
                for (int c = 1; c < 10; c++) ps[c] = cmd.Parameters.Add(names[c], c == 2 ? Microsoft.Data.Sqlite.SqliteType.Integer : Microsoft.Data.Sqlite.SqliteType.Text);
                cmd.Prepare();
                foreach (var row in rows) { BindRow(ps, 0, row, intTime: true); cmd.ExecuteNonQuery(); }
                tx.Commit();
            }
            sw.Stop();
            TestContext.WriteLine($"C single-row int-time    : {sw.ElapsedMilliseconds,7:N0} ms   ({Rate(RowCount, sw.ElapsedMilliseconds)})");
        }
    }

    private static void BindRow(Microsoft.Data.Sqlite.SqliteParameter[] ps, int b, Row row, bool intTime)
    {
        ps[b + 0].Value = intTime ? row.Ticks : row.Time;
        ps[b + 1].Value = "BENCHBOX";
        ps[b + 2].Value = 4;
        ps[b + 3].Value = "user";
        ps[b + 4].Value = "Tick";
        ps[b + 5].Value = "";
        ps[b + 6].Value = "bench.log";
        ps[b + 7].Value = row.Message;
        ps[b + 8].Value = row.Message;
        ps[b + 9].Value = "bench.log";
    }

    private sealed class BenchResult : ISearchResult
    {
        private readonly DateTime _time;
        private readonly string _message;
        public BenchResult(DateTime time, string message) { _time = time; _message = message; }
        public DateTime GetLogTime() => _time;
        public string GetMachineName() => "BENCHBOX";
        public void WriteToConsole() { }
        public Level GetLevel() => Level.Info;
        public string GetUsername() => "user";
        public string GetTaskName() => "Tick";
        public string GetOpCode() => "";
        public string GetSource() => "bench.log";
        public string GetSearchableData() => _message;
        public string GetMessage() => _message;
        public string GetResultSource() => "bench.log";
    }
}
