using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;
using FindNeedlePluginLib;
using FindNeedlePluginUtils.StructuredLog;

namespace CsvPlugin;

/// <summary>
/// Parses delimited log files (.csv / .tsv) into one search result per data row. The header row
/// names the columns; well-known names (time/level/message/provider/pid/…) map to real columns via
/// <see cref="StructuredLogFieldMapper"/> and the rest are kept in the structured-data JSON.
/// </summary>
public class CsvLogProcessor : IFileExtensionProcessor, IPluginDescription
{
    private string _filePath = string.Empty;
    private readonly List<ISearchResult> _results = new();
    private bool _hasLoaded;

    public string GetPluginTextDescription() => "Parses delimited log files (.csv / .tsv) using the header row as column names";
    public string GetPluginFriendlyName() => "CSV Log Processor";
    public string GetPluginClassName() => IPluginDescription.GetPluginClassNameBase(this);

    public List<string> RegisterForExtensions() => new() { ".csv", ".tsv" };

    public void OpenFile(string fileName)
    {
        _filePath = fileName;
        _results.Clear();
        _hasLoaded = false;
    }

    public string GetFileName() => _filePath;

    public bool CheckFileFormat()
    {
        if (string.IsNullOrEmpty(_filePath) || !File.Exists(_filePath)) return false;
        try
        {
            using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[(int)Math.Min(2048, fs.Length)];
            int n = fs.Read(buffer, 0, buffer.Length);
            for (int i = 0; i < n; i++) if (buffer[i] == 0) return false; // binary
            // A header line with at least one delimiter (so it's actually columnar).
            var text = System.Text.Encoding.UTF8.GetString(buffer, 0, n);
            var firstLine = text.Split('\n', 2)[0];
            return firstLine.Contains(',') || firstLine.Contains('\t') || firstLine.Contains(';');
        }
        catch { return false; }
    }

    public void LoadInMemory() => LoadInMemory(CancellationToken.None);

    public void LoadInMemory(CancellationToken cancellationToken)
    {
        if (_hasLoaded || string.IsNullOrEmpty(_filePath) || !File.Exists(_filePath)) return;
        try
        {
            ParseRows((r, _) => _results.Add(r), cancellationToken);
            _hasLoaded = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CSV load failed for {_filePath}: {ex.Message}");
        }
    }

    public void DoPreProcessing() { }
    public void DoPreProcessing(CancellationToken cancellationToken) { }

    public List<ISearchResult> GetResults() => _results;

    public async Task GetResultsWithCallback(Action<List<ISearchResult>> onBatch,
        CancellationToken cancellationToken = default, int batchSize = 1000)
    {
        if (string.IsNullOrEmpty(_filePath) || !File.Exists(_filePath)) return;

        if (_hasLoaded)
        {
            for (int i = 0; i < _results.Count; i += batchSize)
            {
                if (cancellationToken.IsCancellationRequested) break;
                onBatch(_results.GetRange(i, Math.Min(batchSize, _results.Count - i)));
            }
            await Task.CompletedTask;
            return;
        }

        var batch = new List<ISearchResult>(batchSize);
        ParseRows((r, _) =>
        {
            _results.Add(r);
            batch.Add(r);
            if (batch.Count >= batchSize) { onBatch(batch); batch = new List<ISearchResult>(batchSize); }
        }, cancellationToken);
        _hasLoaded = true;
        if (batch.Count > 0) onBatch(batch);
        await Task.CompletedTask;
    }

    private void ParseRows(Action<ISearchResult, int> emit, CancellationToken cancellationToken)
    {
        var delimiter = SniffDelimiter(_filePath);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter,
            HasHeaderRecord = true,
            BadDataFound = null,        // tolerate malformed quoting rather than throw
            MissingFieldFound = null,
            DetectColumnCountChanges = false,
            TrimOptions = TrimOptions.Trim,
        };
        using var reader = new StreamReader(_filePath, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, config);
        if (!csv.Read() || !csv.ReadHeader()) return;
        var header = csv.HeaderRecord;
        if (header == null || header.Length == 0) return;

        // Canonical column names (blank header → colN), used BOTH as field keys and as the mapping
        // store key — so the remap dialog, the saved mapping, and the per-row field names all agree.
        var names = new string[header.Length];
        for (int i = 0; i < header.Length; i++)
            names[i] = string.IsNullOrEmpty(header[i]) ? $"col{i}" : header[i];

        // A user-saved column mapping for this exact column set (null = use the auto alias table).
        var overrides = CsvColumnMappingStore.Get(names);

        int line = 1;
        while (csv.Read())
        {
            if (cancellationToken.IsCancellationRequested) break;
            line++;
            var fields = new List<KeyValuePair<string, string>>(names.Length);
            for (int i = 0; i < names.Length; i++)
                fields.Add(new KeyValuePair<string, string>(names[i], csv.GetField(i) ?? string.Empty));
            emit(StructuredLogFieldMapper.Map(fields, _filePath, line, overrides), line);
        }
    }

    /// <summary>Pick the delimiter from the first line: tab for .tsv, otherwise whichever of , ; \t
    /// appears most in the header.</summary>
    private static string SniffDelimiter(string path)
    {
        if (path.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase)) return "\t";
        try
        {
            using var sr = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
            var first = sr.ReadLine() ?? string.Empty;
            int commas = first.Count(c => c == ','), tabs = first.Count(c => c == '\t'), semis = first.Count(c => c == ';');
            if (tabs > commas && tabs >= semis) return "\t";
            if (semis > commas && semis > tabs) return ";";
        }
        catch { /* default below */ }
        return ",";
    }

    public Dictionary<string, int> GetProviderCount() => new() { { "CSV", _results.Count } };

    public (TimeSpan? timeTaken, int? recordCount) GetSearchPerformanceEstimate(CancellationToken cancellationToken = default)
    {
        if (_results.Count > 0) return (null, _results.Count);
        try
        {
            if (!string.IsNullOrEmpty(_filePath) && File.Exists(_filePath))
            {
                var size = new FileInfo(_filePath).Length;
                if (size > 0) return (null, (int)Math.Min(int.MaxValue, size / 150));
            }
        }
        catch { /* best-effort */ }
        return (null, null);
    }

    public void Dispose() => _results.Clear();
}
