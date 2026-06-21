using System;
using System.IO;
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
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FindNeedle", "Cache");

        static CachedStorage()
        {
            Directory.CreateDirectory(AppDataCacheDir);
        }

        /// <summary>The directory where cache files live (created on first use).</summary>
        public static string CacheDirectory => AppDataCacheDir;

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
