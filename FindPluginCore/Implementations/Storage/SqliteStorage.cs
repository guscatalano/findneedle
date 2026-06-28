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

        // Running per-level counts of FilteredResults, indexed by (int)Level, so GetDistinctLevels /
        // GetLevelCounts(no filter) can answer without SELECT DISTINCT / GROUP BY — and crucially
        // without taking _sync, which during a streaming scan blocks behind the writer (the viewer's
        // GetDistinctLevels stalled ~2s on a cold 1.5M-row open). Only trustworthy when built from an
        // empty table (a fresh scan): _levelCountsValid is true after ClearTables / on an empty open,
        // and flips false if a level falls outside the array. A warm-cache reuse leaves it false, so
        // those reads fall back to SQL — fine, since there's no concurrent writer to contend with then.
        // 16 slots covers the Level enum (0..5) with headroom; atomic int reads/writes, writes under _sync.
        private readonly int[] _levelCounts = new int[16];
        private volatile bool _levelCountsValid;

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
        // v2: added ProcessId/ThreadId/ActivityId. v3: EventId/Keywords/RelatedActivityId/Channel/
        // ProviderGuid/RecordId. v4: ProcessName/StructuredData. v5: folder-aware source signature
        // (source_count added; size/mtime now aggregate across a folder's files). v6: FTS no longer
        // double-indexes SearchableData when it equals Message (trigram build ~halved for text logs).
        // v7: LogTime is excluded from the FTS index by default (IndexLogTimeInFts toggle); bumped so
        // old v6 caches (which indexed timestamps) rebuild under the toggle-aware schema rather than
        // being reused with a mismatched setting. v8: Source/ResultSource are excluded from the FTS when
        // the load has a single distinct value (constant path = repetitive trigrams, no search value);
        // bumped so old caches rebuild with the new triggers. Old caches rescan; EnsureColumns migrates.
        public const int CacheSchemaVersion = 8;

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
            // The running level map is exact only when built from empty (a fresh scan increments it
            // per insert). A non-empty warm cache leaves it invalid → level reads use SQL.
            _levelCountsValid = _filteredCount == 0;
        }

        /// <summary>
        /// Open an existing cache .db directly by its file path (not by hashing a source path), for
        /// read-only viewing of a cached result set — e.g. the "Cached searches" page. Does not wipe;
        /// honors the cache's <c>fts_built</c> flag so substring search uses the FTS index if present.
        /// </summary>
        public static SqliteStorage OpenExistingCache(string dbPath) => new SqliteStorage(dbPath, openExisting: true);

        private SqliteStorage(string dbPath, bool openExisting)
        {
            SQLitePCL.Batteries.Init();
            _dbPath = dbPath;
            OpenAndInitialize(allowRetryAfterCorruption: true);
            lock (_sync)
            {
                _rawCount = GetCount("RawResults");
                _filteredCount = GetCount("FilteredResults");
            }
            _levelCountsValid = _filteredCount == 0;
            // If the cache recorded that its FTS index was built, mark it ready so substring search
            // uses the index immediately rather than falling back to LIKE.
            try
            {
                var meta = ReadMeta();
                _ftsIndexBuilt = RestoreFtsFromMeta(meta); // re-attaches shard files if the cache was sharded
            }
            catch { /* best effort */ }
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
                    ResultSource TEXT,
                    ProcessId TEXT,
                    ThreadId TEXT,
                    ActivityId TEXT,
                    EventId TEXT,
                    Keywords TEXT,
                    RelatedActivityId TEXT,
                    Channel TEXT,
                    ProviderGuid TEXT,
                    RecordId TEXT,
                    ProcessName TEXT,
                    StructuredData TEXT
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
                    ResultSource TEXT,
                    ProcessId TEXT,
                    ThreadId TEXT,
                    ActivityId TEXT,
                    EventId TEXT,
                    Keywords TEXT,
                    RelatedActivityId TEXT,
                    Channel TEXT,
                    ProviderGuid TEXT,
                    RecordId TEXT,
                    ProcessName TEXT,
                    StructuredData TEXT
                );
                -- Indexes for the result viewer's most common filter/sort columns.
                CREATE INDEX IF NOT EXISTS IX_FilteredResults_Level    ON FilteredResults(Level);
                CREATE INDEX IF NOT EXISTS IX_FilteredResults_Source   ON FilteredResults(Source);
                CREATE INDEX IF NOT EXISTS IX_FilteredResults_LogTime  ON FilteredResults(LogTime);
            ";
            cmd.ExecuteNonQuery();

            // Migrate older cache DBs in place: CREATE TABLE IF NOT EXISTS leaves a pre-existing table's
            // columns untouched, so a warm cache from before a column was added is missing it and inserts
            // fail ("table has no column named ProcessId"). Add any missing columns idempotently.
            EnsureColumns("RawResults");
            EnsureColumns("FilteredResults");

            InitializeFts5();
        }

        /// <summary>Add any of the expected newer columns that an existing (older-schema) table lacks.
        /// SQLite has no ADD COLUMN IF NOT EXISTS, so check PRAGMA table_info first.</summary>
        private void EnsureColumns(string table)
        {
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var q = _connection.CreateCommand())
            {
                q.CommandText = $"PRAGMA table_info({table});";
                using var r = q.ExecuteReader();
                while (r.Read()) existing.Add(r.GetString(1)); // col 1 = name
            }
            foreach (var col in new[] { "ProcessId", "ThreadId", "ActivityId",
                                        "EventId", "Keywords", "RelatedActivityId", "Channel", "ProviderGuid", "RecordId",
                                        "ProcessName", "StructuredData" })
            {
                if (existing.Contains(col)) continue;
                using var a = _connection.CreateCommand();
                a.CommandText = $"ALTER TABLE {table} ADD COLUMN {col} TEXT;";
                a.ExecuteNonQuery();
            }
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

        /// <summary>
        /// When true, the LogTime (timestamp) column is included in the FTS trigram index, so a timestamp
        /// fragment typed in the free-text search box matches. Default false: timestamps are highly uniform
        /// (huge, near-universal trigram postings) so indexing them is costly and low-value — time filtering
        /// is served by the dedicated time-range filters (structured LogTime SQL), which are unaffected
        /// either way. Toggled via the app setting (applied at startup) or the FINDNEEDLE_FTS_INDEX_LOGTIME
        /// env var. Flipping it makes a warm cache rescan (the stored value is part of cache validity).
        /// </summary>
        public static bool IndexLogTimeInFts { get; set; } =
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FINDNEEDLE_FTS_INDEX_LOGTIME"));

        /// <summary>
        /// When the filtered row count is at least this, the FTS index is built as N parallel shards
        /// (separate contentless fts5 DB files built concurrently and queried via ATTACH + UNION) instead
        /// of one single-writer index — the trigram build is the dominant, single-threaded cost, so this
        /// parallelizes it (~5x measured). Default 2,000,000 (only genuinely large logs, where the single
        /// build runs tens of seconds to minutes and the win clearly amortizes the per-shard overhead);
        /// smaller logs keep the single external-content index. Override via FINDNEEDLE_FTS_SHARD_THRESHOLD
        /// (set very high to disable). Sharded caches warm-reuse across sessions (shard files persist next
        /// to the .db and are re-attached on reopen; the cache pruner evicts them with their .db).
        /// </summary>
        public const int DefaultFtsShardThreshold = 2_000_000;
        public static int FtsShardThreshold { get; set; } =
            int.TryParse(Environment.GetEnvironmentVariable("FINDNEEDLE_FTS_SHARD_THRESHOLD"), out var _fst) && _fst > 0
                ? _fst : DefaultFtsShardThreshold;

        /// <summary>
        /// When true (default), large streaming "just view a log" ingests fan out the per-shard SQLite
        /// inserts across N writer threads while one thread decodes, then merge the shards into one DB —
        /// measured ~2× faster first-open on a 5M .etl than the serial single-writer insert. The toggle is
        /// the fail-safe: off ⇒ the untouched serial streaming insert (and live row-fill in the viewer,
        /// which the fan-out can't do since rows appear only after the merge). Set the app setting (applied
        /// at startup) or FINDNEEDLE_PARALLEL_INGEST=0/false to disable. See <see cref="ParallelIngestSink"/>.
        /// </summary>
        public static bool ParallelIngestEnabled { get; set; } =
            !IsEnvFalse(Environment.GetEnvironmentVariable("FINDNEEDLE_PARALLEL_INGEST"));

        private static bool IsEnvFalse(string v) =>
            !string.IsNullOrEmpty(v) && (v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase) || v.Equals("off", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Estimated-row floor below which a streaming ingest stays on the serial path even when
        /// <see cref="ParallelIngestEnabled"/> — the fan-out's shard setup + merge tail only amortize on
        /// genuinely large logs (a 60k-row log inserts serially in well under a second). Default 500,000;
        /// override via FINDNEEDLE_PARALLEL_INGEST_MIN.
        /// </summary>
        public const int DefaultParallelIngestMinRows = 500_000;
        public static int ParallelIngestMinRows { get; set; } =
            int.TryParse(Environment.GetEnvironmentVariable("FINDNEEDLE_PARALLEL_INGEST_MIN"), out var _pim) && _pim > 0
                ? _pim : DefaultParallelIngestMinRows;

        /// <summary>Number of shard writers for the parallel fan-out ingest (same core-bound cap as the
        /// FTS shard count: leave a core for the producer + the OS).</summary>
        public static int ParallelIngestShardCount() => Math.Max(2, Math.Min(MaxShards, Environment.ProcessorCount - 1));

        // Sharded-FTS state for the current session, set by BuildShardedIndex.
        private bool _ftsSharded;
        private int _shardCount;
        // SQLite's default SQLITE_MAX_ATTACHED is 10 → cap at 8 to leave headroom for the main + temp dbs.
        private const int MaxShards = 8;
        private static int ShardCountFor(long rows) => Math.Max(2, Math.Min(MaxShards, Environment.ProcessorCount - 1));
        private string ShardDbPath(int k) => $"{_dbPath}.fts{k}";

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
                using (var cmd = _connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        CREATE VIRTUAL TABLE IF NOT EXISTS FilteredResults_fts USING fts5(
                            Source, TaskName, Message, ResultSource, SearchableData, LogTime,
                            content='FilteredResults',
                            content_rowid='Id',
                            tokenize='trigram'
                        );";
                    cmd.ExecuteNonQuery();
                }

                // No AFTER INSERT trigger: maintaining the trigram index per row during a bulk load is
                // ~2.8x slower than inserting trigger-free and running one bulk pass afterward (see
                // BuildSearchIndex). The delete/update triggers keep incremental row changes consistent.
                // Create them with conservative defaults (index everything) now; BuildSearchIndex
                // recreates them with the data-driven decision once row counts are known.
                CreateFtsTriggers(indexSource: true, indexResultSource: true);
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

        /// <summary>
        /// (Re)create the FTS delete/update triggers with the column expressions that decide what gets
        /// trigram-indexed, so the external-content index stays consistent on later delete/update:
        ///   • Source / ResultSource — indexed only when the load has &gt;1 distinct value. A single-source
        ///     load's path is a constant repeated on every row → pure repetitive-trigram cost with no search
        ///     value (matching it returns all-or-nothing); the structured Source/ResultSource filters are
        ///     unaffected. Folder/zip/multi-file loads (&gt;1 distinct) index it normally so path search works.
        ///   • SearchableData — blanked when it equals Message (don't trigram the dominant text twice).
        ///   • LogTime — blanked unless <see cref="IndexLogTimeInFts"/>.
        /// <see cref="BuildSearchIndex"/>'s INSERT…SELECT must use the same expressions. Caller provides
        /// connection-thread safety (ctor, or under the build's lock).
        /// </summary>
        private void CreateFtsTriggers(bool indexSource, bool indexResultSource)
        {
            string srcOld = indexSource ? "old.Source" : "''";
            string srcNew = indexSource ? "new.Source" : "''";
            string rsOld = indexResultSource ? "old.ResultSource" : "''";
            string rsNew = indexResultSource ? "new.ResultSource" : "''";
            string ltOld = IndexLogTimeInFts ? "old.LogTime" : "''";
            string ltNew = IndexLogTimeInFts ? "new.LogTime" : "''";
            const string sdOld = "CASE WHEN old.SearchableData = old.Message THEN '' ELSE old.SearchableData END";
            const string sdNew = "CASE WHEN new.SearchableData = new.Message THEN '' ELSE new.SearchableData END";

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $@"
                DROP TRIGGER IF EXISTS FilteredResults_ad;
                DROP TRIGGER IF EXISTS FilteredResults_au;
                CREATE TRIGGER FilteredResults_ad AFTER DELETE ON FilteredResults
                BEGIN
                    INSERT INTO FilteredResults_fts
                        (FilteredResults_fts, rowid, Source, TaskName, Message, ResultSource, SearchableData, LogTime)
                    VALUES
                        ('delete', old.Id, {srcOld}, old.TaskName, old.Message, {rsOld}, {sdOld}, {ltOld});
                END;
                CREATE TRIGGER FilteredResults_au AFTER UPDATE ON FilteredResults
                BEGIN
                    INSERT INTO FilteredResults_fts
                        (FilteredResults_fts, rowid, Source, TaskName, Message, ResultSource, SearchableData, LogTime)
                    VALUES
                        ('delete', old.Id, {srcOld}, old.TaskName, old.Message, {rsOld}, {sdOld}, {ltOld});
                    INSERT INTO FilteredResults_fts
                        (rowid, Source, TaskName, Message, ResultSource, SearchableData, LogTime)
                    VALUES
                        (new.Id, {srcNew}, new.TaskName, new.Message, {rsNew}, {sdNew}, {ltNew});
                END;";
            cmd.ExecuteNonQuery();
        }

        // The FTS5 index is built in one bulk step (BuildSearchIndex) after ingest, not per-row via
        // an insert trigger (~2.8x faster). Until that runs the index is stale, so substring search
        // must fall back to LIKE. _ftsIndexBuilt tracks readiness; UseFts gates the FTS query path.
        private volatile bool _ftsIndexBuilt;
        private bool UseFts => _ftsAvailable && _ftsIndexBuilt;

        /// <summary>
        /// True once the FTS trigram index is built and substring search runs against it; false when
        /// the index is missing/stale and search falls back to LIKE. Lets the UI decide whether a
        /// lazy/background build is needed. Always effectively "ready" for non-FTS backends.
        /// </summary>
        public bool IsSearchIndexBuilt => !_ftsAvailable || _ftsIndexBuilt;

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

            // Large logs: build the index as parallel shards (the trigram build is the dominant
            // single-threaded cost). Below the threshold, the single external-content index below.
            if (_filteredCount >= FtsShardThreshold)
            {
                BuildShardedIndex(cancellationToken, onProgress);
                return;
            }

            long total = _filteredCount;
            long indexed = 0;
            long lastId = 0;
            var start = Environment.TickCount64;
            try
            {
                // Decide which path columns are worth trigram-indexing: skip Source / ResultSource when the
                // load has a single distinct value (a constant path repeated on every row is pure repetitive-
                // trigram cost with no search value). Recreate the triggers to match before building, so the
                // external-content index stays consistent on later delete/update.
                bool indexSource, indexResultSource;
                lock (_sync)
                {
                    indexSource = DistinctAtLeastTwo("Source");
                    indexResultSource = DistinctAtLeastTwo("ResultSource");
                    CreateFtsTriggers(indexSource, indexResultSource);
                }
                // These expressions must match CreateFtsTriggers exactly.
                string srcExpr = indexSource ? "Source" : "''";
                string rsExpr = indexResultSource ? "ResultSource" : "''";
                string ltExpr = IndexLogTimeInFts ? "LogTime" : "''";

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
                            // Blank Source/ResultSource (single-distinct loads), SearchableData (== Message),
                            // and LogTime (unless IndexLogTimeInFts) — must match CreateFtsTriggers exactly.
                            cmd.CommandText = $@"
                                INSERT INTO FilteredResults_fts
                                    (rowid, Source, TaskName, Message, ResultSource, SearchableData, LogTime)
                                SELECT Id, {srcExpr}, TaskName, Message, {rsExpr},
                                       CASE WHEN SearchableData = Message THEN '' ELSE SearchableData END,
                                       {ltExpr}
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
                        ("rebuild_ms", Environment.TickCount64 - start), ("batched", true), ("rows", indexed),
                        ("idx_source", indexSource), ("idx_resultsource", indexResultSource));
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
        /// Build the trigram index as N parallel shards: each shard is a contentless fts5 table in its own
        /// DB file, built concurrently by reading a contiguous Id-range from the (attached) main DB. The
        /// shards are ATTACHed to the main connection; <see cref="BuildWhere"/> unions the per-shard MATCH.
        /// Rows stay in the one main table, so paging/sort/count are unchanged. Same column-blanking
        /// decisions as the single build so search semantics are identical.
        /// </summary>
        private void BuildShardedIndex(CancellationToken ct, Action<long, long> onProgress)
        {
            var start = Environment.TickCount64;
            int shardCount = ShardCountFor(_filteredCount);

            bool indexSource, indexResultSource;
            long minId, maxId;
            lock (_sync)
            {
                indexSource = DistinctAtLeastTwo("Source");
                indexResultSource = DistinctAtLeastTwo("ResultSource");
                using var mm = _connection.CreateCommand();
                mm.CommandText = "SELECT MIN(Id), MAX(Id) FROM FilteredResults";
                using var r = mm.ExecuteReader();
                if (!r.Read() || r.IsDBNull(0)) { _ftsSharded = false; _ftsIndexBuilt = false; return; }
                minId = r.GetInt64(0); maxId = r.GetInt64(1);
            }
            string srcExpr = indexSource ? "Source" : "''";
            string rsExpr = indexResultSource ? "ResultSource" : "''";
            string ltExpr = IndexLogTimeInFts ? "LogTime" : "''";

            // Clear any prior shards (detach from the connection + delete the files) before rebuilding.
            DetachAndDeleteShards();

            long span = maxId - minId + 1;
            long per = (span + shardCount - 1) / shardCount;
            long indexed = 0;
            try
            {
                System.Threading.Tasks.Parallel.For(0, shardCount, k =>
                {
                    if (ct.IsCancellationRequested) return;
                    long lo = minId + (long)k * per;
                    long hi = Math.Min(lo + per, maxId + 1); // [lo, hi)
                    var path = ShardDbPath(k);
                    try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); } catch { }

                    using var sc = new SqliteConnection($"Data Source={path}");
                    sc.Open();
                    using (var p = sc.CreateCommand())
                    { p.CommandText = "PRAGMA journal_mode=MEMORY; PRAGMA synchronous=OFF; PRAGMA temp_store=MEMORY; PRAGMA cache_size=-65536;"; p.ExecuteNonQuery(); }
                    // Contentless: stores the inverted index + rowid only (we fetch the row from the main
                    // table by Id). Same 6 columns as the single index so MATCH semantics match.
                    using (var cr = sc.CreateCommand())
                    { cr.CommandText = $"CREATE VIRTUAL TABLE fts{k} USING fts5(Source,TaskName,Message,ResultSource,SearchableData,LogTime, content='', tokenize='trigram');"; cr.ExecuteNonQuery(); }
                    // Read this shard's range from a SEPARATE read-only connection to the main DB. A
                    // read-only connection doesn't take a write lock, so N workers read concurrently
                    // without contention — a read-write ATTACH from many workers hits "database is locked".
                    using var src = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
                    src.Open();
                    using var read = src.CreateCommand();
                    read.CommandText = $@"SELECT Id, {srcExpr}, TaskName, Message, {rsExpr},
                                          CASE WHEN SearchableData = Message THEN '' ELSE SearchableData END, {ltExpr}
                                          FROM FilteredResults WHERE Id >= @lo AND Id < @hi";
                    read.Parameters.AddWithValue("@lo", lo);
                    read.Parameters.AddWithValue("@hi", hi);
                    using (var tx = sc.BeginTransaction())
                    using (var ins = sc.CreateCommand())
                    {
                        ins.Transaction = tx;
                        ins.CommandText = $"INSERT INTO fts{k}(rowid,Source,TaskName,Message,ResultSource,SearchableData,LogTime) VALUES(@r,@s,@t,@m,@rs,@sd,@lt)";
                        SqliteParameter Add(string n2) { var p2 = ins.CreateParameter(); p2.ParameterName = n2; ins.Parameters.Add(p2); return p2; }
                        var pr = Add("@r"); var psr = Add("@s"); var pt = Add("@t"); var pm = Add("@m");
                        var prs = Add("@rs"); var psd = Add("@sd"); var plt = Add("@lt");
                        ins.Prepare();
                        long localN = 0;
                        using (var rd = read.ExecuteReader())
                            while (rd.Read())
                            {
                                if (ct.IsCancellationRequested) break;
                                pr.Value = rd.GetInt64(0);
                                psr.Value = rd.IsDBNull(1) ? "" : rd.GetString(1);
                                pt.Value = rd.IsDBNull(2) ? "" : rd.GetString(2);
                                pm.Value = rd.IsDBNull(3) ? "" : rd.GetString(3);
                                prs.Value = rd.IsDBNull(4) ? "" : rd.GetString(4);
                                psd.Value = rd.IsDBNull(5) ? "" : rd.GetString(5);
                                plt.Value = rd.IsDBNull(6) ? "" : rd.GetString(6);
                                ins.ExecuteNonQuery();
                                localN++;
                            }
                        tx.Commit();
                        System.Threading.Interlocked.Add(ref indexed, localN);
                        onProgress?.Invoke(System.Threading.Interlocked.Read(ref indexed), _filteredCount);
                    }
                });

                if (ct.IsCancellationRequested)
                {
                    DetachAndDeleteShards();
                    _ftsIndexBuilt = false;
                    FindPluginCore.Diagnostics.PerfLog.Log("storage.fts", ("built", false), ("reason", "cancelled"), ("sharded", true));
                    return;
                }

                // Attach the freshly-built shards to the query connection.
                lock (_sync)
                {
                    for (int k = 0; k < shardCount; k++)
                    {
                        using var a = _connection.CreateCommand();
                        a.CommandText = $"ATTACH DATABASE '{ShardDbPath(k).Replace("'", "''")}' AS shard{k};";
                        a.ExecuteNonQuery();
                    }
                }
                _ftsSharded = true;
                _shardCount = shardCount;
                _ftsIndexBuilt = true;
                FindPluginCore.Diagnostics.PerfLog.Log("storage.fts", ("built", true),
                    ("rebuild_ms", Environment.TickCount64 - start), ("sharded", true), ("shards", shardCount), ("rows", indexed));
            }
            catch (Exception ex)
            {
                // Parallel.For wraps worker exceptions in AggregateException — unwrap to log the real
                // cause. Any failure falls back to the LIKE scan (search stays correct, just slower).
                var real = (ex as AggregateException)?.Flatten().InnerException ?? ex;
                DetachAndDeleteShards();
                _ftsIndexBuilt = false;
                FindPluginCore.Diagnostics.PerfLog.Log("storage.fts", ("built", false), ("reason", "shard_build_failed"),
                    ("msg", real.GetType().Name), ("detail", real.Message));
                System.Diagnostics.Debug.WriteLine($"[SqliteStorage] sharded FTS build failed; falls back to LIKE: {real.Message}");
            }
        }

        /// <summary>Detach any attached shard DBs from the query connection and delete the shard files.</summary>
        /// <summary>Detach shard DBs from the query connection (release file handles) WITHOUT deleting the
        /// files — so a clean Dispose leaves them on disk for the next session to warm-reuse.</summary>
        private void DetachShards()
        {
            lock (_sync)
            {
                for (int k = 0; k < MaxShards; k++)
                {
                    try { using var d = _connection.CreateCommand(); d.CommandText = $"DETACH DATABASE shard{k};"; d.ExecuteNonQuery(); }
                    catch { /* not attached — fine */ }
                }
            }
        }

        /// <summary>Detach AND delete the shard files — for a cache wipe / fresh rebuild (the index they
        /// hold is being discarded). NOT for Dispose (that keeps them for reuse — see <see cref="DetachShards"/>).</summary>
        private void DetachAndDeleteShards()
        {
            DetachShards();
            for (int k = 0; k < MaxShards; k++)
            {
                var p = ShardDbPath(k);
                try { if (System.IO.File.Exists(p)) System.IO.File.Delete(p); } catch { }
                foreach (var s in new[] { "-wal", "-shm", "-journal" })
                    try { if (System.IO.File.Exists(p + s)) System.IO.File.Delete(p + s); } catch { }
            }
            _ftsSharded = false;
            _shardCount = 0;
        }

        /// <summary>
        /// On cache reuse, restore FTS readiness from the cache metadata. If the cache recorded a sharded
        /// index (fts_shards>0), re-ATTACH the shard files (detach-then-attach so it's idempotent across
        /// the constructor + EvaluateCacheReuse paths). If any shard file is missing, returns false so the
        /// caller rebuilds. Returns whether the FTS index is ready to query.
        /// </summary>
        private bool RestoreFtsFromMeta(Dictionary<string, string> meta)
        {
            if (!_ftsAvailable || meta == null) return false;
            if (!(meta.TryGetValue("fts_built", out var fb) && fb == "1")) return false;
            int shards = meta.TryGetValue("fts_shards", out var fs) && int.TryParse(fs, out var n) ? n : 0;
            if (shards <= 0) return true; // single external-content index — already in this DB.

            for (int k = 0; k < shards; k++)
                if (!System.IO.File.Exists(ShardDbPath(k))) { DetachAndDeleteShards(); return false; }
            try
            {
                lock (_sync)
                    for (int k = 0; k < shards; k++)
                    {
                        try { using var d = _connection.CreateCommand(); d.CommandText = $"DETACH DATABASE shard{k};"; d.ExecuteNonQuery(); } catch { }
                        using var a = _connection.CreateCommand();
                        a.CommandText = $"ATTACH DATABASE '{ShardDbPath(k).Replace("'", "''")}' AS shard{k};";
                        a.ExecuteNonQuery();
                    }
                _ftsSharded = true;
                _shardCount = shards;
                return true;
            }
            catch { DetachAndDeleteShards(); return false; }
        }

        /// <summary>Whether <paramref name="column"/> has at least two distinct values (short-circuits at 2,
        /// so it's cheap even on millions of rows). Used to decide if a path column is worth indexing.
        /// <paramref name="column"/> is always a hard-coded identifier — never user input.</summary>
        private bool DistinctAtLeastTwo(string column)
        {
            using var c = _connection.CreateCommand();
            c.CommandText = $"SELECT COUNT(*) FROM (SELECT 1 FROM FilteredResults GROUP BY {column} LIMIT 2)";
            return Convert.ToInt64(c.ExecuteScalar()) >= 2;
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

            if (!CachedStorage.SourceExists(sourcePath))
            {
                FindPluginCore.Diagnostics.PerfLog.Log("cache.eval", ("reuse", false), ("reason", "source_missing"));
                ClearTables();
                return false;
            }

            try
            {
                long expectedSize;
                DateTime expectedMtime;
                int expectedCount;
                // Signature works for a single file OR a folder (sum of sizes, newest mtime, file
                // count) so the warm-cache fast path covers folder sources like the DISM Logs folder.
                if (!CachedStorage.TryGetSourceSignature(sourcePath, out expectedSize, out expectedMtime, out expectedCount))
                {
                    FindPluginCore.Diagnostics.PerfLog.Log("cache.eval", ("reuse", false), ("reason", "stat_failed"));
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
                // The LogTime-in-FTS setting changes how the index is tokenized; a cache built under the
                // other setting has a mismatched index (and triggers), so don't reuse it — rescan fresh.
                var wantLogTime = IndexLogTimeInFts ? "1" : "0";
                if ((meta.TryGetValue("fts_logtime", out var flt) ? flt : "0") != wantLogTime)
                {
                    FindPluginCore.Diagnostics.PerfLog.Log("cache.eval", ("reuse", false), ("reason", "fts_logtime_differs"),
                        ("got", flt ?? "(null)"), ("want", wantLogTime));
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
                // File count guards the folder case: a file added or removed (without changing total
                // size or the newest mtime — e.g. a log rotated out) would otherwise slip through.
                if (!meta.TryGetValue("source_count", out var sc) || sc != expectedCount.ToString(System.Globalization.CultureInfo.InvariantCulture))
                {
                    FindPluginCore.Diagnostics.PerfLog.Log("cache.eval", ("reuse", false), ("reason", "count_differs"),
                        ("got", sc ?? "(null)"), ("want", expectedCount));
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

                // The reused cache holds a built FTS index only if the run that wrote it actually
                // built one (eager search, or a deferred build that completed before close). If
                // fts_built=0 (deferred run that was never searched), substring search uses LIKE
                // until a lazy/background build runs.
                _ftsIndexBuilt = RestoreFtsFromMeta(meta); // re-attaches shard files if the cache was sharded

                FindPluginCore.Diagnostics.PerfLog.Log("cache.eval", ("reuse", true), ("rows", rows),
                    ("fts_built", _ftsIndexBuilt), ("fts_shards", _shardCount));
                ReusedExistingCache = true;
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
            int count;
            // Same file-or-folder signature EvaluateCacheReuse validates against.
            if (!CachedStorage.TryGetSourceSignature(sourcePath, out size, out var mtimeUtc, out count))
            {
                FindPluginCore.Diagnostics.PerfLog.Log("cache.write", ("ok", false), ("reason", "source_missing"));
                return;
            }
            mtime = mtimeUtc.ToString("o", System.Globalization.CultureInfo.InvariantCulture);

            lock (_sync)
            {
                try
                {
                    using var tx = _connection.BeginTransaction();
                    WriteMetaKey(tx, "schema_version", schemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    WriteMetaKey(tx, "source_path",    sourcePath);
                    WriteMetaKey(tx, "source_size",    size.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    WriteMetaKey(tx, "source_mtime",   mtime);
                    WriteMetaKey(tx, "source_count",   count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    WriteMetaKey(tx, "completed",      "1");
                    // Whether the FTS index is already built. With deferred (lazy/background) indexing
                    // the rows can be cache-complete while the index isn't built yet — a warm reopen
                    // reads this to decide whether substring search can use FTS or must (re)build.
                    WriteMetaKey(tx, "fts_built",      _ftsIndexBuilt ? "1" : "0");
                    // Shard count (0 = single external-content index). A warm reopen re-attaches this many
                    // shard files (RestoreFtsFromMeta); the files live next to this .db as {db}.fts{k}.
                    WriteMetaKey(tx, "fts_shards",     _ftsSharded ? _shardCount.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0");
                    // Part of cache validity: a cache built with a different LogTime-in-FTS setting has a
                    // differently-tokenized index, so reuse must reject it (EvaluateCacheReuse).
                    WriteMetaKey(tx, "fts_logtime",    IndexLogTimeInFts ? "1" : "0");
                    WriteMetaKey(tx, "completed_at",   DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
                    tx.Commit();
                    FindPluginCore.Diagnostics.PerfLog.Log("cache.write", ("ok", true), ("size", size), ("path_len", sourcePath.Length));
                    // A new cache entry just completed — enforce the size ceiling now (off-thread), not
                    // only at the next startup, so a long session can't grow the cache past the cap.
                    CachedStorage.PruneInBackground();
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
        /// <summary>
        /// Merge the FilteredResults rows from other SqliteStorage cache DB files into this store, via
        /// ATTACH + INSERT…SELECT (SQLite-internal row copy — no managed binding). Used by the parallel
        /// fan-out ingest to consolidate per-shard stores into one queryable DB. The Id column IS copied
        /// (not re-assigned): the fan-out stamps each shard row with its global scan position via
        /// <see cref="AddFilteredBatchWithIds"/>, so copying Id verbatim makes the default ORDER BY Id ASC
        /// reproduce true scan order even though rows were written out of order across shards. This store
        /// must be empty before merging (the caller scans into shards, not here). Recomputes the row count
        /// and invalidates the level-count cache (the copy bypasses BumpLevelCount). Returns rows merged.
        /// </summary>
        public long MergeFilteredFrom(IReadOnlyList<string> shardDbPaths)
        {
            if (shardDbPaths == null || shardDbPaths.Count == 0) return 0;
            lock (_sync)
            {
                // Copy every column INCLUDING Id (shards carry the global scan-order Id; preserve it).
                var cols = new List<string>();
                using (var ti = _connection.CreateCommand())
                {
                    ti.CommandText = "PRAGMA table_info(FilteredResults)";
                    using var r = ti.ExecuteReader();
                    while (r.Read()) cols.Add(r.GetString(1));
                }
                string colList = string.Join(",", cols);

                // The fan-out copies global Ids verbatim (so the viewer's default ORDER BY Id reproduces scan
                // order), which requires the target to be empty — Step2 / the caller ClearTables() first. This
                // asserts that invariant rather than silently colliding on a populated cache.
                long before = GetCount("FilteredResults");
                if (before != 0)
                    throw new InvalidOperationException(
                        $"MergeFilteredFrom requires an empty target but it has {before:N0} rows — the fan-out " +
                        "merge copies global Ids verbatim, so a non-empty target collides. Caller must ClearTables first.");

                // ATTACH each shard and copy its rows into the target (Id included → global scan order is
                // preserved; the viewer's default ORDER BY Id sorts on it regardless of physical layout).
                using (var tx = _connection.BeginTransaction())
                {
                    for (int k = 0; k < shardDbPaths.Count; k++)
                    {
                        using (var a = _connection.CreateCommand()) { a.Transaction = tx; a.CommandText = $"ATTACH DATABASE '{shardDbPaths[k].Replace("'", "''")}' AS msrc{k};"; a.ExecuteNonQuery(); }
                        using (var ins = _connection.CreateCommand()) { ins.Transaction = tx; ins.CommandText = $"INSERT INTO main.FilteredResults ({colList}) SELECT {colList} FROM msrc{k}.FilteredResults"; ins.ExecuteNonQuery(); }
                    }
                    tx.Commit();
                }

                for (int k = 0; k < shardDbPaths.Count; k++)
                    try { using var d = _connection.CreateCommand(); d.CommandText = $"DETACH DATABASE msrc{k};"; d.ExecuteNonQuery(); } catch { }

                _filteredCount = GetCount("FilteredResults");
                _levelCountsValid = false; // INSERT…SELECT bypassed BumpLevelCount — recompute lazily from SQL
                return _filteredCount - before;
            }
        }

        public void ClearTables()
        {
            lock (_sync)
            {
                // Sharded FTS lives in separate files attached to this connection — detach + delete them
                // so a wiped cache doesn't leave stale shards behind.
                DetachAndDeleteShards();

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

                // Tables are now empty — keep the running counts in sync. The level map is exact
                // again (all zeros) and a fresh scan will increment it per insert.
                _rawCount = 0;
                _filteredCount = 0;
                Array.Clear(_levelCounts, 0, _levelCounts.Length);
                _levelCountsValid = true;
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
        /// Insert a filtered batch assigning each row an explicit Id starting at <paramref name="baseId"/>
        /// (baseId, baseId+1, …). Used only by the parallel fan-out ingest: the single producer hands each
        /// shard a batch already stamped with its GLOBAL scan position, so after the shards merge into one
        /// DB the default ORDER BY Id ASC reproduces true scan order despite the rows having been written
        /// out of order across N shards. Same prepared-insert fast path as <see cref="AddFilteredBatch"/>.
        /// </summary>
        public void AddFilteredBatchWithIds(IEnumerable<ISearchResult> batch, long baseId, CancellationToken cancellationToken = default)
        {
            if (batch == null) throw new ArgumentNullException(nameof(batch));
            int inserted;
            lock (_sync)
            {
                using var transaction = _connection.BeginTransaction();
                BulkInsert("FilteredResults", batch, transaction, cancellationToken, out inserted, onProgress: null, baseId: baseId);
                transaction.Commit();
                _filteredCount += inserted;
                if (inserted > 0) _ftsIndexBuilt = false;
            }
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
            Action<int> onProgress = null,
            long? baseId = null)
        {
            inserted = 0;

            // One prepared single-row INSERT, reused for every row inside the caller's transaction.
            // Counterintuitively this is ~28× faster than a 500-row multi-VALUES statement here:
            // Microsoft.Data.Sqlite's per-parameter bind cost makes a 5000-parameter chunk far more
            // expensive than 500 cheap single-row binds. (Benchmarked: ~370k rows/s vs ~13k rows/s.)
            // Maintain the running level map only for the filtered table (what the viewer reads).
            // BindAndExecute has just set p.Level.Value, so read it back rather than re-deriving.
            // baseId set ⇒ assign each row an explicit Id (baseId, baseId+1, …) instead of letting
            // AUTOINCREMENT pick it. The parallel fan-out ingest uses this to stamp each shard row with
            // its GLOBAL scan position, so the merged DB's default ORDER BY Id ASC = true scan order.
            bool trackLevels = string.Equals(table, "FilteredResults", StringComparison.Ordinal);
            bool withId = baseId.HasValue;
            using var cmd = CreatePreparedInsert(table, tx, out var p, withId);
            foreach (var result in batch)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (withId) p.Id.Value = baseId.Value + inserted;
                BindAndExecute(cmd, p, result);
                if (trackLevels) BumpLevelCount(p.Level.Value);
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
            public SqliteParameter Id; // only bound when CreatePreparedInsert(withId:true) — the fan-out shard path
            public SqliteParameter LogTime, MachineName, Level, Username, TaskName,
                                   OpCode, Source, SearchableData, Message, ResultSource,
                                   ProcessId, ThreadId, ActivityId,
                                   EventId, Keywords, RelatedActivityId, Channel, ProviderGuid, RecordId,
                                   ProcessName, StructuredData;
        }

        /// <summary>
        /// Build a SqliteCommand whose statement is parsed and bound exactly once. Callers reuse
        /// it across all rows in one batch by mutating the returned <see cref="InsertParams"/>
        /// values and calling <c>ExecuteNonQuery</c> per row.
        /// </summary>
        private SqliteCommand CreatePreparedInsert(string table, SqliteTransaction tx, out InsertParams p, bool withId = false)
        {
            var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            // withId prepends an explicit Id column (the fan-out shard path stamps global scan position).
            string idCol = withId ? "Id, " : "";
            string idVal = withId ? "@Id, " : "";
            cmd.CommandText = $@"
                INSERT INTO {table}
                ({idCol}LogTime, MachineName, Level, Username, TaskName, OpCode, Source, SearchableData, Message, ResultSource, ProcessId, ThreadId, ActivityId, EventId, Keywords, RelatedActivityId, Channel, ProviderGuid, RecordId, ProcessName, StructuredData)
                VALUES ({idVal}@LogTime, @MachineName, @Level, @Username, @TaskName, @OpCode, @Source, @SearchableData, @Message, @ResultSource, @ProcessId, @ThreadId, @ActivityId, @EventId, @Keywords, @RelatedActivityId, @Channel, @ProviderGuid, @RecordId, @ProcessName, @StructuredData)";

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
                ProcessId      = cmd.Parameters.Add("@ProcessId",      SqliteType.Text),
                ThreadId       = cmd.Parameters.Add("@ThreadId",       SqliteType.Text),
                ActivityId     = cmd.Parameters.Add("@ActivityId",     SqliteType.Text),
                EventId           = cmd.Parameters.Add("@EventId",           SqliteType.Text),
                Keywords          = cmd.Parameters.Add("@Keywords",          SqliteType.Text),
                RelatedActivityId = cmd.Parameters.Add("@RelatedActivityId", SqliteType.Text),
                Channel           = cmd.Parameters.Add("@Channel",           SqliteType.Text),
                ProviderGuid      = cmd.Parameters.Add("@ProviderGuid",      SqliteType.Text),
                RecordId          = cmd.Parameters.Add("@RecordId",          SqliteType.Text),
                ProcessName       = cmd.Parameters.Add("@ProcessName",       SqliteType.Text),
                StructuredData    = cmd.Parameters.Add("@StructuredData",    SqliteType.Text),
            };
            if (withId) p.Id = cmd.Parameters.Add("@Id", SqliteType.Integer);
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
            p.ProcessId.Value      = r.GetProcessId() ?? "";
            p.ThreadId.Value       = r.GetThreadId() ?? "";
            p.ActivityId.Value     = r.GetActivityId() ?? "";
            p.EventId.Value           = r.GetEventId() ?? "";
            p.Keywords.Value          = r.GetKeywords() ?? "";
            p.RelatedActivityId.Value = r.GetRelatedActivityId() ?? "";
            p.Channel.Value           = r.GetChannel() ?? "";
            p.ProviderGuid.Value      = r.GetProviderGuid() ?? "";
            p.RecordId.Value          = r.GetRecordId() ?? "";
            p.ProcessName.Value       = r.GetProcessName() ?? "";
            p.StructuredData.Value    = r.GetStructuredData() ?? "";
            cmd.ExecuteNonQuery();
        }

        /// <summary>Increment the running level map for one just-inserted filtered row. Called under
        /// _sync (inside BulkInsert). A level outside the array invalidates the map so reads fall back
        /// to SQL rather than under-count.</summary>
        private void BumpLevelCount(object levelBoxed)
        {
            if (!_levelCountsValid) return;
            int lvl;
            try { lvl = Convert.ToInt32(levelBoxed); }
            catch { _levelCountsValid = false; return; }
            if ((uint)lvl < (uint)_levelCounts.Length) _levelCounts[lvl]++;
            else _levelCountsValid = false;
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
                cmd.CommandText = $"SELECT LogTime, MachineName, Level, Username, TaskName, OpCode, Source, SearchableData, Message, ResultSource, Id, ProcessId, ThreadId, ActivityId, EventId, Keywords, RelatedActivityId, Channel, ProviderGuid, RecordId, ProcessName, StructuredData FROM {table}";
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
            // Multi-select OR-sets (exact match). Non-empty → "column IN (...)"; takes precedence over
            // the matching substring field above. ProviderSet→Source, SourceSet→ResultSource.
            public IReadOnlyList<string> ProviderSet;
            public IReadOnlyList<string> TaskNameSet;
            public IReadOnlyList<string> SourceSet;
            public FindPluginCore.Searching.Query.QueryNode Query; // structured search-box query (optional)
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
            // Fast path: no filter predicates → the count is the running total we already maintain
            // (volatile, lock-free, identical to GetStatistics().filteredRecordCount). This avoids
            // both a full COUNT(*) scan AND taking _sync — which during a streaming search would
            // otherwise block behind the writer's bulk-insert transactions. The viewer's first-page
            // count waiting on that lock was a multi-second stall when opening a 1.45M-row folder.
            // Slightly stale (may omit an in-flight uncommitted batch) the same way GetStatistics is;
            // the viewer refreshes on FilteredRowsAdded, so the count converges.
            if (IsEmptyFilter(filter)) return _filteredCount;

            lock (_sync)
            {
                var (where, ps) = BuildWhere(filter, UseFts, _shardCount);
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
                var (where, ps) = BuildWhere(filter, UseFts, _shardCount);
                var orderBy = BuildOrderBy(sort);
                using var cmd = _connection.CreateCommand();
                cmd.CommandText =
                    "SELECT LogTime, MachineName, Level, Username, TaskName, OpCode, Source, " +
                    "SearchableData, Message, ResultSource, Id, ProcessId, ThreadId, ActivityId, " +
                    "EventId, Keywords, RelatedActivityId, Channel, ProviderGuid, RecordId, ProcessName, StructuredData FROM FilteredResults " +
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

        /// <summary>
        /// The LAST page of the filter+sort result, fetched WITHOUT a deep OFFSET. Jumping to the last
        /// page of a multi-million-row set with <c>LIMIT n OFFSET ~rows</c> made SQLite scan every skipped
        /// row (~10s). Instead run the query with the sort direction flipped, take the first
        /// <paramref name="limit"/> rows (OFFSET 0 — O(limit)), then reverse them back into forward order.
        /// Equivalent rows, O(pageSize) instead of O(rows).
        /// </summary>
        public List<ISearchResult> GetLastFilteredPage(FilterInput filter, SortInput sort, int limit)
        {
            const string select =
                "SELECT LogTime, MachineName, Level, Username, TaskName, OpCode, Source, " +
                "SearchableData, Message, ResultSource, Id, ProcessId, ThreadId, ActivityId, " +
                "EventId, Keywords, RelatedActivityId, Channel, ProviderGuid, RecordId, ProcessName, StructuredData FROM FilteredResults ";
            lock (_sync)
            {
                List<ISearchResult> Run(bool ftsAvailable)
                {
                    var (where, ps) = BuildWhere(filter, ftsAvailable, _shardCount);
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = select + $"{where} {BuildOrderByFlipped(sort)} LIMIT @limit";
                    BindParams(cmd, ps);
                    cmd.Parameters.AddWithValue("@limit", limit);
                    var list = new List<ISearchResult>();
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read()) list.Add(new SqliteSearchResult(reader));
                    list.Reverse(); // fetched in reverse order; flip back so the page reads forward
                    return list;
                }
                try { return Run(UseFts); }
                catch (SqliteException) when (UseFts && HasGlobalSearch(filter)) { return Run(false); }
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
                "SearchableData, Message, ResultSource, Id FROM FilteredResults " +
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

        /// <summary>True when the filter has no predicates, so a query over it spans the whole
        /// FilteredResults table. Lets count/level queries short-circuit to the maintained running
        /// total instead of scanning. Mirrors the conditions BuildWhere would emit.</summary>
        private static bool HasSet(IReadOnlyList<string> s) => s != null && s.Count > 0;

        private static bool IsEmptyFilter(FilterInput f)
            => f == null || (
                string.IsNullOrEmpty(f.Search) &&
                string.IsNullOrEmpty(f.Provider) &&
                string.IsNullOrEmpty(f.TaskName) &&
                string.IsNullOrEmpty(f.Message) &&
                string.IsNullOrEmpty(f.Source) &&
                !HasSet(f.ProviderSet) && !HasSet(f.TaskNameSet) && !HasSet(f.SourceSet) &&
                f.Query == null &&
                !f.LevelInt.HasValue &&
                string.IsNullOrEmpty(f.LogTimeFrom) &&
                string.IsNullOrEmpty(f.LogTimeTo));

        /// <summary>
        /// Fetch a single filtered row by its stable <c>FilteredResults.Id</c>, independent of any
        /// filter/sort/paging. Returns null if no such row exists. Used for record lookup
        /// (e.g. the MCP <c>get_record</c> tool and row tagging by stable id).
        /// </summary>
        public ISearchResult GetById(long id)
        {
            lock (_sync)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText =
                    "SELECT LogTime, MachineName, Level, Username, TaskName, OpCode, Source, " +
                    "SearchableData, Message, ResultSource, Id, ProcessId, ThreadId, ActivityId, " +
                    "EventId, Keywords, RelatedActivityId, Channel, ProviderGuid, RecordId, ProcessName, StructuredData FROM FilteredResults WHERE Id = @id LIMIT 1";
                cmd.Parameters.AddWithValue("@id", id);
                using var reader = cmd.ExecuteReader();
                return reader.Read() ? new SqliteSearchResult(reader) : null;
            }
        }

        public List<int> GetDistinctLevels()
        {
            // Fast path: the running level map knows exactly which levels are present, lock-free —
            // so the viewer's load doesn't take _sync and stall behind the streaming writer.
            if (_levelCountsValid)
            {
                var present = new List<int>();
                for (int i = 0; i < _levelCounts.Length; i++)
                    if (_levelCounts[i] > 0) present.Add(i); // ascending, mirrors ORDER BY Level
                return present;
            }
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
            // Fast path: no filter + a valid running map → return per-level totals without a GROUP BY
            // scan and without taking _sync (avoids the streaming-writer contention on a cold load).
            if (IsEmptyFilter(filter) && _levelCountsValid)
            {
                var d = new Dictionary<int, int>();
                for (int i = 0; i < _levelCounts.Length; i++)
                    if (_levelCounts[i] > 0) d[i] = _levelCounts[i];
                return d;
            }
            lock (_sync)
            {
                var (where, ps) = BuildWhere(filter, UseFts, _shardCount);
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

        /// <summary>Distinct ResultSource (the viewer's "Source" column = each row's file/origin) values
        /// with row counts, unfiltered. One GROUP BY scan — no row materialization — so it's cheap even
        /// on million-row tables (the Sources dialog uses it instead of an O(rows) facet scan).</summary>
        public Dictionary<string, int> GetSourceCounts()
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            lock (_sync)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT ResultSource, COUNT(*) FROM FilteredResults GROUP BY ResultSource";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var src = reader.IsDBNull(0) ? "" : reader.GetString(0);
                    counts[src] = reader.GetInt32(1);
                }
            }
            return counts;
        }

        /// <summary>Distinct values + counts of one known-filter field over the rows matching
        /// <paramref name="filter"/> — a single GROUP BY (no row materialization), so the viewer's
        /// "known value" dropdowns populate fast even on million-row tables instead of scanning a
        /// 200k-row sample. The viewer field maps to its column: Provider→Source, Source→ResultSource,
        /// TaskName→TaskName. Returns empty for any other field.</summary>
        public Dictionary<string, int> GetFieldCounts(string viewerField, FilterInput filter)
        {
            var col = (viewerField ?? "").Trim().ToLowerInvariant() switch
            {
                "provider" => "Source",
                "taskname" or "task" => "TaskName",
                "source" => "ResultSource",
                _ => null,
            };
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (col == null) return counts;
            lock (_sync)
            {
                void Run(bool ftsAvailable)
                {
                    counts.Clear();
                    var (where, ps) = BuildWhere(filter, ftsAvailable, _shardCount);
                    using var cmd = _connection.CreateCommand();
                    cmd.CommandText = $"SELECT {col}, COUNT(*) FROM FilteredResults {where} GROUP BY {col}";
                    BindParams(cmd, ps);
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                        counts[reader.IsDBNull(0) ? "" : reader.GetString(0)] = reader.GetInt32(1);
                }
                try { Run(UseFts); }
                catch (SqliteException) when (UseFts && HasGlobalSearch(filter)) { Run(false); }
            }
            return counts;
        }

        // ----- WHERE / ORDER BY builders -----

        // Trigram FTS5 needs at least 3 characters in the query to generate any trigrams. Anything
        // shorter would return zero results, so the global search falls back to LIKE for those.
        private const int TrigramMinLength = 3;

        private static (string clause, List<KeyValuePair<string, object>> parameters) BuildWhere(FilterInput f, bool ftsAvailable, int shardCount = 0)
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

            // Exact-match OR-set: "column IN (@p0, @p1, …)". COLLATE NOCASE keeps it case-insensitive
            // like the substring path. Values come verbatim from the data's facets, so they match.
            void AddInSet(string column, IReadOnlyList<string> vals)
            {
                if (!HasSet(vals)) return;
                var names = new List<string>(vals.Count);
                foreach (var v in vals)
                {
                    var name = "@p" + (idx++);
                    names.Add(name);
                    ps.Add(new KeyValuePair<string, object>(name, v ?? ""));
                }
                conditions.Add($"{column} COLLATE NOCASE IN ({string.Join(", ", names)})");
            }

            if (f != null)
            {
                // A set takes precedence over the substring field for that column.
                AddLike("Source",         HasSet(f.ProviderSet) ? null : f.Provider);  // viewer.Provider == SQL.Source
                AddLike("TaskName",       HasSet(f.TaskNameSet) ? null : f.TaskName);
                AddLike("Message",        f.Message);
                AddLike("ResultSource",   HasSet(f.SourceSet) ? null : f.Source);      // viewer.Source == SQL.ResultSource
                AddInSet("Source",        f.ProviderSet);
                AddInSet("TaskName",      f.TaskNameSet);
                AddInSet("ResultSource",  f.SourceSet);

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
                        if (shardCount > 0)
                        {
                            // Union the per-shard MATCH (Id ranges are disjoint → UNION ALL, no dupes).
                            // FTS5 MATCH needs the bare unique table name (fts0/fts1/…), not schema-qualified.
                            var union = string.Join(" UNION ALL ", Enumerable.Range(0, shardCount)
                                .Select(k => $"SELECT rowid FROM shard{k}.fts{k} WHERE fts{k} MATCH {name}"));
                            conditions.Add($"Id IN ({union})");
                        }
                        else
                        {
                            conditions.Add($"Id IN (SELECT rowid FROM FilteredResults_fts WHERE FilteredResults_fts MATCH {name})");
                        }
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

                // Structured search-box query (its own @q… params, no collision with @p… above).
                if (f.Query != null)
                {
                    var qctx = new FindPluginCore.Searching.Query.QuerySqlContext();
                    conditions.Add(f.Query.AppendSql(qctx));
                    foreach (var kv in qctx.Parameters) ps.Add(kv);
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

        /// <summary>Same column mapping as <see cref="BuildOrderBy"/> but with the direction reversed —
        /// including load order (Id), which BuildOrderBy always emits ASC. Used by
        /// <see cref="GetLastFilteredPage"/> to fetch the tail without a deep OFFSET.</summary>
        private static string BuildOrderByFlipped(SortInput s)
        {
            if (s == null || string.IsNullOrEmpty(s.Column)) return "ORDER BY Id DESC"; // reverse of load order
            var dir = s.Descending ? "ASC" : "DESC"; // flipped
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
                    // Detach (don't delete) shard files so the next session can warm-reuse them; just
                    // release the attach handles before closing. Eviction is handled by the cache pruner.
                    if (_shardCount > 0) { try { DetachShards(); } catch { } }
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
            private readonly long _id;
            private readonly string _processId;
            private readonly string _threadId;
            private readonly string _activityId;
            private readonly string _eventId;
            private readonly string _keywords;
            private readonly string _relatedActivityId;
            private readonly string _channel;
            private readonly string _providerGuid;
            private readonly string _recordId;
            private readonly string _processName;
            private readonly string _structuredData;

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
                // Id then ProcessId/ThreadId/ActivityId are appended after the base columns. Guard each
                // by FieldCount so a reader from an older SELECT still constructs (id stays -1, ids "").
                _id = record.FieldCount > 10 && !record.IsDBNull(10) ? record.GetInt64(10) : -1;
                _processId  = record.FieldCount > 11 && !record.IsDBNull(11) ? record.GetString(11) : "";
                _threadId   = record.FieldCount > 12 && !record.IsDBNull(12) ? record.GetString(12) : "";
                _activityId = record.FieldCount > 13 && !record.IsDBNull(13) ? record.GetString(13) : "";
                _eventId           = record.FieldCount > 14 && !record.IsDBNull(14) ? record.GetString(14) : "";
                _keywords          = record.FieldCount > 15 && !record.IsDBNull(15) ? record.GetString(15) : "";
                _relatedActivityId = record.FieldCount > 16 && !record.IsDBNull(16) ? record.GetString(16) : "";
                _channel           = record.FieldCount > 17 && !record.IsDBNull(17) ? record.GetString(17) : "";
                _providerGuid      = record.FieldCount > 18 && !record.IsDBNull(18) ? record.GetString(18) : "";
                _recordId          = record.FieldCount > 19 && !record.IsDBNull(19) ? record.GetString(19) : "";
                _processName       = record.FieldCount > 20 && !record.IsDBNull(20) ? record.GetString(20) : "";
                _structuredData    = record.FieldCount > 21 && !record.IsDBNull(21) ? record.GetString(21) : "";
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
            public long GetRowId() => _id;
            public string GetProcessId() => _processId;
            public string GetThreadId() => _threadId;
            public string GetActivityId() => _activityId;
            public string GetEventId() => _eventId;
            public string GetKeywords() => _keywords;
            public string GetRelatedActivityId() => _relatedActivityId;
            public string GetChannel() => _channel;
            public string GetProviderGuid() => _providerGuid;
            public string GetRecordId() => _recordId;
            public string GetProcessName() => _processName;
            public string GetStructuredData() => _structuredData;
        }
    }
}
