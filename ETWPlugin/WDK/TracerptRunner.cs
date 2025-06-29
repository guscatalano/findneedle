using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace findneedle.WDK;

public class ETLSummary
{
    public string? ProcessedFile { get; set; }
    public int TotalBuffersProcessed { get; set; }
    public int TotalEventsProcessed { get; set; }
    public int TotalEventsLost { get; set; }
    public int TotalFormatErrors { get; set; }
    public int TotalFormatsUnknown { get; set; }
    public string? TotalElapsedTime { get; set; }
    public string? WindowsBuildInfo { get; set; }
    public List<string> Providers { get; set; } = new();
}

public class TracerptRunner
{
    public static ETLSummary RunAndParse(string etlPath, string outputDir)
    {
        if (!File.Exists(etlPath))
            throw new FileNotFoundException($"ETL file not found: {etlPath}");

        var summaryPath = Path.Combine(outputDir, "summary.txt");
        var tracerptExe = "tracerpt.exe"; // Assumes tracerpt is in PATH

        var psi = new ProcessStartInfo
        {
            FileName = tracerptExe,
            Arguments = $"\"{etlPath}\" -o \"{summaryPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = outputDir
        };

        using (var proc = Process.Start(psi))
        {
            if (proc == null)
                throw new Exception("Failed to start tracerpt process");
            proc.WaitForExit();
            if (proc.ExitCode != 0)
            {
                var error = proc.StandardError.ReadToEnd();
                throw new Exception($"tracerpt failed: {error}");
            }
        }

        // Wait for summary.txt to be written
        int tries = 20;
        while (!File.Exists(summaryPath) && tries-- > 0)
        {
            Thread.Sleep(100);
        }
        if (!File.Exists(summaryPath))
            throw new Exception("summary.txt was not created by tracerpt");

        return ParseSummary(summaryPath);
    }

    public static ETLSummary RunAndParseReport(string etlPath, string outputDir)
    {
        if (!File.Exists(etlPath))
            throw new FileNotFoundException($"ETL file not found: {etlPath}");

        var reportPath = Path.Combine(outputDir, "report.txt");
        var tracerptExe = "tracerpt.exe";

        var psi = new ProcessStartInfo
        {
            FileName = tracerptExe,
            Arguments = $"\"{etlPath}\" -report \"{reportPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = outputDir
        };

        using (var proc = Process.Start(psi))
        {
            if (proc == null)
                throw new Exception("Failed to start tracerpt process");
            proc.WaitForExit();
            if (proc.ExitCode != 0)
            {
                var error = proc.StandardError.ReadToEnd();
                throw new Exception($"tracerpt -report failed: {error}");
            }
        }

        // Wait for report.txt to be written
        int tries = 20;
        while (!File.Exists(reportPath) && tries-- > 0)
        {
            Thread.Sleep(100);
        }
        if (!File.Exists(reportPath))
            throw new Exception("report.txt was not created by tracerpt");

        return ParseReport(reportPath);
    }

    public static ETLSummary ParseSummary(string summaryPath)
    {
        var summary = new ETLSummary();
        var lines = File.ReadAllLines(summaryPath);
        foreach (var line in lines)
        {
            if (line.StartsWith("Trace File Name:", StringComparison.OrdinalIgnoreCase))
                summary.ProcessedFile = line.Substring(line.IndexOf(":") + 1).Trim();
            else if (line.StartsWith("Buffers Processed:", StringComparison.OrdinalIgnoreCase))
                summary.TotalBuffersProcessed = ParseInt(line);
            else if (line.StartsWith("Events Processed:", StringComparison.OrdinalIgnoreCase))
                summary.TotalEventsProcessed = ParseInt(line);
            else if (line.StartsWith("Events Lost:", StringComparison.OrdinalIgnoreCase))
                summary.TotalEventsLost = ParseInt(line);
            else if (line.StartsWith("Format Errors:", StringComparison.OrdinalIgnoreCase))
                summary.TotalFormatErrors = ParseInt(line);
            else if (line.StartsWith("Unknown Formats:", StringComparison.OrdinalIgnoreCase))
                summary.TotalFormatsUnknown = ParseInt(line);
            else if (line.StartsWith("Elapsed Time:", StringComparison.OrdinalIgnoreCase))
                summary.TotalElapsedTime = line.Substring(line.IndexOf(":") + 1).Trim();
        }
        return summary;
    }

    public static ETLSummary ParseReport(string reportPath)
    {
        var summary = new ETLSummary();
        var lines = File.ReadAllLines(reportPath);
        var providers = new List<string>();
        string? buildInfo = null;
        foreach (var line in lines)
        {
            // Example: Provider Name: Microsoft-Windows-Kernel-Process
            if (line.StartsWith("Provider Name:", StringComparison.OrdinalIgnoreCase))
            {
                var provider = line.Substring(line.IndexOf(":") + 1).Trim();
                if (!string.IsNullOrEmpty(provider))
                    providers.Add(provider);
            }
           
            // XML-style: <Data name="build">26100</Data>
            if (buildInfo == null && line.Contains("<Data name=\"build\">"))
            {
                int start = line.IndexOf(">", line.IndexOf("<Data name=\"build\"")) + 1;
                int end = line.IndexOf("</Data>", start);
                if (start > 0 && end > start)
                {
                    buildInfo = line.Substring(start, end - start).Trim();
                }
            }
        }
        summary.Providers = providers;
        summary.WindowsBuildInfo = buildInfo;
        return summary;
    }

    private static int ParseInt(string line)
    {
        var idx = line.IndexOf(":");
        if (idx >= 0 && int.TryParse(line.Substring(idx + 1).Trim(), out int val))
            return val;
        return 0;
    }
}
