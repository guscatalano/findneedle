using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FindNeedlePluginLib;

namespace BasicTextPlugin;

/// <summary>
/// Simple processor for plain text log files (.txt, .log).
/// Reads each line as a separate search result.
/// </summary>
public class PlainTextProcessor : IFileExtensionProcessor, IPluginDescription
{
    private string _filePath = string.Empty;
    private readonly List<ISearchResult> _results = new();
    private bool _hasLoaded = false;

    // IPluginDescription implementation
    public string GetPluginTextDescription() => "Processes plain text log files (.txt, .log) line by line";
    public string GetPluginFriendlyName() => "Plain Text Processor";
    public string GetPluginClassName() => IPluginDescription.GetPluginClassNameBase(this);

    public List<string> RegisterForExtensions()
    {
        return new List<string> { ".txt", ".log" };
    }

    public void OpenFile(string fileName)
    {
        _filePath = fileName;
        _results.Clear();
        _hasLoaded = false;
    }

    public string GetFileName()
    {
        return _filePath;
    }

    public bool CheckFileFormat()
    {
        // Accept any text file - we'll try to read it as plain text
        if (string.IsNullOrEmpty(_filePath) || !File.Exists(_filePath))
            return false;

        // Check if it's a text file by reading first few bytes
        try
        {
            using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[Math.Min(1024, fs.Length)];
            fs.Read(buffer, 0, buffer.Length);

            // Check for binary content (null bytes typically indicate binary)
            foreach (var b in buffer)
            {
                if (b == 0)
                    return false; // Binary file
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void LoadInMemory()
    {
        LoadInMemory(CancellationToken.None);
    }

    public void LoadInMemory(CancellationToken cancellationToken)
    {
        if (_hasLoaded || string.IsNullOrEmpty(_filePath) || !File.Exists(_filePath))
            return;

        try
        {
            // Pre-size the result list from file size: typical log lines are ~100–200 bytes;
            // using 150 as a midpoint avoids ~20 List resizes on a 500k-row file.
            long fileSize = 0;
            try { fileSize = new FileInfo(_filePath).Length; } catch { /* ignore */ }
            int estimatedLines = fileSize > 0 ? (int)(fileSize / 150) : 4096;
            if (_results.Capacity < estimatedLines) _results.Capacity = estimatedLines;

            // Stream the file instead of File.ReadAllLines — ReadAllLines materialises every
            // line in a single string[] before we touch the first one, so a 500 MB log eats
            // ~1 GB of strings up front before we can even start parsing.
            // Triage scope: plain text has no provider/level cheaply, so only the time window applies
            // (and only to lines that actually parse a timestamp — un-timestamped lines are always kept).
            var scope = DecodeScope.Current;
            bool scopeTime = scope != null && (scope.FromUtc.HasValue || scope.ToUtc.HasValue);

            using var sr = new StreamReader(_filePath, detectEncodingFromByteOrderMarks: true);
            var lineNumber = 0;
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                if (cancellationToken.IsCancellationRequested) break;
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line)) continue;

                var result = new PlainTextSearchResult
                {
                    LineNumber = lineNumber,
                    Text = line,
                    SourceFile = _filePath
                };
                if (scopeTime && !KeptByScope(result, scope)) continue;
                _results.Add(result);
            }

            _hasLoaded = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading text file {_filePath}: {ex.Message}");
        }
    }

    /// <summary>True if a line passes the scope's time window. A line with no parseable timestamp is always
    /// kept (we can't classify it, so we don't drop it). The parsed time is treated as local and compared in
    /// UTC against the scope's UTC bounds — text logs rarely carry a timezone, so this is a best-effort window.</summary>
    private static bool KeptByScope(PlainTextSearchResult result, DecodeScope scope)
    {
        var ts = result.GetLogTime();
        if (ts <= DateTime.MinValue) return true; // un-timestamped line — keep
        var tsUtc = ts.Kind == DateTimeKind.Utc ? ts : DateTime.SpecifyKind(ts, DateTimeKind.Local).ToUniversalTime();
        return scope.Keep(null, tsUtc, -1); // provider/level unknown for plain text → time window only
    }

    public void DoPreProcessing()
    {
        // No preprocessing needed
    }

