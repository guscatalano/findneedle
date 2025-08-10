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
        private readonly SqliteConnection _connection;

        /// <summary>
        /// Constructs a SqliteStorage for the given file being searched, storing the DB in AppData cache.
        /// </summary>
        /// <param name="searchedFilePath">The path of the file being searched.</param>
        public SqliteStorage(string searchedFilePath)
        {
            SQLitePCL.Batteries.Init(); // Ensure SQLite provider is initialized
            _dbPath = CachedStorage.GetCacheFilePath(searchedFilePath, ".db");
            _connection = new SqliteConnection($"Data Source={_dbPath}");
            _connection.Open();
            InitializeSchema();
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
            ";
            cmd.ExecuteNonQuery();
        }

        public void AddRawBatch(IEnumerable<ISearchResult> batch, CancellationToken cancellationToken = default)
        {
            using var transaction = _connection.BeginTransaction();
            foreach (var result in batch)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                InsertResult("RawResults", result, transaction);
            }
            transaction.Commit();
        }

        public void AddFilteredBatch(IEnumerable<ISearchResult> batch, CancellationToken cancellationToken = default)
        {
            using var transaction = _connection.BeginTransaction();
            foreach (var result in batch)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                InsertResult("FilteredResults", result, transaction);
            }
            transaction.Commit();
        }

        private void InsertResult(string table, ISearchResult result, SqliteTransaction transaction)
        {
            var cmd = _connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = $@"
                INSERT INTO {table} 
                (LogTime, MachineName, Level, Username, TaskName, OpCode, Source, SearchableData, Message, ResultSource)
                VALUES (@LogTime, @MachineName, @Level, @Username, @TaskName, @OpCode, @Source, @SearchableData, @Message, @ResultSource)";
            cmd.Parameters.AddWithValue("@LogTime", result.GetLogTime().ToString("o"));
            cmd.Parameters.AddWithValue("@MachineName", result.GetMachineName() ?? "");
            cmd.Parameters.AddWithValue("@Level", (int)result.GetLevel());
            cmd.Parameters.AddWithValue("@Username", result.GetUsername() ?? "");
            cmd.Parameters.AddWithValue("@TaskName", result.GetTaskName() ?? "");
            cmd.Parameters.AddWithValue("@OpCode", result.GetOpCode() ?? "");
            cmd.Parameters.AddWithValue("@Source", result.GetSource() ?? "");
            cmd.Parameters.AddWithValue("@SearchableData", result.GetSearchableData() ?? "");
            cmd.Parameters.AddWithValue("@Message", result.GetMessage() ?? "");
            cmd.Parameters.AddWithValue("@ResultSource", result.GetResultSource() ?? "");
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

        public (int rawRecordCount, int filteredRecordCount, long sizeOnDisk, long sizeInMemory) GetStatistics()
        {
            int rawCount = GetCount("RawResults");
            int filteredCount = GetCount("FilteredResults");
            long sizeOnDisk = 0;
            if (File.Exists(_dbPath))
                sizeOnDisk = new FileInfo(_dbPath).Length;
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
            _connection?.Dispose();
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
                _logTime = DateTime.Parse(record.GetString(0));
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
