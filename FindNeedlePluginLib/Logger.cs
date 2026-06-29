using System;
using System.IO;
using System.Collections.Generic;
using FindNeedleCoreUtils;

namespace FindNeedlePluginLib;

public class Logger
{
    private static readonly Lazy<Logger> _instance = new(() => new Logger());
    public static Logger Instance => _instance.Value;

    private readonly string logFilePath;
    public Action<string>? LogCallback { get; set; }
    private readonly List<string> _logCache = new();
    private readonly object _sync = new();
    // Keep memory bounded over a long session AND keep the LogsPage load snappy.
    private const int MaxCachedLines = 5000;
    // Return a SNAPSHOT (copy taken under the lock), not a live view — consumers (e.g. LogsPage) iterate it
    // while background threads keep logging, so a live AsReadOnly() wrapper threw "Collection was modified".
    public IReadOnlyList<string> LogCache { get { lock (_sync) { return _logCache.ToArray(); } } }

    private Logger()
    {
        var folder = FileIO.GetAppDataFindNeedlePluginFolder();
        logFilePath = Path.Combine(folder, "findneedle_log.txt");
        try
        {
            // Ensure folder exists
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            // Truncate the log file on startup so it is cleared each run
            File.WriteAllText(logFilePath, string.Empty);
        }
        catch
        {
            // Ignore IO errors here - logging will attempt to append later
        }
    }

    public void Log(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        Action<string>? cb;
        // Log() is called from many threads (UI, background searches, the off-thread metadata warm). Serialize
        // the cache mutation + file append so a concurrent reader/writer can't corrupt or throw.
        lock (_sync)
        {
            _logCache.Add(line);
            if (_logCache.Count > MaxCachedLines)
                _logCache.RemoveRange(0, _logCache.Count - MaxCachedLines);
            try { File.AppendAllText(logFilePath, line + Environment.NewLine); }
            catch { /* Optionally handle file I/O errors */ }
            cb = LogCallback;
        }
        // Invoke the callback OUTSIDE the lock (it marshals to the UI thread; never hold the lock across it).
        cb?.Invoke(line);
    }
}