    public void DoPreProcessing(CancellationToken cancellationToken)
    {
        // No preprocessing needed
    }

    public List<ISearchResult> GetResults()
    {
        return _results;
    }

    public async Task GetResultsWithCallback(Action<List<ISearchResult>> onBatch, CancellationToken cancellationToken = default, int batchSize = 1000)
    {
        if (string.IsNullOrEmpty(_filePath) || !File.Exists(_filePath))
            return;

        // Fast path: LoadInMemory already parsed every row into _results. Just yield those in
        // batches without re-reading the file. The previous implementation re-read the file
        // here, doubling I/O + parsing cost on every search.
        if (_hasLoaded)
        {
            var cached = new List<ISearchResult>(batchSize);
            for (int i = 0; i < _results.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;
                cached.Add(_results[i]);
                if (cached.Count >= batchSize)
                {
                    onBatch(cached);
                    cached = new List<ISearchResult>(batchSize);
                }
            }
            if (cached.Count > 0) onBatch(cached);
            await Task.CompletedTask;
            return;
        }

        // Cold path: nobody called LoadInMemory first. Stream the file (one pass, no
        // ReadAllLines), populate _results, and yield batches as we go.
        var scope = DecodeScope.Current;
        bool scopeTime = scope != null && (scope.FromUtc.HasValue || scope.ToUtc.HasValue);
        var batch = new List<ISearchResult>(batchSize);
        using var sr = new StreamReader(_filePath, detectEncodingFromByteOrderMarks: true);
        var lineNumber = 0;
        string line;
        while ((line = sr.ReadLine()) != null)
        {
            if (cancellationToken.IsCancellationRequested) break;
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var result = new PlainTextSearchResult
            {
                LineNumber = lineNumber,
                Text = line,
                SourceFile = _filePath
            };
            if (scopeTime && !KeptByScope(result, scope)) continue;
            _results.Add(result);
            batch.Add(result);

            if (batch.Count >= batchSize)
            {
                onBatch(batch);
                batch = new List<ISearchResult>(batchSize);
            }
        }
        _hasLoaded = true;
        if (batch.Count > 0) onBatch(batch);
        await Task.CompletedTask;
    }

    public Dictionary<string, int> GetProviderCount()
    {
        return new Dictionary<string, int> { { "PlainText", _results.Count } };
    }

    public (TimeSpan? timeTaken, int? recordCount) GetSearchPerformanceEstimate(CancellationToken cancellationToken = default)
    {
        // After load: exact count. Before load (which is when Auto storage selection asks):
        // estimate from file size, since the storage tier picker depends on this and was
        // previously always getting 0 (forcing InMemory tier even for huge files).
        if (_results.Count > 0) return (null, _results.Count);
        try
        {
            if (!string.IsNullOrEmpty(_filePath) && File.Exists(_filePath))
            {
                var size = new FileInfo(_filePath).Length;
                if (size > 0) return (null, (int)Math.Min(int.MaxValue, size / 150));
            }
        }
        catch { /* swallow — estimate is best-effort */ }
        return (null, null);
    }

    public void Dispose()
    {
        _results.Clear();
    }
}

/// <summary>
/// Simple search result for plain text lines. Parsed fields (Level, LogTime, Source basename)
/// are computed once and cached so the search pipeline can call <c>GetLevel()</c>/
/// <c>GetLogTime()</c> from multiple stages (filter, storage insert, viewer display) without
/// paying the regex / ToUpper / DateTime.Parse cost N times. On a 500k-row search the cache
/// shaves ~3x of the per-row CPU work.
/// </summary>
public class PlainTextSearchResult : ISearchResult
{
    public int LineNumber { get; set; }
    public string Text { get; set; } = string.Empty;
    public string SourceFile { get; set; } = string.Empty;

    // Lazy-cached parses. Default Level.Info / DateTime.MinValue would be valid values, so we
    // need a separate "computed?" flag rather than checking for sentinels.
    private bool _levelComputed;
    private Level _level;
    private bool _logTimeComputed;
    private DateTime _logTime;
    private string _sourceBasename;

    public string GetSearchableData() => Text;
    public string GetMessage() => Text;

    public DateTime GetLogTime()
    {
        if (!_logTimeComputed)
        {
            _logTime = ParseLogTime(Text);
            _logTimeComputed = true;
        }
        return _logTime;
    }

