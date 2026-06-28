using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using FindNeedleCoreUtils;
using FindNeedlePluginLib;
using FindPluginCore.Diagnostics;
using Microsoft.Data.Sqlite;

namespace FindPluginCore.Implementations.Storage
{
    /// <summary>
    /// Parallel fan-out ingest sink for the streaming "just view a big log" path. The scan/decode thread is
    /// the single producer: it calls <see cref="Add"/> with each filtered batch, which is stamped with its
    /// global scan position (a running row counter) and queued to a bounded channel. N consumer tasks each
    /// own one shard <see cref="SqliteStorage"/> and drain the channel concurrently, so the inserts run on N
    /// cores while one thread decodes/wraps events (TraceEvent objects are only valid in-callback, so the
    /// wrap can't be parallelised). When the scan finishes, <see cref="CompleteAndMergeInto"/> waits for the
    /// shard writers, then folds every shard's FilteredResults into the caller's real storage via
    /// <see cref="SqliteStorage.MergeFilteredFrom"/> (ATTACH + INSERT…SELECT) so all queries afterwards run
    /// on one DB. Because each row carries its global Id, the merged DB's default ORDER BY Id ASC reproduces
    /// true scan order even though the rows were written out of order across shards.
    ///
    /// Measured ~2× end-to-end on a 5M-event .etl vs the serial single-writer path; the cap is the per-event
    /// wrap (pinned to the producer thread, contends with the writers for CPU) plus the ~6s merge tail.
    ///
    /// Fail-safe: this path runs only when the parallel-ingest toggle is on
    /// (<see cref="NuSearchQuery"/> gates it); off → the untouched serial streaming insert. Trade: rows are
    /// not queryable until the merge, so the viewer can't fill in live during a parallel load.
    /// </summary>
    public sealed class ParallelIngestSink : IDisposable
    {
        private readonly struct Work
        {
            public readonly List<ISearchResult> Batch;
            public readonly long BaseId;
            public Work(List<ISearchResult> batch, long baseId) { Batch = batch; BaseId = baseId; }
        }

        private readonly SqliteStorage[] _shards;
        private readonly string[] _shardDbPaths;
        private readonly Channel<Work> _channel;
        private readonly Task[] _consumers;
        private readonly CancellationToken _ct;
        private long _seq;       // global scan position assigned to the next row (single producer)
        private bool _disposed;

        public int ShardCount => _shards.Length;
        public long ProducedCount => Interlocked.Read(ref _seq);
        /// <summary>Wall-clock ms spent in the shard→target merge (set by CompleteAndMergeInto). Diagnostics.</summary>
        public long LastMergeMs { get; private set; }

        public ParallelIngestSink(int shardCount, CancellationToken ct)
        {
            if (shardCount < 1) shardCount = 1;
            _ct = ct;
            _shards = new SqliteStorage[shardCount];
            _shardDbPaths = new string[shardCount];
            for (int k = 0; k < shardCount; k++)
            {
                var searched = Path.Combine(Path.GetTempPath(), $"fnshard_{Guid.NewGuid():N}");
                _shardDbPaths[k] = CachedStorage.GetCacheFilePath(searched, ".db");
                var s = new SqliteStorage(searched);
                s.ClearTables();
                _shards[k] = s;
            }
            // 4 batches in flight per shard: enough to keep every writer busy without unbounded RAM.
            _channel = Channel.CreateBounded<Work>(
                new BoundedChannelOptions(Math.Max(4, 4 * shardCount)) { SingleWriter = true, SingleReader = false });

            _consumers = new Task[shardCount];
            for (int k = 0; k < shardCount; k++)
            {
                var shard = _shards[k];
                _consumers[k] = Task.Run(async () =>
                {
                    await foreach (var w in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
                    {
                        if (_ct.IsCancellationRequested) break;
                        shard.AddFilteredBatchWithIds(w.Batch, w.BaseId, _ct);
                    }
                });
            }
        }

        /// <summary>Producer side (scan thread): stamp the batch with its global scan position and enqueue it
        /// for whichever shard writer is free. Blocks on backpressure when the writers fall behind. A
        /// defensive copy of the list container guards against plugins that reuse their batch buffer across
        /// callbacks (the row objects themselves are immutable, so copying refs is cheap and safe).</summary>
        public void Add(List<ISearchResult> batch)
        {
            if (batch == null || batch.Count == 0) return;
            long baseId = _seq + 1;   // Ids are 1-based (AUTOINCREMENT starts at 1); single producer, no lock needed
            _seq += batch.Count;
            var copy = new List<ISearchResult>(batch);
            _channel.Writer.WriteAsync(new Work(copy, baseId), _ct).AsTask().GetAwaiter().GetResult();
        }

        /// <summary>Scan finished: stop accepting work, wait for the shard writers to drain, then merge all
        /// shards into <paramref name="target"/> (the caller's real, empty storage) and delete the shard
        /// files. Returns rows merged. Rethrows the first shard-writer fault if any (the toggle is the
        /// operator's recovery — turn parallel ingest off and re-run on the serial path).</summary>
        public long CompleteAndMergeInto(SqliteStorage target, Action<int, int> onMergeProgress = null)
        {
            _channel.Writer.Complete();
            try { Task.WaitAll(_consumers); }
            catch (AggregateException ae) { throw ae.Flatten().InnerExceptions.FirstOrDefault() ?? ae; }

            // Release shard file handles before ATTACH — pooled connections keep the file open otherwise.
            foreach (var s in _shards) { try { s.Dispose(); } catch { } }
            SqliteConnection.ClearAllPools();

            if (_ct.IsCancellationRequested) { DeleteShardFiles(); return 0; }

            // The fan-out owns the target's final content (the scan went to shards, not the target), so the
            // target MUST be empty for the global-Id merge to not collide. Step2 already ClearTables'd it,
            // but clear again here so the merge is self-guarding even on a re-scan of a populated cache
            // (CacheReuseMode.Never) — MergeFilteredFrom asserts emptiness and would otherwise throw.
            target.ClearTables();

            long merged;
            var mergeWatch = System.Diagnostics.Stopwatch.StartNew();
            using (PerfLog.Scope("ingest.merge", ("shards", _shards.Length), ("rows", ProducedCount)))
                merged = target.MergeFilteredFrom(_shardDbPaths, onMergeProgress);
            LastMergeMs = mergeWatch.ElapsedMilliseconds;
            DeleteShardFiles();
            return merged;
        }

        private void DeleteShardFiles()
        {
            foreach (var db in _shardDbPaths)
                foreach (var p in new[] { db, db + "-wal", db + "-shm", db + "-journal" })
                    try { if (File.Exists(p)) File.Delete(p); } catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _channel.Writer.TryComplete(); } catch { }
            try { Task.WaitAll(_consumers, 2000); } catch { }
            foreach (var s in _shards) { try { s.Dispose(); } catch { } }
            try { SqliteConnection.ClearAllPools(); } catch { }
            DeleteShardFiles();
        }
    }
}
