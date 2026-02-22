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
    public IReadOnlyList<string> LogCache => _logCache.AsReadOnly();

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
        _logCache.Add(line);
        try
        {
            File.AppendAllText(logFilePath, line + Environment.NewLine);
        }
        catch { /* Optionally handle file I/O errors */ }
        LogCallback?.Invoke(line);
    }
}
