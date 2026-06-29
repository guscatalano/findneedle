using System;
using System.IO;

namespace FindNeedleUX.Pages.NativeResultViewer;

/// <summary>
/// Groups a row's Source (the file path / origin shown in the Source column) into a coarse
/// "source type" bucket — used by the Sources dialog's by-type on/off toggles so a whole kind of
/// source (all PCAP captures, all ETW, all plain logs…) can be hidden or shown at once. Classifies
/// by file extension, which is stable across plugins (each file-based result carries its file path).
/// </summary>
public static class SourceTypeClassifier
{
    public static string Classify(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return "Other";
        string ext;
        try { ext = Path.GetExtension(source).ToLowerInvariant(); }
        catch { ext = string.Empty; }

        return ext switch
        {
            ".pcap" or ".pcapng" or ".cap" => "PCAP captures",
            ".etl" => "ETW (.etl)",
            ".evtx" => "Event Log",
            ".csv" or ".tsv" => "CSV",
            ".json" or ".jsonl" or ".ndjson" => "JSON",
            ".zip" => "Zip archives",
            ".cab" => "Cab archives",
            ".dmp" => "Crash dumps (ETW)",
            ".txt" or ".log" or ".trace" or ".out" or "" => "Log files",
            _ => $"Other ({ext})",
        };
    }
}
