using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using findneedle.Interfaces;
using findneedle;

namespace KustoPlugin.FileExtension;

public class KustoExportProcessor : IFileExtensionProcessor
{
    private string? _fileName;
    private readonly List<KustoExportLogLine> _results = new();
    private readonly Dictionary<string, int> _providerCount = new();
    private bool _formatChecked = false;
    private string[]? _headerFields;

    public KustoExportProcessor() { }

    public string FileExtension => ".txt";

    public List<string> RegisterForExtensions() => new() { ".txt" };

    public void OpenFile(string fileName)
    {
        _fileName = fileName;
        _formatChecked = false;
        _results.Clear();
        _providerCount.Clear();
        _headerFields = null;
    }

    public bool CheckFileFormat()
    {
        if (_fileName == null || !File.Exists(_fileName))
            return false;
        using var reader = new StreamReader(_fileName);
        var header = reader.ReadLine();
        if (header == null)
            return false;
        _headerFields = header.Split('\t');
        // Check for required columns
        _formatChecked = _headerFields.Contains("PreciseTimeStamp") && _headerFields.Contains("ProviderName");
        return _formatChecked;
    }

    public void LoadInMemory()
    {
        if (!_formatChecked)
            return;
        if (_fileName == null || !File.Exists(_fileName))
            return;
        _results.Clear();
        _providerCount.Clear();
        using var reader = new StreamReader(_fileName);
        var header = reader.ReadLine();
        if (header == null)
            return;
        _headerFields = header.Split('\t');
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var logLine = KustoExportLogLine.Parse(line, _headerFields, _fileName);
            if (logLine != null)
            {
                _results.Add(logLine);
                if (!string.IsNullOrEmpty(logLine.ProviderName))
                {
                    if (_providerCount.ContainsKey(logLine.ProviderName))
                        _providerCount[logLine.ProviderName]++;
                    else
                        _providerCount[logLine.ProviderName] = 1;
                }
            }
        }
    }

    public void DoPreProcessing() { /* No pre-processing needed */ }

    public List<ISearchResult> GetResults() => _results.Cast<ISearchResult>().ToList();

    public string GetFileName() => _fileName ?? string.Empty;

    public Dictionary<string, int> GetProviderCount() => new(_providerCount);

    public void Dispose() { /* Nothing to dispose */ }
}

public class KustoExportLogLine : ISearchResult
{
    public DateTime PreciseTimeStamp { get; set; }
    public string ActivityId { get; set; } = string.Empty;
    public string Pid { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string TaskName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string EventMessage { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string HostInstance { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;

    public static KustoExportLogLine? Parse(string line, string[] headerFields, string fileName)
    {
        var fields = line.Split('\t');
        if (fields.Length != headerFields.Length)
            return null;
        var log = new KustoExportLogLine { FileName = fileName };
        for (var i = 0; i < headerFields.Length; i++)
        {
            var key = headerFields[i];
            var value = fields[i];
            switch (key)
            {
                case "PreciseTimeStamp":
                    if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
                        log.PreciseTimeStamp = dt;
                    break;
                case "ActivityId": log.ActivityId = value; break;
                case "Pid": log.Pid = value; break;
                case "ProviderName": log.ProviderName = value; break;
                case "TaskName": log.TaskName = value; break;
                case "Message": log.Message = value; break;
                case "EventMessage": log.EventMessage = value; break;
                case "Level": log.Level = value; break;
                case "HostInstance": log.HostInstance = value; break;
            }
        }
        return log;
    }

    public DateTime GetLogTime() => PreciseTimeStamp;
    public string GetMachineName() => HostInstance;
    public void WriteToConsole() => Console.WriteLine(GetMessage());
    public Level GetLevel()
    {
        return Level switch
        {
            "1" => findneedle.Level.Catastrophic,
            "2" => findneedle.Level.Error,
            "3" => findneedle.Level.Warning,
            "4" => findneedle.Level.Info,
            "5" => findneedle.Level.Verbose,
            _ => findneedle.Level.Info
        };
    }
    public string GetUsername() => string.Empty;
    public string GetTaskName() => TaskName;
    public string GetOpCode() => string.Empty;
    public string GetSource() => ProviderName;
    public string GetSearchableData() => $"{Message} {EventMessage}";
    public string GetMessage() => !string.IsNullOrEmpty(EventMessage) ? EventMessage : Message;
    public string GetResultSource() => FileName;
}
