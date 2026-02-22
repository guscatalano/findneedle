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
            var lines = File.ReadAllLines(_filePath);
            var lineNumber = 0;

            foreach (var line in lines)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                lineNumber++;
                
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var result = new PlainTextSearchResult
                {
                    LineNumber = lineNumber,
                    Text = line,
                    SourceFile = _filePath
                };
                _results.Add(result);
            }

            _hasLoaded = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading text file {_filePath}: {ex.Message}");
        }
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

        var batch = new List<ISearchResult>();
        var lines = await File.ReadAllLinesAsync(_filePath, cancellationToken);
        var lineNumber = 0;

        foreach (var line in lines)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            lineNumber++;
            
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var result = new PlainTextSearchResult
            {
                LineNumber = lineNumber,
                Text = line,
                SourceFile = _filePath
            };
            batch.Add(result);

            if (batch.Count >= batchSize)
            {
                onBatch(batch);
                batch = new List<ISearchResult>();
            }
        }

        if (batch.Count > 0)
            onBatch(batch);
    }

    public Dictionary<string, int> GetProviderCount()
    {
        return new Dictionary<string, int> { { "PlainText", _results.Count } };
    }

    public (TimeSpan? timeTaken, int? recordCount) GetSearchPerformanceEstimate(CancellationToken cancellationToken = default)
    {
        return (null, _results.Count > 0 ? _results.Count : null);
    }

    public void Dispose()
    {
        _results.Clear();
    }
}

/// <summary>
/// Simple search result for plain text lines.
/// </summary>
public class PlainTextSearchResult : ISearchResult
{
    public int LineNumber { get; set; }
    public string Text { get; set; } = string.Empty;
    public string SourceFile { get; set; } = string.Empty;

    public string GetSearchableData()
    {
        return Text;
    }

    public string GetMessage()
    {
        return Text;
    }

    public DateTime GetLogTime()
    {
        // Try to extract timestamp from common log formats
        // Format: [YYYY-MM-DD HH:MM:SS] or YYYY-MM-DD HH:MM:SS
        if (Text.Length > 20)
        {
            var start = Text.IndexOf('[');
            var end = Text.IndexOf(']');
            if (start >= 0 && end > start)
            {
                var timestamp = Text.Substring(start + 1, end - start - 1);
                if (DateTime.TryParse(timestamp, out var dt))
                    return dt;
            }
        }
        return DateTime.MinValue;
    }

    public string GetMachineName()
    {
        return ISearchResult.NOT_SUPPORTED;
    }

    public string GetUsername()
    {
        return ISearchResult.NOT_SUPPORTED;
    }

    public string GetTaskName()
    {
        return ISearchResult.NOT_SUPPORTED;
    }

    public string GetOpCode()
    {
        return ISearchResult.NOT_SUPPORTED;
    }

    public string GetSource()
    {
        return Path.GetFileName(SourceFile);
    }

    public string GetResultSource()
    {
        return SourceFile;
    }

    public Level GetLevel()
    {
        // Try to detect level from common patterns
        var upper = Text.ToUpperInvariant();
        if (upper.Contains("CRITICAL") || upper.Contains("FATAL"))
            return Level.Catastrophic;
        if (upper.Contains("ERROR"))
            return Level.Error;
        if (upper.Contains("WARNING") || upper.Contains("WARN"))
            return Level.Warning;
        if (upper.Contains("DEBUG") || upper.Contains("VERBOSE"))
            return Level.Verbose;
        return Level.Info; // Default
    }

    public void WriteToConsole()
    {
        Console.WriteLine($"[{LineNumber}] {Text}");
    }
}
