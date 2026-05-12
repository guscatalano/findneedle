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

        /// <summary>
        /// Fires after each <see cref="AddFilteredBatch"/> commits. The argument is the number of
        /// rows that just landed (the batch size, not the running total). Used by the streaming
        /// result viewer to refresh its row count and visible page without polling.
        /// </summary>
        public event Action<int>? FilteredRowsAdded;

        /// <summary>
        /// Constructs a SqliteStorage for the given file being searched, storing the DB in AppData cache.
        /// </summary>
        /// <param name="searchedFilePath">The path of the file being searched.</param>
        public SqliteStorage(string searchedFilePath)
        {
            SQLitePCL.Batteries.Init(); // Ensure SQLite provider is initialized
            _dbPath = CachedStorage.GetCacheFilePath(searchedFilePath, ".db");
            OpenAndInitialize(allowRetryAfterCorruption: true);
        }

        /// <summary>
        /// Open the connection, apply pragmas, create the schema, and wipe any leftover rows. If
        /// any of that throws <c>SQLITE_CORRUPT</c> (error code 11, "database disk image is
        /// malformed"), the file is left over from a prior process crash — our pragmas
        /// (<c>journal_mode=MEMORY</c> + <c>synchronous=OFF</c>) trade durability for throughput,
        /// so a crash mid-write can leave the file unreadable. We can't repair it, but since this
        /// file is a cache that's wiped every construction anyway, we delete it and start fresh.
        /// </summary>
        private void OpenAndInitialize(bool allowRetryAfterCorruption)
        {
            _connection = new SqliteConnection($"Data Source={_dbPath}");
            _connection.Open();
            try
            {
                ApplyBulkInsertPragmas();
                InitializeSchema();
                // Cache files are keyed only by file path (no content hash / mtime), so a stale
                // cache would silently surface old or duplicated rows on every reopen. Wipe both
                // tables on construction — each new SqliteStorage instance starts from a clean
                // slate.
                ClearTables();
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
        private void InitializeFts5()
        {
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

                    CREATE TRIGGER IF NOT EXISTS FilteredResults_ai AFTER INSERT ON FilteredResults
                    BEGIN
                        INSERT INTO FilteredResults_fts
                            (rowid, Source, TaskName, Message, ResultSource, SearchableData, LogTime)
                        VALUES
                            (new.Id, new.Source, new.TaskName, new.Message,
                             new.ResultSource, new.SearchableData, new.LogTime);
                    END;

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

                // Recreate the FTS5 table + triggers fresh.
                InitializeFts5();
            }
        }

        /// <summary>
        /// Rows per multi-row INSERT statement. 500 × 10 columns = 5000 parameters, well under
        /// SQLite's default <c>SQLITE_MAX_VARIABLE_NUMBER</c> (32766). Trades a slightly larger
        /// prepared statement (~30 KB SQL string, built once per batch) for a 100–500× reduction
        /// in managed↔native interop crossings vs. single-row inserts.
        /// </summary>
        private const int InsertChunkRows = 500;
        private const int InsertColumnsPerRow = 10;

        public void AddRawBatch(IEnumerable<ISearchResult> batch, CancellationToken cancellationToken = default)
        {
            if (batch == null) throw new ArgumentNullException(nameof(batch));
            lock (_sync)
            {
                using var transaction = _connection.BeginTransaction();
                BulkInsert("RawResults", batch, transaction, cancellationToken, out _);
                transaction.Commit();
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

            using var chunkCmd = CreatePreparedMultiInsert(table, InsertChunkRows, tx, out var chunkParams);
            using var singleCmd = CreatePreparedInsert(table, tx, out var singleParams);

            // Reusable row buffer — never allocates beyond the initial array.
            var buffer = new ISearchResult[InsertChunkRows];
            int bufferCount = 0;

            foreach (var result in batch)
            {
                if (cancellationToken.IsCancellationRequested) break;
                buffer[bufferCount++] = result;
                if (bufferCount == InsertChunkRows)
                {
                    BindAndExecuteChunk(chunkCmd, chunkParams, buffer, InsertChunkRows);
                    inserted += InsertChunkRows;
                    bufferCount = 0;
                    // Fire progress mid-transaction so the caller can update its status text.
                    // The lock is still held; subscribers must not call back into storage from
                    // here (we just pass a count — that's the contract).
                    onProgress?.Invoke(inserted);
                }
            }

            // Tail (anything fewer than InsertChunkRows left over). For a 500k-row search with
            // chunks of 500 this is at worst ~499 single-row inserts — <0.1% of the total — so
            // building a second prepared command sized to the exact tail isn't worth the parse cost.
            for (int i = 0; i < bufferCount; i++)
            {
                BindAndExecute(singleCmd, singleParams, buffer[i]);
                inserted++;
            }
            if (bufferCount > 0) onProgress?.Invoke(inserted);
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

        /// <summary>
        /// Build a single prepared statement of the form
        /// <c>INSERT INTO T (…cols…) VALUES (?,?,…),(?,?,…),…</c> with <paramref name="rowCount"/>
        /// row tuples, so one <c>ExecuteNonQuery</c> commits all of them. Returns a flat array of
        /// the parameter handles in row-major order: index <c>row*10 + column</c>.
        /// </summary>
        private SqliteCommand CreatePreparedMultiInsert(string table, int rowCount, SqliteTransaction tx, out SqliteParameter[] flatParams)
        {
            var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;

            // Build the SQL: ~60 bytes/tuple at 500 rows ≈ 30 KB. Cheap.
            var sb = new System.Text.StringBuilder(rowCount * 64 + 200);
            sb.Append("INSERT INTO ").Append(table).Append(
                " (LogTime, MachineName, Level, Username, TaskName, OpCode, Source, SearchableData, Message, ResultSource) VALUES ");

            flatParams = new SqliteParameter[rowCount * InsertColumnsPerRow];
            for (int r = 0; r < rowCount; r++)
            {
                if (r > 0) sb.Append(',');
                sb.Append('(');
                int baseIdx = r * InsertColumnsPerRow;
                for (int c = 0; c < InsertColumnsPerRow; c++)
                {
                    if (c > 0) sb.Append(',');
                    string name = "@p" + (baseIdx + c).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    sb.Append(name);
                    // Column 2 = Level (INTEGER). Everything else is TEXT.
                    var type = c == 2 ? SqliteType.Integer : SqliteType.Text;
                    flatParams[baseIdx + c] = cmd.Parameters.Add(name, type);
                }
                sb.Append(')');
            }
            cmd.CommandText = sb.ToString();
            cmd.Prepare();
            return cmd;
        }

        /// <summary>
        /// Bind <paramref name="count"/> rows worth of parameters from <paramref name="rows"/>
        /// into the flat parameter array and fire a single <c>ExecuteNonQuery</c>. Caller must
        /// have built <paramref name="cmd"/>/<paramref name="flatParams"/> for the exact row
        /// count (use <see cref="CreatePreparedMultiInsert"/>).
        /// </summary>
        private static void BindAndExecuteChunk(
            SqliteCommand cmd,
            SqliteParameter[] flatParams,
            ISearchResult[] rows,
            int count)
        {
            for (int i = 0; i < count; i++)
            {
                var r = rows[i];
                int b = i * InsertColumnsPerRow;
                flatParams[b + 0].Value = r.GetLogTime().ToString("o");
                flatParams[b + 1].Value = r.GetMachineName() ?? "";
                flatParams[b + 2].Value = (int)r.GetLevel();
                flatParams[b + 3].Value = r.GetUsername() ?? "";
                flatParams[b + 4].Value = r.GetTaskName() ?? "";
                flatParams[b + 5].Value = r.GetOpCode() ?? "";
                flatParams[b + 6].Value = r.GetSource() ?? "";
                flatParams[b + 7].Value = r.GetSearchableData() ?? "";
                flatParams[b + 8].Value = r.GetMessage() ?? "";
                flatParams[b + 9].Value = r.GetResultSource() ?? "";
            }
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
                var (where, ps) = BuildWhere(filter, _ftsAvailable);
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM FilteredResults {where}";
                BindParams(cmd, ps);
                try
                {
                    return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
                }
                catch (SqliteException) when (_ftsAvailable && HasGlobalSearch(filter))
                {
                    return GetFilteredCountLikeFallback(filter);
                }
            }
        }

        public List<ISearchResult> GetFilteredPage(FilterInput filter, SortInput sort, int offset, int limit)
        {
            lock (_sync)
            {
                var (where, ps) = BuildWhere(filter, _ftsAvailable);
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
                catch (SqliteException) when (_ftsAvailable && HasGlobalSearch(filter))
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
                var (where, ps) = BuildWhere(filter, _ftsAvailable);
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
                catch (SqliteException) when (_ftsAvailable && HasGlobalSearch(filter))
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
            int rawCount;
            int filteredCount;
            long sizeOnDisk = 0;
            lock (_sync)
            {
                rawCount = GetCount("RawResults");
                filteredCount = GetCount("FilteredResults");
                if (File.Exists(_dbPath))
                    sizeOnDisk = new FileInfo(_dbPath).Length;
            }
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