    private static DateTime ParseLogTime(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return DateTime.MinValue;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var styles = System.Globalization.DateTimeStyles.NoCurrentDateDefault;

        // 1) Bracketed near the start: "[2026-06-19 08:00:01] …"
        var lb = text.IndexOf('[');
        if (lb >= 0 && lb < 40)
        {
            var rb = text.IndexOf(']', lb + 1);
            if (rb > lb && DateTime.TryParse(text.Substring(lb + 1, rb - lb - 1), inv, styles, out var b) && b.Year > 1)
                return b;
        }

        // 2) Leading bare/ISO timestamp: "2026-06-19 08:00:01.001 INFO …" or "2026-06-19T08:00:01Z …".
        var s = text.TrimStart();
        int sp1 = s.IndexOf(' ');
        var first = sp1 > 0 ? s.Substring(0, sp1) : s;
        // ISO form (date+time joined by 'T') is a single token.
        if (first.IndexOf('T') > 0 && LooksDateLike(first)
            && DateTime.TryParse(first, inv, styles, out var iso) && iso.Year > 1)
            return iso;
        // "date time" — the first two whitespace tokens.
        if (sp1 > 0)
        {
            int sp2 = s.IndexOf(' ', sp1 + 1);
            var two = sp2 > sp1 ? s.Substring(0, sp2) : s;
            // DISM and similar emit "2026-06-20 09:33:36, Info …" — a comma terminates the time
            // token. Strip a trailing comma so the full time-of-day parses; otherwise this falls
            // through to the date-only branch below and every row collapses to midnight.
            two = two.TrimEnd(',');
            if (LooksDateLike(two) && DateTime.TryParse(two, inv, styles, out var dt) && dt.Year > 1)
                return dt;
        }
        // Date only as the first token.
        if (LooksDateLike(first) && DateTime.TryParse(first, inv, styles, out var d) && d.Year > 1)
            return d;
        return DateTime.MinValue;
    }

    /// <summary>Cheap guard so we don't accidentally DateTime.Parse a non-timestamp token.</summary>
    private static bool LooksDateLike(string s)
    {
        bool digit = false, sep = false;
        foreach (var c in s)
        {
            if (char.IsDigit(c)) digit = true;
            else if (c == '-' || c == '/' || c == ':') sep = true;
        }
        return digit && sep;
    }

    // Plain-text logs don't carry these fields. Return empty so consumers (auto-hide column
    // logic, display layer) treat them as missing rather than as a literal sentinel string.
    public string GetMachineName() => string.Empty;
    public string GetUsername()    => string.Empty;
    public string GetTaskName()    => string.Empty;
    public string GetOpCode()      => string.Empty;

    public string GetSource()
    {
        // Path.GetFileName allocates each call; cache the basename since SourceFile doesn't change.
        return _sourceBasename ??= Path.GetFileName(SourceFile);
    }

    public string GetResultSource() => SourceFile;

    public Level GetLevel()
    {
        if (!_levelComputed)
        {
            _level = ParseLevel(Text);
            _levelComputed = true;
        }
        return _level;
    }

    /// <summary>
    /// Detect a level keyword in the line without allocating. The previous implementation did
    /// <c>Text.ToUpperInvariant()</c> on every call — a fresh ~150-char string allocation per
    /// row per call site. <see cref="String.IndexOf(string, StringComparison)"/> with
    /// <c>OrdinalIgnoreCase</c> compares in place with zero allocations.
    /// </summary>
    private static Level ParseLevel(string text)
    {
        if (string.IsNullOrEmpty(text)) return Level.Info;
        if (Contains(text, "CRITICAL") || Contains(text, "FATAL")) return Level.Catastrophic;
        if (Contains(text, "ERROR")) return Level.Error;
        if (Contains(text, "WARNING") || Contains(text, "WARN")) return Level.Warning;
        if (Contains(text, "DEBUG") || Contains(text, "VERBOSE")) return Level.Verbose;
        return Level.Info;
    }

    private static bool Contains(string haystack, string needle)
        => haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    public void WriteToConsole()
    {
        Console.WriteLine($"[{LineNumber}] {Text}");
    }
}
