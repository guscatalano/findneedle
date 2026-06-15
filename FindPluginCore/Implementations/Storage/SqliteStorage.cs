using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading;
using Microsoft.Data.Sqlite;
using FindNeedlePluginLib;
using FindNeedlePluginLib.Interfaces;
using FindNeedleCoreUtils; // Added for CachedStorage

namespace FindPluginCore.Implementations.Storage
{
    /// <summary>
    /// SQLite-backed implementation of ISearchStorage, using CachedStorage for DB location.
    /// </summary>
    public class SqliteStorage : ISearchStorage, IDisposable
    {
        private readonly string _dbPath;
        // Not readonly: replaced in the corruption-recovery path inside OpenAndInitialize.
        private SqliteConnection _connection;
        private readonly object _sync = new();

        // Running row counts, maintained on every insert/clear so GetStatistics is O(1). SQLite
        // has no cached row count, so SELECT COUNT(*) is a full table scan; GetStatistics is
        // called repeatedly during a search (status text, per-location PerfLog, cache prompt), so
        // scanning per call was O(rows) and dominated wall time on large spilled tables (~93s
        // across 40 calls in the 2M-record perf test). Seeded once from the tables at construction
        // so a reused warm cache reports the right numbers. volatile so GetStatistics can read
        // them without taking _sync — otherwise a status/viewer call to GetStatistics blocks behind
        // an in-progress write/spill that holds _sync for the duration of a slow multi-row insert
        // (on a slow disk that was ~90s of lock-wait in the 2M-record perf test). int reads/writes
        // are atomic; the += updates stay under _sync so they remain serialized with each other.
        private volatile int _rawCount;
        private volatile int _filteredCount;

        /// <summary>
        /// Fires after each <see cref="AddFilteredBatch"/> commits. The argument is the number of
        /// rows that just landed (the batch size, not the running total). Used by the streaming
        /// result viewer to refresh its row count and visible page without polling.
        /// </summary>
        public event Action<int>? FilteredRowsAdded;

        /// <summary>
        /// Bump whenever the on-disk schema (tables, indexes, FTS5 config, triggers) changes in
        /// a way that makes existing caches incompatible. Cache reuse checks this against the
        /// value stored in the <c>_meta</c> table on the cached DB.
        /// </summary>
        public const int CacheSchemaVersion = 1;

        /// <summary>
        /// True if the constructor was given a DB file whose <c>_meta</c> matched the source
        /// file and the caller subsequently chose to reuse it via <see cref="EvaluateCacheReuse"/>.
        /// Callers (NuSearchQuery) check this to decide whether to skip the file scan entirely.
        /// </summary>
        public bool ReusedExistingCache { get; private set; }

        /// <summary>
        /// Constructs a SqliteStorage for the given file being searched, storing the DB in AppData cache.
        /// </summary>
        /// <param name="searchedFilePath">The path of the file being searched.</param>
        public SqliteStorage(string searchedFilePath)
        {
            SQLitePCL.Batteries.Init(); // Ensure SQLite provider is initialized
            _dbPath = CachedStorage.GetCacheFilePath(searchedFilePath, ".db");
            OpenAndInitialize(allowRetryAfterCorruption: true);
            // NOTE: this constructor no longer wipes the tables unconditionally. The caller now
            // explicitly decides via EvaluateCacheReuse(): if the source file's size + mtime +
            // schema match what we wrote last time, the existing data is kept (warm cache hit,
            // search is near-instant); otherwise that method calls ClearTables and we run a
            // fresh scan as before. For callers that don't care about caching, calling
            // ClearTables() directly post-construction matches the old behaviour.

            // Seed the running counts from whatever the (possibly warm) cache holds. One COUNT
            // scan here — cheap on a fresh/empty DB, and paid once instead of on every
            // GetStatistics call. ClearTables() and the insert paths keep them in sync thereafter.
            lock (_sync)
            {
                _rawCount = GetCount("RawResults");
                _filteredCount = GetCount("FilteredResults");
            }
        }

        /// <summary>
        /// Open the connection, apply pragmas, and create the schema (without wiping existing
        /// rows — see the constructor note on cache reuse). If any of that throws
        /// <c>SQLITE_CORRUPT</c> (error code 11, "database disk image is malformed"), the file is
        /// left over from a prior process crash — our pragmas (<c>journal_mode=MEMORY</c> +
        /// <c>synchronous=OFF</c>) trade durability for throughput, so a crash mid-write can leave
        /// the file unreadable. We can't repair it, so we delete the cache file and start fresh.
        /// </summary>
        private void OpenAndInitialize(bool allowRetryAfterCorruption)
        {
            _connection = new SqliteConnection($"Data Source={_dbPath}");
            _connection.Open();
            try
            {
                ApplyBulkInsertPragmas();
                InitializeSchema();
                // Deliberately NOT wiping here. The constructor opens the (possibly warm) cache
                // as-is so a caller can validate and reuse it via EvaluateCacheReuse() — that's
                // the whole point of the on-disk cache. Clearing the tables here would make every
                // reopen start empty and silently defeat cache reuse. The consumer is responsible
                // for starting clean before a fresh scan: NuSearchQuery.Step2 calls ClearTables(),
                // and EvaluateCacheReuse() itself clears on a cache miss.
            }
            catch (SqliteException ex) when (allowRetryAfterCorruption && IsCorruption(ex))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SqliteStorage] cache DB '{_dbPath}' is corrupt; rebuilding from scratch: {ex.Message}");

                // Close + drop the connection so the OS releases the file handle before we delete.
                try { _connection.Close(); } catch { }
                try { _connection.Dispose(); } catch { }
                _connection = null;
                // Flush Microsoft.Data.Sqlite's connection pool so the file lock is fully released
                // (otherwise File.Delete fails on Windows with "file is being used by another
                // process").
                SqliteConnection.ClearAllPools();

