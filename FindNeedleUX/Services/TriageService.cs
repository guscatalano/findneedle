using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FindNeedleUX.Services;

/// <summary>
/// Brains of the large-file triage UX: decide when a file is big enough to triage, do a fast (bounded)
/// provider scan to show the user, and turn a provider selection into a `scope` rules.json the search engine
/// loads (which then filters the load at decode time — see docs/scope-rule-design.md). The UI panel binds to
/// this; the rule generation is exercised by the RuleDSL round-trip test.
/// </summary>
public static class TriageService
{
    /// <summary>Files at or above this size are worth a triage step (a full load would be minutes).</summary>
    public const long LargeFileBytes = 500L * 1024 * 1024; // 500 MB

    public static bool ShouldOfferTriage(string? path)
    {
        try
        {
            return !string.IsNullOrEmpty(path) && File.Exists(path)
                   && path.EndsWith(".etl", StringComparison.OrdinalIgnoreCase)
                   && new FileInfo(path).Length >= LargeFileBytes;
        }
        catch { return false; }
    }

    /// <summary>Bounded provider scan (sub-second) for the triage panel.</summary>
    public static List<string> InspectProviders(string etlPath)
    {
        try
        {
            var (providers, _) = findneedle.ETWPlugin.EtlInfoExtractor.QuickScan(etlPath);
            return providers.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch { return new List<string>(); }
    }

    /// <summary>Bounded provider scan returning each provider with its event count (descending), plus whether
    /// the scan hit its cap (so the counts are a SAMPLE of a bigger file, not exact totals). For the triage
    /// picker: shows which logger is the firehose and lets the user sort/filter by volume.</summary>
    public static (List<(string provider, int count)> providers, bool sampled) InspectProvidersWithCounts(string etlPath)
    {
        try
        {
            // Tight budget for the interactive picker: manifest/kernel traces dispatch fast so 120k events
            // enumerate their providers in well under a second; the 1.5s wall-clock cap keeps a WPP/odd trace
            // (whose events don't dispatch through these parsers) from grinding the whole file before the
            // dialog appears. The spinner covers this short wait.
            var (counts, capped, _) = findneedle.ETWPlugin.EtlInfoExtractor.QuickScanCounts(etlPath, maxEvents: 120000, maxMs: 1500);
            var list = counts
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => (kv.Key, kv.Value))
                .ToList();
            return (list, capped);
        }
        catch { return (new List<(string, int)>(), false); }
    }

    /// <summary>
    /// Generate a `scope` rules.json from the panel's selection and return its path. The caller adds it to the
    /// search query's RulesConfigPaths; the engine compiles it to a DecodeScope and filters the load at decode.
    /// </summary>
    public static string WriteScopeRuleFile(IEnumerable<string> selectedProviders, bool exclude = false,
        DateTime? fromUtc = null, DateTime? toUtc = null)
    {
        var set = FindNeedleRuleDSL.ScopeRuleParser.BuildScopeRuleSet(selectedProviders, exclude, fromUtc, toUtc);
        var json = FindNeedleRuleDSL.ScopeRuleParser.ToJson(set);
        var dir = Path.Combine(Path.GetTempPath(), "FindNeedle-triage");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"scope_{Guid.NewGuid():N}.rules.json");
        File.WriteAllText(path, json);
        return path;
    }
}
