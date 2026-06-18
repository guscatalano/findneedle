using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FindNeedleCoreUtils;
using Microsoft.Data.Sqlite;

namespace FindPluginCore.Implementations.Storage
{
    /// <summary>One cached search on disk: where it came from, how big it is, and when it was built.</summary>
    public sealed class CachedSearchEntry
    {
        public string DbPath { get; set; } = "";
        public string SourcePath { get; set; } = "";
        public long Rows { get; set; }
        public long SizeOnDiskBytes { get; set; }
        public DateTime? CompletedAt { get; set; }
        public bool FtsBuilt { get; set; }
        public bool SourceExists { get; set; }
    }

    /// <summary>
    /// Lists / manages the on-disk search caches (SQLite .db files in
    /// <see cref="CachedStorage.CacheDirectory"/>). Each cache's source path + build time live in its
    /// <c>_meta</c> table; the row count is <c>COUNT(*) FROM FilteredResults</c>. Read-only and
    /// defensive — a corrupt / in-use cache file is skipped rather than throwing.
    /// </summary>
    public static class CachedSearchCatalog
    {
        public static string Directory => CachedStorage.CacheDirectory;

        /// <summary>Count of cache .db files (cheap — no SQLite opens).</summary>
        public static int CountFiles()
        {
            string dir = CachedStorage.CacheDirectory;
            if (!System.IO.Directory.Exists(dir)) return 0;
            try { return System.IO.Directory.GetFiles(dir, "*.db").Length; }
            catch { return 0; }
        }

        /// <summary>
        /// Read cache metadata for the newest <paramref name="max"/> caches (by file mtime, which is
        /// cheap to sort on before opening each SQLite file). Caps the number of opens so a cache
        /// directory with thousands of entries doesn't take seconds to enumerate.
        /// </summary>
        public static List<CachedSearchEntry> List(int max = 500)
        {
            var result = new List<CachedSearchEntry>();
            string dir = CachedStorage.CacheDirectory;
            if (!System.IO.Directory.Exists(dir)) return result;

            string[] files;
            try { files = System.IO.Directory.GetFiles(dir, "*.db"); }
            catch { return result; }

            var ordered = files
                .OrderByDescending(f => { try { return File.GetLastWriteTimeUtc(f); } catch { return DateTime.MinValue; } })
                .Take(max);

            foreach (var db in ordered)
            {
                var entry = TryRead(db);
                if (entry != null) result.Add(entry);
            }
            // Final ordering by the recorded build time (falls back to mtime order for entries w/o one).
            return result.OrderByDescending(e => e.CompletedAt ?? DateTime.MinValue).ToList();
        }

        private static CachedSearchEntry? TryRead(string dbPath)
        {
            try
            {
                SQLitePCL.Batteries.Init();
                var entry = new CachedSearchEntry { DbPath = dbPath };
                try { entry.SizeOnDiskBytes = new FileInfo(dbPath).Length; } catch { }

                var csb = new SqliteConnectionStringBuilder
                {
                    DataSource = dbPath,
                    Mode = SqliteOpenMode.ReadOnly,
                };
                using var conn = new SqliteConnection(csb.ToString());
                conn.Open();

                // Metadata
                var meta = new Dictionary<string, string>(StringComparer.Ordinal);
                try
                {
                    using var mc = conn.CreateCommand();
                    mc.CommandText = "SELECT Key, Value FROM _meta";
                    using var r = mc.ExecuteReader();
                    while (r.Read()) meta[r.GetString(0)] = r.IsDBNull(1) ? "" : r.GetString(1);
                }
                catch { /* no _meta table — not one of our caches; skip */ return null; }

                meta.TryGetValue("source_path", out var sp);
                entry.SourcePath = sp ?? "";
                entry.FtsBuilt = meta.TryGetValue("fts_built", out var fb) && fb == "1";
                if (meta.TryGetValue("completed_at", out var ca)
                    && DateTime.TryParse(ca, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                    entry.CompletedAt = dt;
                try { entry.SourceExists = !string.IsNullOrEmpty(entry.SourcePath) && File.Exists(entry.SourcePath); } catch { }

                // Row count — use MAX(Id) (O(1) via the integer PK), NOT COUNT(*), which is a full
                // table scan and makes enumerating hundreds of multi-million-row caches hang.
                // FilteredResults is append-only per cache, so MAX(Id) == the row count.
                try
                {
                    using var cc = conn.CreateCommand();
                    cc.CommandText = "SELECT IFNULL(MAX(Id), 0) FROM FilteredResults";
                    cc.CommandTimeout = 5;
                    entry.Rows = Convert.ToInt64(cc.ExecuteScalar() ?? 0L);
                }
                catch { entry.Rows = 0; }

                return entry;
            }
            catch
            {
                return null; // locked / corrupt / not a cache db
            }
        }

        /// <summary>Best-effort delete of a cache .db and its SQLite sidecar files.</summary>
        public static void Delete(string dbPath)
        {
            // Drop pooled handles so the file isn't locked on Windows.
            try { SqliteConnection.ClearAllPools(); } catch { }
            foreach (var p in new[] { dbPath, dbPath + "-wal", dbPath + "-shm", dbPath + "-journal" })
            {
                try { if (File.Exists(p)) File.Delete(p); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Delete every cache file in the directory (fast — no SQLite opens). Returns how many .db
        /// files were removed. Best-effort: files in use are skipped.
        /// </summary>
        public static int DeleteAll()
        {
            string dir = CachedStorage.CacheDirectory;
            if (!System.IO.Directory.Exists(dir)) return 0;
            try { SqliteConnection.ClearAllPools(); } catch { }
            int removed = 0;
            string[] files;
            try { files = System.IO.Directory.GetFiles(dir, "*.db"); }
            catch { return 0; }
            foreach (var db in files)
            {
                bool gone = true;
                foreach (var p in new[] { db, db + "-wal", db + "-shm", db + "-journal" })
                {
                    try { if (File.Exists(p)) File.Delete(p); }
                    catch { if (p == db) gone = false; }
                }
                if (gone) removed++;
            }
            return removed;
        }
    }
}
