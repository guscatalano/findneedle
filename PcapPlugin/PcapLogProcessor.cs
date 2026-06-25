using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FindNeedlePluginLib;

namespace PcapPlugin;

/// <summary>
/// Parses network capture files (.pcap / .pcapng / .cap) into one search result per packet. The
/// container is read by <see cref="PcapFileReader"/> (fully managed — no native libpcap), each packet
/// is dissected by <see cref="PacketMapper"/> (PacketDotNet + deep DNS/TLS/HTTP decoding) and emitted
/// as a structured row, so packets flow through the same viewer, filters and RuleDSL as any other log.
/// </summary>
public class PcapLogProcessor : IFileExtensionProcessor, IPluginDescription
{
    private string _filePath = string.Empty;
    private readonly List<ISearchResult> _results = new();
    private bool _hasLoaded;

    public string GetPluginTextDescription() => "Parses network capture files (.pcap / .pcapng) into one searchable row per packet, decoding Ethernet/IP/TCP/UDP plus DNS, TLS and HTTP";
    public string GetPluginFriendlyName() => "PCAP Network Capture Processor";
    public string GetPluginClassName() => IPluginDescription.GetPluginClassNameBase(this);

    public List<string> RegisterForExtensions() => new() { ".pcap", ".pcapng", ".cap" };

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
        return PcapFileReader.LooksLikeCapture(_filePath);
    }

    public void LoadInMemory() => LoadInMemory(CancellationToken.None);

    public void LoadInMemory(CancellationToken cancellationToken)
    {
        if (_hasLoaded || string.IsNullOrEmpty(_filePath) || !File.Exists(_filePath)) return;
        try
        {
            ParsePackets(r => _results.Add(r), cancellationToken);
            _hasLoaded = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PCAP load failed for {_filePath}: {ex.Message}");
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
        ParsePackets(r =>
        {
            _results.Add(r);
            batch.Add(r);
            if (batch.Count >= batchSize) { onBatch(batch); batch = new List<ISearchResult>(batchSize); }
        }, cancellationToken);
        _hasLoaded = true;
        if (batch.Count > 0) onBatch(batch);
        await Task.CompletedTask;
    }

    private void ParsePackets(Action<ISearchResult> emit, CancellationToken cancellationToken)
    {
        int frame = 0;
        foreach (var raw in PcapFileReader.Read(_filePath))
        {
            if (cancellationToken.IsCancellationRequested) break;
            frame++;
            emit(PacketMapper.Map(raw, _filePath, frame));
        }
    }

    /// <summary>Per-protocol packet counts (drives the Provider filter and the Statistics page).</summary>
    public Dictionary<string, int> GetProviderCount()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in _results)
        {
            var p = r.GetSource();
            if (string.IsNullOrEmpty(p)) p = "(other)";
            counts[p] = counts.TryGetValue(p, out var c) ? c + 1 : 1;
        }
        return counts;
    }

    public (TimeSpan? timeTaken, int? recordCount) GetSearchPerformanceEstimate(CancellationToken cancellationToken = default)
    {
        if (_results.Count > 0) return (null, _results.Count);
        try
        {
            if (!string.IsNullOrEmpty(_filePath) && File.Exists(_filePath))
            {
                // Rough guess: average ~200 bytes/packet (header + payload sample) over the file size.
                var size = new FileInfo(_filePath).Length;
                if (size > 0) return (null, (int)Math.Min(int.MaxValue, size / 200));
            }
        }
        catch { /* best-effort */ }
        return (null, null);
    }

    public void Dispose() => _results.Clear();
}
