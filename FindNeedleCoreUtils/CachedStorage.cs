using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FindNeedleCoreUtils
{
    /// <summary>
    /// Utility for storing and retrieving cached search result files (e.g., SQLite DBs) in AppData.
    /// </summary>
    public static class CachedStorage
    {
        private static readonly string AppDataCacheDir = Path.Combine(
            PackagedAppPaths.AppData,
            "FindNeedle", "Cache");

        static CachedStorage()
        {
            Directory.CreateDirectory(AppDataCacheDir);
        }

        /// <summary>The directory where cache files live (created on first use).</summary>
        public static string CacheDirectory => AppDataCacheDir;

        /// <summary>Default cache-size ceiling (10 GB) used when the host doesn't supply one. The cache
        /// is keyed by source path and reused on a hit, but nothing pruned it before — it grew unbounded
        /// (hundreds of GB). <see cref="Prune"/> enforces a ceiling.</summary>
        public const long DefaultMaxCacheBytes = 10L * 1024 * 1024 * 1024;

        /// <summary>Current cache footprint: number of files and total bytes.</summary>
        public static (int files, long bytes) GetCacheStats() => GetStats(AppDataCacheDir);

        /// <summary>Footprint of an arbitrary cache directory (testable overload).</summary>
        public static (int files, long bytes) GetStats(string directory)
        {
            int n = 0; long bytes = 0;
            try
            {
                foreach (var f in Directory.EnumerateFiles(directory))
                {
                    try { bytes += new FileInfo(f).Length; n++; } catch { }
                }
            }
            catch { }
            return (n, bytes);
        }

        /// <summary>
        /// Evict least-recently-written cache files until the cache is under <paramref name="maxBytes"/>
        /// (and <paramref name="maxFiles"/>). Safe to call anytime: a file that's currently open (an
        /// active search's db) can't be deleted on Windows, so the attempt simply fails and that file is
        /// skipped. Best run at startup (nothing open) and after a new cache is written. Returns bytes
        /// freed. LRU uses LastWriteTime (NTFS last-access is usually disabled).
        /// </summary>
        public static long Prune(long maxBytes = DefaultMaxCacheBytes, int maxFiles = int.MaxValue)
            => PruneDirectory(AppDataCacheDir, maxBytes, maxFiles);

        private static int _pruning;

        /// <summary>
        /// Fire-and-forget prune to the default cap, off the calling thread. Coalesces concurrent calls
        /// (a single prune covers writes that land while it runs). Call this after a cache write so the
        /// ceiling is enforced continuously within a session — not only at the next startup, where a long
        /// session opening several large files could otherwise blow far past the cap unbounded.
        /// </summary>
        public static void PruneInBackground(long maxBytes = DefaultMaxCacheBytes)
        {
            if (System.Threading.Interlocked.Exchange(ref _pruning, 1) == 1) return; // one already queued
            System.Threading.Tasks.Task.Run(() =>
            {
                try { Prune(maxBytes); }
                catch { /* best-effort */ }
                finally { System.Threading.Interlocked.Exchange(ref _pruning, 0); }
            });
        }

        /// <summary>The base <c>.db</c> a sidecar/shard file belongs to, or null if it's a primary db /
        /// unrelated. Sidecars: <c>{db}-wal/-shm/-journal</c>; shards: <c>{db}.fts{k}</c>.</summary>
        private static string BaseDbOf(string path)
        {
            foreach (var s in new[] { "-wal", "-shm", "-journal" })
                if (path.EndsWith(s, StringComparison.OrdinalIgnoreCase)) return path.Substring(0, path.Length - s.Length);
            int i = path.IndexOf(".db.fts", StringComparison.OrdinalIgnoreCase);
            if (i >= 0) return path.Substring(0, i + 3); // up to and including ".db"
            return null;
        }

        /// <summary>
        /// Prune an arbitrary cache directory (testable overload; see <see cref="Prune"/>). Shard-aware:
        /// a cache entry is a <c>.db</c> plus its sidecar/shard files; they're sized and evicted together,
        /// and orphaned sidecars/shards (whose <c>.db</c> is gone — e.g. leaked by a killed app) are swept.
        /// </summary>
        public static long PruneDirectory(string directory, long maxBytes, int maxFiles = int.MaxValue)
        {
            long freed = 0;
            try
            {
                var all = new List<FileInfo>();
                foreach (var f in Directory.EnumerateFiles(directory))
                {
                    try { all.Add(new FileInfo(f)); } catch { }
                }

                // Orphan sweep: delete any sidecar/shard whose base .db no longer exists.
                var present = new HashSet<string>(all.Select(f => f.FullName), StringComparer.OrdinalIgnoreCase);
                foreach (var f in all.ToList())
                {
                    var b = BaseDbOf(f.FullName);
                    if (b != null && !present.Contains(b))
                    {
                        try { long l = f.Length; f.Delete(); freed += l; all.Remove(f); } catch { }
                    }
                }

                // Group the rest into cache entries: each .db + its sidecars/shards.
                var sidecars = all.Where(f => BaseDbOf(f.FullName) != null)
                                  .GroupBy(f => BaseDbOf(f.FullName), StringComparer.OrdinalIgnoreCase)
                                  .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
                var dbs = all.Where(f => f.Name.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
                                         && BaseDbOf(f.FullName) == null).ToList();
                long EntrySize(FileInfo db) =>
                    db.Length + (sidecars.TryGetValue(db.FullName, out var sc) ? sc.Sum(x => x.Length) : 0);

                long total = dbs.Sum(EntrySize);
                int count = dbs.Count;
                if (total <= maxBytes && count <= maxFiles) return freed;

                dbs.Sort((a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc)); // oldest first
                foreach (var db in dbs)
                {
                    if (total <= maxBytes && count <= maxFiles) break;
                    long entry = EntrySize(db);
                    try { db.Delete(); }
                    catch { continue; /* open / locked — skip, eligible next time */ }
                    // The .db is gone; delete its sidecars/shards too.
                    if (sidecars.TryGetValue(db.FullName, out var sc))
                        foreach (var x in sc) { try { x.Delete(); } catch { } }
                    freed += entry; total -= entry; count--;
                }
            }
            catch { /* pruning is best-effort, never throw into the caller */ }
            return freed;
        }

        /// <summary>
        /// Gets a unique filename for a cache file based on a one-way hash of the original filename.
        /// </summary>
        public static string GetCacheFileName(string originalFileName, string extension = ".db")
        {
            string hash = GetSha256Hash(originalFileName);
            return hash + extension;
        }

        /// <summary>
        /// Gets the full path for a cached file given the original filename.
        /// </summary>
        public static string GetCacheFilePath(string originalFileName, string extension = ".db")
        {
            string fileName = GetCacheFileName(originalFileName, extension);
            return Path.Combine(AppDataCacheDir, fileName);
        }

        /// <summary>
        /// A cheap change-signature for a search source — works for a single file OR a whole folder.
        /// For a file: its byte length + last-write time, count 1. For a folder: the aggregate file
        /// count, total byte length, and newest last-write time across every file the scan would read
        /// (<see cref="FileIO.GetAllFiles"/> — the same recursive enumeration <c>FolderLocation</c> uses).
        /// Metadata only; never reads file contents. Returns false if <paramref name="path"/> exists as
        /// neither a file nor a directory.
        ///
        /// This is what lets the warm-cache fast path skip a full rescan of a folder: if the folder's
        /// signature matches what was stored when the cache was built, nothing the scan reads has
        /// changed. Enumerating every file (not just the ones a processor parses) means an unrelated
        /// change merely forces a rescan — safe — rather than ever serving stale results.
        /// </summary>
        public static bool TryGetSourceSignature(string path, out long totalSize, out DateTime newestMtimeUtc, out int fileCount)
        {
            totalSize = 0;
            newestMtimeUtc = DateTime.MinValue;
            fileCount = 0;
            if (string.IsNullOrEmpty(path)) return false;

            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                totalSize = fi.Length;
                newestMtimeUtc = fi.LastWriteTimeUtc;
                fileCount = 1;
                return true;
            }
            if (!Directory.Exists(path)) return false;

            foreach (var f in FileIO.GetAllFiles(path))
            {
                try
                {
                    var fi = new FileInfo(f);
                    totalSize += fi.Length;
                    if (fi.LastWriteTimeUtc > newestMtimeUtc) newestMtimeUtc = fi.LastWriteTimeUtc;
                    fileCount++;
                }
                catch { /* unreadable file — the scan would skip it too; leave it out of the signature */ }
            }
            return true;
        }

        /// <summary>True if the path exists as either a file or a directory — the set of sources the
        /// cache fast path can key on.</summary>
        public static bool SourceExists(string path)
            => !string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path));

        /// <summary>
        /// Computes a SHA256 hash of the input string and returns it as a hex string.
        /// </summary>
        private static string GetSha256Hash(string input)
        {
            using var sha256 = SHA256.Create();
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            byte[] hashBytes = sha256.ComputeHash(bytes);
            var sb = new StringBuilder();
            foreach (var b in hashBytes)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
