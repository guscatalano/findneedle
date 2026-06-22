using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using FindNeedlePluginLib;
using FindNeedlePluginUtils.StructuredLog;

namespace JsonPlugin;

/// <summary>
/// Parses JSON logs into one search result per object. Supports JSON Lines / NDJSON (one object per
/// line — streamed), a top-level array of objects, and a single object that wraps an array (e.g.
/// {"logs":[ ... ]}). Each object's top-level fields are mapped to columns via
/// <see cref="StructuredLogFieldMapper"/>; the full object is kept as structured data.
/// </summary>
public class JsonLogProcessor : IFileExtensionProcessor, IPluginDescription
{
    private string _filePath = string.Empty;
    private readonly List<ISearchResult> _results = new();
    private bool _hasLoaded;

    public string GetPluginTextDescription() => "Parses JSON logs (.json / .jsonl / .ndjson) — one object per record";
    public string GetPluginFriendlyName() => "JSON Log Processor";
    public string GetPluginClassName() => IPluginDescription.GetPluginClassNameBase(this);

    public List<string> RegisterForExtensions() => new() { ".json", ".jsonl", ".ndjson" };

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
            var text = System.Text.Encoding.UTF8.GetString(buffer, 0, n).TrimStart('﻿', ' ', '\t', '\r', '\n');
            // Must look like JSON (object or array). Excludes most non-log .json only loosely; a
            // genuine non-log JSON object will simply yield one row.
            return text.StartsWith("{") || text.StartsWith("[");
        }
        catch { return false; }
    }

    public void LoadInMemory() => LoadInMemory(CancellationToken.None);

    public void LoadInMemory(CancellationToken cancellationToken)
    {
        if (_hasLoaded || string.IsNullOrEmpty(_filePath) || !File.Exists(_filePath)) return;
        try
        {
            ParseRecords(r => _results.Add(r), cancellationToken);
            _hasLoaded = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"JSON load failed for {_filePath}: {ex.Message}");
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
        ParseRecords(r =>
        {
            _results.Add(r);
            batch.Add(r);
            if (batch.Count >= batchSize) { onBatch(batch); batch = new List<ISearchResult>(batchSize); }
        }, cancellationToken);
        _hasLoaded = true;
        if (batch.Count > 0) onBatch(batch);
        await Task.CompletedTask;
    }

    private void ParseRecords(Action<ISearchResult> emit, CancellationToken cancellationToken)
    {
        // Decide JSONL vs single document by the first non-whitespace byte.
        bool isArrayOrWrappedDoc;
        using (var peek = new StreamReader(_filePath, detectEncodingFromByteOrderMarks: true))
        {
            int ci;
            do { ci = peek.Read(); } while (ci != -1 && char.IsWhiteSpace((char)ci));
            isArrayOrWrappedDoc = ci == '[' ||
                _filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                !_filePath.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase);
        }

        if (!isArrayOrWrappedDoc) { ParseJsonLines(emit, cancellationToken); return; }

        // Whole-document path: a top-level array of objects, or an object wrapping an array.
        using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        JsonDocument doc;
        try { doc = JsonDocument.Parse(fs, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
        catch (JsonException) { ParseJsonLines(emit, cancellationToken); return; } // not a single doc — fall back to JSONL

        using (doc)
        {
            int line = 0;
            JsonElement arr = default;
            bool haveArr = false;
            if (doc.RootElement.ValueKind == JsonValueKind.Array) { arr = doc.RootElement; haveArr = true; }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                    if (prop.Value.ValueKind == JsonValueKind.Array) { arr = prop.Value; haveArr = true; break; }
                if (!haveArr) { emit(MapObject(doc.RootElement, ++line)); return; } // single object → one row
            }
            else return;

            foreach (var el in arr.EnumerateArray())
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (el.ValueKind == JsonValueKind.Object) emit(MapObject(el, ++line));
            }
        }
    }

    private void ParseJsonLines(Action<ISearchResult> emit, CancellationToken cancellationToken)
    {
        using var sr = new StreamReader(_filePath, detectEncodingFromByteOrderMarks: true);
        string? line;
        int n = 0;
        while ((line = sr.ReadLine()) != null)
        {
            if (cancellationToken.IsCancellationRequested) break;
            n++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.ValueKind == JsonValueKind.Object) emit(MapObject(doc.RootElement, n));
            }
            catch (JsonException) { /* skip a malformed line rather than aborting the file */ }
        }
    }

    private StructuredLogResult MapObject(JsonElement obj, int line)
    {
        var fields = new List<KeyValuePair<string, string>>();
        foreach (var prop in obj.EnumerateObject())
            fields.Add(new KeyValuePair<string, string>(prop.Name, ScalarText(prop.Value)));
        return StructuredLogFieldMapper.Map(fields, _filePath, line);
    }

    /// <summary>Scalars as their text; nested objects/arrays as their raw JSON (kept searchable).</summary>
    private static string ScalarText(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? string.Empty,
        JsonValueKind.Null => string.Empty,
        JsonValueKind.Object or JsonValueKind.Array => v.GetRawText(),
        _ => v.GetRawText(),
    };

    public Dictionary<string, int> GetProviderCount() => new() { { "JSON", _results.Count } };

    public (TimeSpan? timeTaken, int? recordCount) GetSearchPerformanceEstimate(CancellationToken cancellationToken = default)
    {
        if (_results.Count > 0) return (null, _results.Count);
        try
        {
            if (!string.IsNullOrEmpty(_filePath) && File.Exists(_filePath))
            {
                var size = new FileInfo(_filePath).Length;
                if (size > 0) return (null, (int)Math.Min(int.MaxValue, size / 200));
            }
        }
        catch { /* best-effort */ }
        return (null, null);
    }

    public void Dispose() => _results.Clear();
}