                DeleteCacheFiles();
                // Single retry — if the recreated file also can't be initialised, throw the
                // failure of the second attempt so the caller sees a real error instead of an
                // infinite loop.
                OpenAndInitialize(allowRetryAfterCorruption: false);
            }
        }

        /// <summary>
        /// True if a <see cref="SqliteException"/> indicates the file is corrupt and can't be
        /// recovered in-place. SQLite result code 11 = <c>SQLITE_CORRUPT</c>; we also match on
        /// the message in case the provider exposes a different code for an extended error.
        /// </summary>
        private static bool IsCorruption(SqliteException ex)
        {
            if (ex == null) return false;
            if (ex.SqliteErrorCode == 11) return true; // SQLITE_CORRUPT
            return ex.Message != null
                && ex.Message.IndexOf("malformed", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Best-effort delete of the main DB plus any SQLite sidecar files.</summary>
        private void DeleteCacheFiles()
        {
            TryDelete(_dbPath);
            TryDelete(_dbPath + "-wal");
            TryDelete(_dbPath + "-shm");
            TryDelete(_dbPath + "-journal");
        }

        private static void TryDelete(string path)
        {
            try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SqliteStorage] could not delete '{path}': {ex.Message}");
            }
        }

        /// <summary>
        /// PRAGMAs tuned for the cache-DB workload: write 100k+ rows fast, then read often.
        /// Durability is not needed because the file is wiped on every <see cref="ClearTables"/>
        /// (i.e. on every construction) — if the process crashes mid-write, the file is going to
        /// be rebuilt from the source log on the next run anyway.
        /// </summary>
        private void ApplyBulkInsertPragmas()
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                PRAGMA journal_mode = MEMORY;
                PRAGMA synchronous  = OFF;
                PRAGMA temp_store   = MEMORY;
                PRAGMA cache_size   = -65536;  -- 64 MB page cache
            ";
            cmd.ExecuteNonQuery();
        }

        private void InitializeSchema()
        {
            var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS _meta (
                    Key   TEXT PRIMARY KEY,
                    Value TEXT
                );
                CREATE TABLE IF NOT EXISTS RawResults (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    LogTime TEXT,
                    MachineName TEXT,
                    Level INTEGER,
                    Username TEXT,
                    TaskName TEXT,
                    OpCode TEXT,
                    Source TEXT,
                    SearchableData TEXT,
                    Message TEXT,
                    ResultSource TEXT
                );
                CREATE TABLE IF NOT EXISTS FilteredResults (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    LogTime TEXT,
                    MachineName TEXT,
                    Level INTEGER,
                    Username TEXT,
                    TaskName TEXT,
                    OpCode TEXT,
                    Source TEXT,
                    SearchableData TEXT,
                    Message TEXT,
                    ResultSource TEXT
                );
                -- Indexes for the result viewer's most common filter/sort columns.
                CREATE INDEX IF NOT EXISTS IX_FilteredResults_Level    ON FilteredResults(Level);
                CREATE INDEX IF NOT EXISTS IX_FilteredResults_Source   ON FilteredResults(Source);
                CREATE INDEX IF NOT EXISTS IX_FilteredResults_LogTime  ON FilteredResults(LogTime);
            ";
            cmd.ExecuteNonQuery();

            InitializeFts5();
        }

        /// <summary>
        /// Set up an FTS5 trigram virtual table over the searchable text columns of
        /// <c>FilteredResults</c>. Trigram tokenization gives substring-match semantics — typing
        /// "rror" finds "Error" — at ~3× the storage cost of the indexed text. With this in
        /// place the result viewer's global search runs in milliseconds on million-row tables
        /// instead of doing the full-table <c>LIKE '%term%'</c> scan it would otherwise need.
        ///
        /// Implemented as an "external content" FTS5 table that indexes the existing
        /// FilteredResults rows in place. Triggers below keep the index in sync on insert,
        /// delete, and update.
        ///
        /// Falls back silently if the SQLite build doesn't support the trigram tokenizer (3.34+),
        /// in which case <see cref="_ftsAvailable"/> stays false and BuildWhere drops back to
        /// the LIKE OR-chain for the global search.
        /// </summary>
        /// <summary>
        /// Diagnostic switch: when true, the FTS5 trigram index is NOT created, so bulk inserts skip
        /// the per-row AFTER INSERT trigram trigger. Used to measure how much of SQLite ingest cost
        /// is the FTS index build. Set via the FINDNEEDLE_DISABLE_FTS environment variable (any
        /// non-empty value) so it can be toggled per process launch. Global search falls back to LIKE.
        /// </summary>
        public static bool DisableFtsForMeasurement { get; set; } =
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FINDNEEDLE_DISABLE_FTS"));

        private void InitializeFts5()
        {
            if (DisableFtsForMeasurement)
            {
                _ftsAvailable = false;
                FindPluginCore.Diagnostics.PerfLog.Log("storage.fts", ("built", false), ("reason", "disabled"));
                return;
            }
            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
                    CREATE VIRTUAL TABLE IF NOT EXISTS FilteredResults_fts USING fts5(
                        Source, TaskName, Message, ResultSource, SearchableData, LogTime,
                        content='FilteredResults',
                        content_rowid='Id',
                        tokenize='trigram'
                    );

                    -- No AFTER INSERT trigger: maintaining the trigram index per row during a bulk
                    -- load is ~2.8x slower than inserting rows trigger-free and running one
                    -- 'rebuild' afterward (see BuildSearchIndex / SqliteInsertBenchmark). The
                    -- delete/update triggers stay so incremental row changes remain consistent.
                    CREATE TRIGGER IF NOT EXISTS FilteredResults_ad AFTER DELETE ON FilteredResults
                    BEGIN
                        INSERT INTO FilteredResults_fts
                            (FilteredResults_fts, rowid, Source, TaskName, Message,
                             ResultSource, SearchableData, LogTime)
                        VALUES
                            ('delete', old.Id, old.Source, old.TaskName, old.Message,
                             old.ResultSource, old.SearchableData, old.LogTime);
                    END;

                    CREATE TRIGGER IF NOT EXISTS FilteredResults_au AFTER UPDATE ON FilteredResults
                    BEGIN
                        INSERT INTO FilteredResults_fts
                            (FilteredResults_fts, rowid, Source, TaskName, Message,
                             ResultSource, SearchableData, LogTime)
                        VALUES
                            ('delete', old.Id, old.Source, old.TaskName, old.Message,
                             old.ResultSource, old.SearchableData, old.LogTime);
                        INSERT INTO FilteredResults_fts
                            (rowid, Source, TaskName, Message, ResultSource, SearchableData, LogTime)
                        VALUES
                            (new.Id, new.Source, new.TaskName, new.Message,
                             new.ResultSource, new.SearchableData, new.LogTime);
                    END;
                ";
                cmd.ExecuteNonQuery();
                _ftsAvailable = true;
                _ftsIndexBuilt = false; // empty until BuildSearchIndex runs
            }
            catch (SqliteException ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SqliteStorage] FTS5 trigram unavailable; global search will fall back to LIKE: {ex.Message}");
                _ftsAvailable = false;
            }
        }

        private bool _ftsAvailable;

        // The FTS5 index is built in one bulk step (BuildSearchIndex) after ingest, not per-row via
        // an insert trigger (~2.8x faster). Until that runs the index is stale, so substring search
        // must fall back to LIKE. _ftsIndexBuilt tracks readiness; UseFts gates the FTS query path.
        private volatile bool _ftsIndexBuilt;
        private bool UseFts => _ftsAvailable && _ftsIndexBuilt;

        /// <summary>
        /// Build the FTS5 trigram index over FilteredResults in one bulk pass, after all rows are
        /// inserted (called by NuSearchQuery Step2). ~2.8x faster than maintaining it per-row via an
        /// insert trigger and produces an identical index. No-op when FTS isn't available (trigram
        /// tokenizer unsupported, or disabled via FINDNEEDLE_DISABLE_FTS).
        /// </summary>
        /// <summary>Rows indexed per batch. ~1.2s/batch at 50k, so the lock is released frequently
        /// enough that concurrent viewer paging interleaves, and cancellation is checked ~per second.</summary>
        private const int IndexBatchRows = 50_000;

        /// <summary>
        /// Build the FTS5 trigram index over FilteredResults in cancellable batches (INSERT…SELECT by
        /// Id range). Benchmarked at ~1.05x the one-shot 'rebuild' but, unlike 'rebuild', it can be
        /// cancelled between batches, reports progress, and frees the lock between batches so a
        /// concurrent viewer can keep paging. Until it finishes, substring search falls back to LIKE.
        /// <paramref name="onProgress"/> receives (rowsIndexed, totalRows) after each batch.
        /// No-op when FTS isn't available (unsupported tokenizer, or disabled via FINDNEEDLE_DISABLE_FTS).
        /// </summary>
        public void BuildSearchIndex(CancellationToken cancellationToken = default, Action<long, long> onProgress = null)
        {
            if (!_ftsAvailable) { _ftsIndexBuilt = false; return; }

            long total = _filteredCount;
            long indexed = 0;
            long lastId = 0;
            var start = Environment.TickCount64;
            try
            {
                // Start from an empty index so a re-run (or a resume after cancel) never double-inserts.
                lock (_sync)
                {
                    using var clr = _connection.CreateCommand();
                    clr.CommandText = "INSERT INTO FilteredResults_fts(FilteredResults_fts) VALUES('delete-all');";
                    clr.ExecuteNonQuery();
                }

                while (!cancellationToken.IsCancellationRequested)
                {
                    int n;
                    lock (_sync)
                    {
                        using var tx = _connection.BeginTransaction();
                        using (var cmd = _connection.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = @"
                                INSERT INTO FilteredResults_fts
                                    (rowid, Source, TaskName, Message, ResultSource, SearchableData, LogTime)
                                SELECT Id, Source, TaskName, Message, ResultSource, SearchableData, LogTime
                                FROM FilteredResults WHERE Id > @last ORDER BY Id LIMIT @bs;";
                            cmd.Parameters.AddWithValue("@last", lastId);
                            cmd.Parameters.AddWithValue("@bs", IndexBatchRows);
                            n = cmd.ExecuteNonQuery();
                            if (n > 0)
                            {
                                using var mx = _connection.CreateCommand();
                                mx.Transaction = tx;
                                mx.CommandText = "SELECT Id FROM FilteredResults WHERE Id > @last ORDER BY Id LIMIT 1 OFFSET @off";
                                mx.Parameters.AddWithValue("@last", lastId);
                                mx.Parameters.AddWithValue("@off", n - 1);
                                lastId = Convert.ToInt64(mx.ExecuteScalar());
                            }
                        }
                        tx.Commit();
                    }
                    if (n == 0) break;
                    indexed += n;
                    onProgress?.Invoke(indexed, total);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    // Partial index left behind; mark not-built so search uses LIKE and the next
                    // build's 'delete-all' starts clean.
                    _ftsIndexBuilt = false;
                    FindPluginCore.Diagnostics.PerfLog.Log("storage.fts", ("built", false),
                        ("reason", "cancelled"), ("indexed", indexed), ("of", total));
                }
                else
                {
                    _ftsIndexBuilt = true;
                    FindPluginCore.Diagnostics.PerfLog.Log("storage.fts", ("built", true),
                        ("rebuild_ms", Environment.TickCount64 - start), ("batched", true), ("rows", indexed));
                }
            }
            catch (SqliteException ex)
            {
                _ftsIndexBuilt = false;
                FindPluginCore.Diagnostics.PerfLog.Log("storage.fts", ("built", false),
                    ("reason", "rebuild_failed"), ("msg", ex.GetType().Name));
                System.Diagnostics.Debug.WriteLine(
                    $"[SqliteStorage] FTS build failed; substring search falls back to LIKE: {ex.Message}");
            }
        }

        /// <summary>
        /// Rough estimate (ms) of how long <see cref="BuildSearchIndex"/> will take for a given row
        /// count, for the "this will take a while" warning. Fitted to measured trigram-index build
        /// times (superlinear: t ≈ 0.0034·n^1.16; ~30s near ~900k rows, ~210s at 5M). Machine-
        /// dependent, so treat it as an order-of-magnitude gate, not a precise countdown.
        /// </summary>
        public static long PredictIndexBuildMs(long rowCount)
        {
            if (rowCount <= 0) return 0;
            return (long)(0.0034 * Math.Pow(rowCount, 1.16));
        }

        // ----- Cache reuse (size + mtime + schema version) -----

        /// <summary>
        /// Decide whether the on-disk cache can be reused for <paramref name="sourcePath"/>.
        /// If the stored metadata matches the file's current <c>size</c> + <c>mtime</c> + the
        /// schema version, we keep the rows and the FTS5 index untouched — the next search
        /// completes in milliseconds. Otherwise the tables are wiped and the caller runs a
        /// fresh scan as before.
        ///
        /// Size+mtime is a deliberate trade-off: virtually free to compute (one stat call) and
        /// catches every practical case where the log has actually changed. A full content hash
        /// would be ironclad but cost ~0.2–1 s per open even for warm hits.
        /// </summary>
        public bool EvaluateCacheReuse(string sourcePath, int schemaVersion)
        {
            ReusedExistingCache = false;

            if (string.IsNullOrEmpty(sourcePath) || !System.IO.File.Exists(sourcePath))
            {
                FindPluginCore.Diagnostics.PerfLog.Log("cache.eval", ("reuse", false), ("reason", "source_missing"));
                ClearTables();
                return false;
            }

            try
            {
                long expectedSize;
                DateTime expectedMtime;
                try
                {
                    var info = new System.IO.FileInfo(sourcePath);
                    expectedSize = info.Length;
                    expectedMtime = info.LastWriteTimeUtc;
                }
                catch (Exception ex)
                {
                    FindPluginCore.Diagnostics.PerfLog.Log("cache.eval", ("reuse", false), ("reason", "stat_failed"), ("msg", ex.GetType().Name));
                    ClearTables();
                    return false;
                }

                var meta = ReadMeta();
                if (meta == null)
                {
                    FindPluginCore.Diagnostics.PerfLog.Log("cache.eval", ("reuse", false), ("reason", "meta_unreadable"));
                    ClearTables();
                    return false;
                }
                if (meta.Count == 0)
                {
                    FindPluginCore.Diagnostics.PerfLog.Log("cache.eval", ("reuse", false), ("reason", "meta_empty"));
                    ClearTables();
                    return false;
                }
                if (!meta.TryGetValue("schema_version", out var sv) || sv != schemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
                {
                    FindPluginCore.Diagnostics.PerfLog.Log("cache.eval", ("reuse", false), ("reason", "schema_version_differs"),
                        ("got", sv ?? "(null)"), ("want", schemaVersion));
                    ClearTables();
                    return false;
                }
                if (!meta.TryGetValue("completed", out var c) || c != "1")
                {
                    FindPluginCore.Diagnostics.PerfLog.Log("cache.eval", ("reuse", false), ("reason", "not_completed"), ("got", c ?? "(null)"));
                    ClearTables();
                    return false;
                }
                if (!meta.TryGetValue("source_path", out var sp) || !string.Equals(sp, sourcePath, StringComparison.OrdinalIgnoreCase))
                {
                    FindPluginCore.Diagnostics.PerfLog.Log("cache.eval", ("reuse", false), ("reason", "path_differs"),
                        ("got_len", sp?.Length ?? 0), ("want_len", sourcePath.Length));
                    ClearTables();
                    return false;
                }
                if (!meta.TryGetValue("source_size", out var ss) || ss != expectedSize.ToString(System.Globalization.CultureInfo.InvariantCulture))
                {
                    FindPluginCore.Diagnostics.PerfLog.Log("cache.eval", ("reuse", false), ("reason", "size_differs"),
                        ("got", ss ?? "(null)"), ("want", expectedSize));
                    ClearTables();
                    return false;
                }
                var expectedMtimeStr = expectedMtime.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
                if (!meta.TryGetValue("source_mtime", out var sm) || sm != expectedMtimeStr)
                {
                    FindPluginCore.Diagnostics.PerfLog.Log("cache.eval", ("reuse", false), ("reason", "mtime_differs"),
                        ("got", sm ?? "(null)"), ("want", expectedMtimeStr));
                    ClearTables();
                    return false;
                }

                // Quick sanity check: make sure FilteredResults actually has rows. A meta-only
                // row with no data would be useless.
                int rows;
                using (var rc = _connection.CreateCommand())
                {
                    rc.CommandText = "SELECT COUNT(*) FROM FilteredResults";
                    rows = Convert.ToInt32(rc.ExecuteScalar() ?? 0);
                }
                if (rows == 0)
                {
                    FindPluginCore.Diagnostics.PerfLog.Log("cache.eval", ("reuse", false), ("reason", "empty_table"));
                    ClearTables();
                    return false;
                }

                FindPluginCore.Diagnostics.PerfLog.Log("cache.eval", ("reuse", true), ("rows", rows));
                ReusedExistingCache = true;
                // The reused cache DB already holds a built FTS index, so substring search can use
                // it immediately (no rebuild this run).
                _ftsIndexBuilt = _ftsAvailable;
                return true;
            }
            catch (Exception ex)
            {
                // Anything unexpected — fall back to a clean rebuild rather than risking a
                // partially-valid cache.
                FindPluginCore.Diagnostics.PerfLog.Log("cache.eval", ("reuse", false), ("reason", "exception"), ("msg", ex.GetType().Name));
                System.Diagnostics.Debug.WriteLine($"[SqliteStorage] EvaluateCacheReuse failed: {ex.Message}");
                try { ClearTables(); } catch { /* ignore */ }
                return false;
            }
        }

        /// <summary>
        /// Stamp the cache as complete + valid for the given source file. Caller (typically
        /// <c>NuSearchQuery</c> after Step 2 finishes) invokes this once the data is fully
        /// written so the next session's <see cref="EvaluateCacheReuse"/> can match against it.
        /// </summary>
        public void WriteCompletionMetadata(string sourcePath, int schemaVersion)
        {
            if (string.IsNullOrEmpty(sourcePath))
            {
                FindPluginCore.Diagnostics.PerfLog.Log("cache.write", ("ok", false), ("reason", "no_path"));
                return;
            }
            long size;
            string mtime;
            try
            {
                var info = new System.IO.FileInfo(sourcePath);
                if (!info.Exists)
                {
                    FindPluginCore.Diagnostics.PerfLog.Log("cache.write", ("ok", false), ("reason", "source_missing"));
                    return;
                }
                size = info.Length;
                mtime = info.LastWriteTimeUtc.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                FindPluginCore.Diagnostics.PerfLog.Log("cache.write", ("ok", false), ("reason", "stat_failed"), ("msg", ex.GetType().Name));
                return;
            }

            lock (_sync)
            {
                try
                {
                    using var tx = _connection.BeginTransaction();
                    WriteMetaKey(tx, "schema_version", schemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    WriteMetaKey(tx, "source_path",    sourcePath);
                    WriteMetaKey(tx, "source_size",    size.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    WriteMetaKey(tx, "source_mtime",   mtime);
                    WriteMetaKey(tx, "completed",      "1");
                    WriteMetaKey(tx, "completed_at",   DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
                    tx.Commit();
                    FindPluginCore.Diagnostics.PerfLog.Log("cache.write", ("ok", true), ("size", size), ("path_len", sourcePath.Length));
                }
                catch (Exception ex)
                {
                    FindPluginCore.Diagnostics.PerfLog.Log("cache.write", ("ok", false), ("reason", "sql_exception"), ("msg", ex.GetType().Name));
                    System.Diagnostics.Debug.WriteLine($"[SqliteStorage] WriteCompletionMetadata failed: {ex.Message}");
                }
            }
        }

        private Dictionary<string, string> ReadMeta()
        {
            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT Key, Value FROM _meta";
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    map[reader.GetString(0)] = reader.IsDBNull(1) ? "" : reader.GetString(1);
                }
                return map;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Read the <c>completed_at</c> stamp from <c>_meta</c> if present. Used by the cache-
        /// reuse prompt so the dialog can show "cache built X minutes ago". Returns null if
        /// the table is missing or the value isn't a valid ISO 8601 timestamp.
        /// </summary>
        public DateTime? TryGetCacheCompletedAt()
        {
            try
            {
                var meta = ReadMeta();
                if (meta != null
                    && meta.TryGetValue("completed_at", out var s)
                    && DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                {
                    return dt;
                }
            }
            catch { /* ignore */ }
            return null;
        }

        private void WriteMetaKey(SqliteTransaction tx, string key, string value)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO _meta(Key, Value) VALUES (@k, @v) " +
                              "ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value";
            cmd.Parameters.AddWithValue("@k", key);
            cmd.Parameters.AddWithValue("@v", value ?? "");
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Truncate both tables and reclaim disk space. Public so callers can also force a wipe
        /// mid-lifetime (e.g. for tests).
        /// </summary>
        public void ClearTables()
        {
            lock (_sync)
            {
                // For large existing FTS5 indexes, dropping the triggers + FTS table and then
                // re-running InitializeFts5() is much faster than letting the AD trigger fire
                // once per FilteredResults row. The new index is empty, ready for the next batch.
                if (_ftsAvailable)
                {
                    using var dropTx = _connection.BeginTransaction();
                    using (var drop = _connection.CreateCommand())
                    {
                        drop.Transaction = dropTx;
                        drop.CommandText = @"
                            DROP TRIGGER IF EXISTS FilteredResults_ai;
                            DROP TRIGGER IF EXISTS FilteredResults_ad;
                            DROP TRIGGER IF EXISTS FilteredResults_au;
                            DROP TABLE   IF EXISTS FilteredResults_fts;";
                        drop.ExecuteNonQuery();
                    }
                    dropTx.Commit();
                    _ftsAvailable = false;
                }

                using var tx = _connection.BeginTransaction();
                using (var cmd = _connection.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText =
                        "DELETE FROM RawResults; " +
                        "DELETE FROM FilteredResults; " +
                        // Wipe the cache-reuse metadata as well — the data it refers to is gone.
                        // The next successful search re-populates this via WriteCompletionMetadata.
                        "DELETE FROM _meta; " +
                        // Reset AUTOINCREMENT counters so Id starts at 1 again. sqlite_sequence may
                        // not exist if AUTOINCREMENT has never been triggered — IF EXISTS guard.
                        "DELETE FROM sqlite_sequence WHERE name IN ('RawResults','FilteredResults');";
                    try { cmd.ExecuteNonQuery(); }
                    catch (SqliteException) { /* sqlite_sequence absent on a fresh DB; ignore */ }
                }
                tx.Commit();

                using (var vac = _connection.CreateCommand())
                {
                    vac.CommandText = "VACUUM";
                    vac.ExecuteNonQuery();
                }

                // Recreate the FTS5 table + triggers fresh (empty index, not yet built).
                InitializeFts5();
                _ftsIndexBuilt = false;

                // Tables are now empty — keep the running counts in sync.
                _rawCount = 0;
                _filteredCount = 0;
            }
        }

        /// <summary>
        /// How often the bulk-insert loop fires its progress callback (every N rows). NOT a SQL
        /// batch size: inserts go one row at a time through a reused prepared statement, which
        /// benchmarks ~28× faster than a 500-row multi-VALUES statement under Microsoft.Data.Sqlite
        /// (the 5000 named-parameter bind per chunk dominated). See SqliteInsertBenchmark.
        /// </summary>
        private const int InsertChunkRows = 500;

        public void AddRawBatch(IEnumerable<ISearchResult> batch, CancellationToken cancellationToken = default)
        {
            if (batch == null) throw new ArgumentNullException(nameof(batch));
            lock (_sync)
            {
                using var transaction = _connection.BeginTransaction();
                BulkInsert("RawResults", batch, transaction, cancellationToken, out var insertedRaw);
                transaction.Commit();
                _rawCount += insertedRaw;
            }
        }

        public void AddFilteredBatch(IEnumerable<ISearchResult> batch, CancellationToken cancellationToken = default)
            => AddFilteredBatch(batch, cancellationToken, onProgress: null);

        /// <summary>
        /// Same as the parameterless overload, plus a progress hook. <paramref name="onProgress"/>
        /// is called with the running insert count after every <see cref="InsertChunkRows"/>
        /// (500) rows. Lets a caller surface "indexing 12,345 / 500,000…" status text while a
        /// single bulk insert is running — without breaking the inserts into many small
        /// transactions, which was ~3× slower in practice.
        /// </summary>
        public void AddFilteredBatch(
            IEnumerable<ISearchResult> batch,
            CancellationToken cancellationToken,
            Action<int> onProgress)
        {
            if (batch == null) throw new ArgumentNullException(nameof(batch));
            int inserted;
            lock (_sync)
            {
                using var transaction = _connection.BeginTransaction();
                BulkInsert("FilteredResults", batch, transaction, cancellationToken, out inserted, onProgress);
                transaction.Commit();
                _filteredCount += inserted;
                // New rows aren't in the FTS index (no insert trigger) — mark it stale until the
                // caller runs BuildSearchIndex. Substring search falls back to LIKE meanwhile.
                if (inserted > 0) _ftsIndexBuilt = false;
            }
            // Fire outside the lock so subscribers can re-enter (e.g. read a page) without
            // deadlocking the writer thread.
            if (inserted > 0) FilteredRowsAdded?.Invoke(inserted);
        }

        /// <summary>
        /// Bulk-insert path used by both AddRawBatch and AddFilteredBatch. Accumulates rows up to
        /// <see cref="InsertChunkRows"/>, flushes via a single multi-row <c>INSERT … VALUES (…),(…)</c>
        /// statement, then drains any tail through the single-row prepared command. Both commands
        /// are constructed and prepared exactly once per call.
        /// </summary>
        private void BulkInsert(
            string table,
            IEnumerable<ISearchResult> batch,
            SqliteTransaction tx,
            CancellationToken cancellationToken,
            out int inserted,
            Action<int> onProgress = null)
        {
            inserted = 0;

            // One prepared single-row INSERT, reused for every row inside the caller's transaction.
            // Counterintuitively this is ~28× faster than a 500-row multi-VALUES statement here:
            // Microsoft.Data.Sqlite's per-parameter bind cost makes a 5000-parameter chunk far more
            // expensive than 500 cheap single-row binds. (Benchmarked: ~370k rows/s vs ~13k rows/s.)
            using var cmd = CreatePreparedInsert(table, tx, out var p);
            foreach (var result in batch)
            {
                if (cancellationToken.IsCancellationRequested) break;
                BindAndExecute(cmd, p, result);
                inserted++;
                // Fire progress mid-transaction so the caller can update status text. The lock is
                // still held; subscribers must not call back into storage (the contract is: count only).
                if (inserted % InsertChunkRows == 0) onProgress?.Invoke(inserted);
            }
            if (inserted % InsertChunkRows != 0) onProgress?.Invoke(inserted);
        }

        /// <summary>
        /// Holds the parameter handles for a prepared insert so the batch loop can update values
        /// in place instead of re-allocating <see cref="SqliteParameter"/> objects every row.
        /// On a 500k-row insert this is the difference between ~30 s of UI hang and ~3 s.
        /// </summary>
        private struct InsertParams
        {
            public SqliteParameter LogTime, MachineName, Level, Username, TaskName,
                                   OpCode, Source, SearchableData, Message, ResultSource;
        }

        /// <summary>
        /// Build a SqliteCommand whose statement is parsed and bound exactly once. Callers reuse
        /// it across all rows in one batch by mutating the returned <see cref="InsertParams"/>
        /// values and calling <c>ExecuteNonQuery</c> per row.
        /// </summary>
        private SqliteCommand CreatePreparedInsert(string table, SqliteTransaction tx, out InsertParams p)
        {
            var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $@"
                INSERT INTO {table}
                (LogTime, MachineName, Level, Username, TaskName, OpCode, Source, SearchableData, Message, ResultSource)
                VALUES (@LogTime, @MachineName, @Level, @Username, @TaskName, @OpCode, @Source, @SearchableData, @Message, @ResultSource)";

            p = new InsertParams
            {
                LogTime        = cmd.Parameters.Add("@LogTime",        SqliteType.Text),
                MachineName    = cmd.Parameters.Add("@MachineName",    SqliteType.Text),
                Level          = cmd.Parameters.Add("@Level",          SqliteType.Integer),
                Username       = cmd.Parameters.Add("@Username",       SqliteType.Text),
                TaskName       = cmd.Parameters.Add("@TaskName",       SqliteType.Text),
                OpCode         = cmd.Parameters.Add("@OpCode",         SqliteType.Text),
                Source         = cmd.Parameters.Add("@Source",         SqliteType.Text),
                SearchableData = cmd.Parameters.Add("@SearchableData", SqliteType.Text),
                Message        = cmd.Parameters.Add("@Message",        SqliteType.Text),
                ResultSource   = cmd.Parameters.Add("@ResultSource",   SqliteType.Text),
            };
            cmd.Prepare();
            return cmd;
        }

        private static void BindAndExecute(SqliteCommand cmd, InsertParams p, ISearchResult r)
        {
            p.LogTime.Value        = r.GetLogTime().ToString("o");
            p.MachineName.Value    = r.GetMachineName() ?? "";
            p.Level.Value          = (int)r.GetLevel();
            p.Username.Value       = r.GetUsername() ?? "";
            p.TaskName.Value       = r.GetTaskName() ?? "";
            p.OpCode.Value         = r.GetOpCode() ?? "";
            p.Source.Value         = r.GetSource() ?? "";
            p.SearchableData.Value = r.GetSearchableData() ?? "";
            p.Message.Value        = r.GetMessage() ?? "";
            p.ResultSource.Value   = r.GetResultSource() ?? "";
            cmd.ExecuteNonQuery();
        }

        public void GetRawResultsInBatches(Action<List<ISearchResult>> onBatch, int batchSize = 1000, CancellationToken cancellationToken = default)
        {
            GetResultsInBatches("RawResults", onBatch, batchSize, cancellationToken);
        }

        public void GetFilteredResultsInBatches(Action<List<ISearchResult>> onBatch, int batchSize = 1000, CancellationToken cancellationToken = default)
        {
            GetResultsInBatches("FilteredResults", onBatch, batchSize, cancellationToken);
        }

        private void GetResultsInBatches(string table, Action<List<ISearchResult>> onBatch, int batchSize, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                var cmd = _connection.CreateCommand();
                cmd.CommandText = $"SELECT LogTime, MachineName, Level, Username, TaskName, OpCode, Source, SearchableData, Message, ResultSource FROM {table}";
                using var reader = cmd.ExecuteReader();
                var batch = new List<ISearchResult>(batchSize);
                while (reader.Read())
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    batch.Add(new SqliteSearchResult(reader));
                    if (batch.Count == batchSize)
                    {
                        onBatch(batch);
                        batch = new List<ISearchResult>(batchSize);
                    }
                }
                if (batch.Count > 0)
                {
                    onBatch(batch);
                }
            }
        }

        // ----- Paging API used by IPagedLogSource -----

        /// <summary>
        /// Bag of filter inputs that <see cref="GetFilteredCount"/> / <see cref="GetFilteredPage"/>
        /// translate into a parameterized SQL WHERE clause.
        /// Note the field-name vs SQL-column gotcha: the result viewer's "Provider" column comes
        /// from <c>ISearchResult.GetSource()</c>, which is stored in the SQL <c>Source</c> column;
        /// the viewer's "Source" column comes from <c>ISearchResult.GetResultSource()</c>, stored
        /// in the SQL <c>ResultSource</c> column.
        /// </summary>
        public sealed class FilterInput
        {
            public string Search;
            public string Provider;   // matches SQL Source
            public string TaskName;
            public string Message;
            public string Source;     // matches SQL ResultSource
            public int? LevelInt;     // (int)Level enum, mapped by caller
            public string LogTimeFrom; // ISO 8601 round-trip string ("o")
            public string LogTimeTo;
        }

        public sealed class SortInput
        {
            public string Column;     // viewer column name; mapped to SQL column inside
            public bool Descending;
        }

        public int GetFilteredCount(FilterInput filter)
        {
            lock (_sync)
            {
                var (where, ps) = BuildWhere(filter, UseFts);
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM FilteredResults {where}";
                BindParams(cmd, ps);
                try
                {
                    return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
                }
                catch (SqliteException) when (UseFts && HasGlobalSearch(filter))
                {
                    return GetFilteredCountLikeFallback(filter);
                }
            }
        }

        public List<ISearchResult> GetFilteredPage(FilterInput filter, SortInput sort, int offset, int limit)
        {
            lock (_sync)
            {
                var (where, ps) = BuildWhere(filter, UseFts);
                var orderBy = BuildOrderBy(sort);
                using var cmd = _connection.CreateCommand();
                cmd.CommandText =
                    "SELECT LogTime, MachineName, Level, Username, TaskName, OpCode, Source, " +
                    "SearchableData, Message, ResultSource FROM FilteredResults " +
                    $"{where} {orderBy} LIMIT @limit OFFSET @offset";
                BindParams(cmd, ps);
                cmd.Parameters.AddWithValue("@limit", limit);
                cmd.Parameters.AddWithValue("@offset", offset);
                var list = new List<ISearchResult>();
                try
                {
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read()) list.Add(new SqliteSearchResult(reader));
                    return list;
                }
                catch (SqliteException) when (UseFts && HasGlobalSearch(filter))
                {
                    return GetFilteredPageLikeFallback(filter, sort, offset, limit);
                }
            }
        }

        // Slow-path fallbacks. Used only when FTS5 was claimed available but the query against
        // FilteredResults_fts threw at runtime (e.g. malformed query expression). Same behavior
        // as the pre-FTS5 codepath: a six-column LIKE OR-chain.
        private int GetFilteredCountLikeFallback(FilterInput filter)
        {
            var (where, ps) = BuildWhere(filter, ftsAvailable: false);
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM FilteredResults {where}";
            BindParams(cmd, ps);
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        }

        private List<ISearchResult> GetFilteredPageLikeFallback(FilterInput filter, SortInput sort, int offset, int limit)
        {
            var (where, ps) = BuildWhere(filter, ftsAvailable: false);
            var orderBy = BuildOrderBy(sort);
            using var cmd = _connection.CreateCommand();
            cmd.CommandText =
                "SELECT LogTime, MachineName, Level, Username, TaskName, OpCode, Source, " +
                "SearchableData, Message, ResultSource FROM FilteredResults " +
                $"{where} {orderBy} LIMIT @limit OFFSET @offset";
            BindParams(cmd, ps);
            cmd.Parameters.AddWithValue("@limit", limit);
            cmd.Parameters.AddWithValue("@offset", offset);
            var list = new List<ISearchResult>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) list.Add(new SqliteSearchResult(reader));
            return list;
        }

        private static bool HasGlobalSearch(FilterInput f) => f != null && !string.IsNullOrEmpty(f.Search);

        public List<int> GetDistinctLevels()
        {
            lock (_sync)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT DISTINCT Level FROM FilteredResults ORDER BY Level";
                var levels = new List<int>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) levels.Add(reader.GetInt32(0));
                return levels;
            }
        }

        public Dictionary<int, int> GetLevelCounts(FilterInput filter)
        {
            lock (_sync)
            {
                var (where, ps) = BuildWhere(filter, UseFts);
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = $"SELECT Level, COUNT(*) FROM FilteredResults {where} GROUP BY Level";
                BindParams(cmd, ps);
                var counts = new Dictionary<int, int>();
                try
                {
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read()) counts[reader.GetInt32(0)] = reader.GetInt32(1);
                    return counts;
                }
                catch (SqliteException) when (UseFts && HasGlobalSearch(filter))
                {
                    var (where2, ps2) = BuildWhere(filter, ftsAvailable: false);
                    using var cmd2 = _connection.CreateCommand();
                    cmd2.CommandText = $"SELECT Level, COUNT(*) FROM FilteredResults {where2} GROUP BY Level";
                    BindParams(cmd2, ps2);
                    using var reader2 = cmd2.ExecuteReader();
                    while (reader2.Read()) counts[reader2.GetInt32(0)] = reader2.GetInt32(1);
                    return counts;
                }
            }
        }

        // ----- WHERE / ORDER BY builders -----

        // Trigram FTS5 needs at least 3 characters in the query to generate any trigrams. Anything
        // shorter would return zero results, so the global search falls back to LIKE for those.
        private const int TrigramMinLength = 3;

        private static (string clause, List<KeyValuePair<string, object>> parameters) BuildWhere(FilterInput f, bool ftsAvailable)
        {
            var conditions = new List<string>();
            var ps = new List<KeyValuePair<string, object>>();
            int idx = 0;

            void AddLike(string column, string term)
            {
                if (string.IsNullOrEmpty(term)) return;
                var name = "@p" + (idx++);
                conditions.Add($"{column} LIKE {name} ESCAPE '\\'");
                ps.Add(new KeyValuePair<string, object>(name, "%" + EscapeLike(term) + "%"));
            }

            if (f != null)
            {
                AddLike("Source",         f.Provider);     // viewer.Provider == SQL.Source
                AddLike("TaskName",       f.TaskName);
                AddLike("Message",        f.Message);
                AddLike("ResultSource",   f.Source);       // viewer.Source == SQL.ResultSource

                if (f.LevelInt.HasValue)
                {
                    var name = "@p" + (idx++);
                    conditions.Add($"Level = {name}");
                    ps.Add(new KeyValuePair<string, object>(name, f.LevelInt.Value));
                }
                if (!string.IsNullOrEmpty(f.LogTimeFrom))
                {
                    var name = "@p" + (idx++);
                    conditions.Add($"LogTime >= {name}");
                    ps.Add(new KeyValuePair<string, object>(name, f.LogTimeFrom));
                }
                if (!string.IsNullOrEmpty(f.LogTimeTo))
                {
                    var name = "@p" + (idx++);
                    conditions.Add($"LogTime <= {name}");
                    ps.Add(new KeyValuePair<string, object>(name, f.LogTimeTo));
                }
                if (!string.IsNullOrEmpty(f.Search))
                {
                    if (ftsAvailable && f.Search.Length >= TrigramMinLength)
                    {
                        // Trigram FTS5 path — substring-match semantics, served by the inverted
                        // index instead of a full table scan. The user's text becomes a single
                        // FTS5 phrase so any character is treated literally.
                        var name = "@p" + (idx++);
                        conditions.Add($"Id IN (SELECT rowid FROM FilteredResults_fts WHERE FilteredResults_fts MATCH {name})");
                        ps.Add(new KeyValuePair<string, object>(name, EscapeFts5Phrase(f.Search)));
                    }
                    else
                    {
                        // Fallback — short queries (< 3 chars) and DBs without FTS5. Six-column
                        // LIKE OR-chain. Slow on big tables, but correct.
                        var name = "@p" + (idx++);
                        var pattern = "%" + EscapeLike(f.Search) + "%";
                        conditions.Add(
                            "(Source LIKE " + name + " ESCAPE '\\' OR " +
                            "TaskName LIKE " + name + " ESCAPE '\\' OR " +
                            "Message LIKE " + name + " ESCAPE '\\' OR " +
                            "ResultSource LIKE " + name + " ESCAPE '\\' OR " +
                            "SearchableData LIKE " + name + " ESCAPE '\\' OR " +
                            "LogTime LIKE " + name + " ESCAPE '\\')");
                        ps.Add(new KeyValuePair<string, object>(name, pattern));
                    }
                }
            }

            return conditions.Count == 0 ? ("", ps) : ("WHERE " + string.Join(" AND ", conditions), ps);
        }

        // Wrap user text as a single FTS5 phrase. Inside double-quoted phrases FTS5 treats every
        // character literally except embedded double-quotes, which must be doubled.
        private static string EscapeFts5Phrase(string s)
            => "\"" + s.Replace("\"", "\"\"") + "\"";

        private static string BuildOrderBy(SortInput s)
        {
            if (s == null || string.IsNullOrEmpty(s.Column)) return "ORDER BY Id ASC"; // load order
            var dir = s.Descending ? "DESC" : "ASC";
            // Map viewer column names to SQL columns. See FilterInput note above for the
            // Provider/Source naming mismatch.
            var sqlCol = s.Column switch
            {
                "Index" => "Id",
                "Time" => "LogTime",
                "Provider" => "Source",
                "TaskName" => "TaskName",
                "Message" => "Message",
                "Source" => "ResultSource",
                "Level" => "Level",
                _ => "Id"
            };
            return $"ORDER BY {sqlCol} {dir}";
        }

        private static string EscapeLike(string s)
        {
            // Escape SQL LIKE wildcards so user-typed % and _ are matched literally.
            // ESCAPE '\\' is set on the LIKE clause so the backslash escape works.
            return s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        }

        private static void BindParams(SqliteCommand cmd, List<KeyValuePair<string, object>> ps)
        {
            foreach (var kv in ps) cmd.Parameters.AddWithValue(kv.Key, kv.Value);
        }

        public (int rawRecordCount, int filteredRecordCount, long sizeOnDisk, long sizeInMemory) GetStatistics()
        {
            // Deliberately lock-free. The counts are volatile ints (atomic reads) and the file
            // size is a filesystem metadata call that doesn't touch the DB connection — so this
            // never blocks behind a writer/spill that holds _sync for a slow multi-row insert.
            // Stats are a point-in-time snapshot; reading a count that omits an in-flight (not yet
            // committed) batch is fine for status display.
            int rawCount = _rawCount;
            int filteredCount = _filteredCount;
            long sizeOnDisk = 0;
            try
            {
                if (File.Exists(_dbPath))
                    sizeOnDisk = new FileInfo(_dbPath).Length;
            }
            catch { /* file briefly inaccessible — report 0, never throw from a stats read */ }
            long sizeInMemory = 0; // Not applicable for SQLite
            return (rawCount, filteredCount, sizeOnDisk, sizeInMemory);
        }

        private int GetCount(string table)
        {
            var cmd = _connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public void Dispose()
        {
            lock (_sync)
            {
                try
                {
                    // Ensure connection is closed before disposing to release any file locks
                    try { _connection?.Close(); } catch { }
                    _connection?.Dispose();
                }
                catch
                {
                    // swallow - tests will attempt deletion with retries
                }
            }
        }

        /// <summary>
        /// Helper class to wrap a search result loaded from SQLite.
        /// </summary>
        private class SqliteSearchResult : ISearchResult
        {
            private readonly DateTime _logTime;
            private readonly string _machineName;
            private readonly int _level;
            private readonly string _username;
            private readonly string _taskName;
            private readonly string _opCode;
            private readonly string _source;
            private readonly string _searchableData;
            private readonly string _message;
            private readonly string _resultSource;

            public SqliteSearchResult(IDataRecord record)
            {
                _logTime = DateTime.Parse(record.GetString(0), null, System.Globalization.DateTimeStyles.RoundtripKind);
                _machineName = record.GetString(1);
                _level = record.GetInt32(2);
                _username = record.GetString(3);
                _taskName = record.GetString(4);
                _opCode = record.GetString(5);
                _source = record.GetString(6);
                _searchableData = record.GetString(7);
                _message = record.GetString(8);
                _resultSource = record.GetString(9);
            }

            public DateTime GetLogTime() => _logTime;
            public string GetMachineName() => _machineName;
            public void WriteToConsole() => Console.WriteLine(GetMessage());
            public Level GetLevel() => (Level)_level;
            public string GetUsername() => _username;
            public string GetTaskName() => _taskName;
            public string GetOpCode() => _opCode;
            public string GetSource() => _source;
            public string GetSearchableData() => _searchableData;
            public string GetMessage() => _message;
            public string GetResultSource() => _resultSource;
        }
    }
}
