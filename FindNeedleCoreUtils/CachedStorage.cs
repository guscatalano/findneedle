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
