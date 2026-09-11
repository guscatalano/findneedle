using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FindNeedleRuleDSL;

namespace FindNeedleUX.Services;

/// <summary>One RuleDSL rule set applied to the last search: where it came from, what it contains
/// (sections + rules by action) and how many results it matched at run time. Shown in the viewer's
/// "Loaded sources" dialog beside the locations — runtime status, not workspace configuration.</summary>
public sealed class ActiveRuleInfo
{
    public string Title { get; init; } = "";
    public string FilePath { get; init; } = "";
    public bool AutoAdded { get; init; }
    public long Matched { get; init; }
    /// <summary>"Matched 12 results · 34 ms (field extraction)" / "Matched 12 results".</summary>
    public string MatchedSummary { get; init; } = "";
    /// <summary>"2 sections · 5 rules (3 tag, 2 filter)" or a parse note.</summary>
    public string StructureSummary { get; init; } = "";
    /// <summary>"Tags: error 12, warn 3" — empty when the run recorded no tags.</summary>
    public string TagSummary { get; init; } = "";
    /// <summary>Per-rule field-extraction cost, one "name: matches · ms" per line — empty when none.</summary>
    public string TimingSummary { get; init; } = "";
}

/// <summary>Builds the per-rule runtime summaries from MiddleLayerService's last-run state
/// (LastRuleProcessors + LastEnrichmentRuleStats + LastAutoAddedRules).</summary>
public static class ActiveRuleSummary
{
    /// <summary>The rule sets the last search applied, in processor order. Empty until a search has run.</summary>
    public static List<ActiveRuleInfo> Build()
    {
        var processors = MiddleLayerService.LastRuleProcessors ?? new List<FindNeedleRuleDSLPlugin>();
        var autoAdded = new HashSet<string>(
            MiddleLayerService.LastAutoAddedRules ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

        // Per-rule in-scan enrichment stats (name → matches + ms). These rules don't run as Step3
        // processors, so the processor's MatchedCount is 0 — we show the real numbers from here instead.
        var enrichByName = (MiddleLayerService.LastEnrichmentRuleStats
                ?? new List<FindPluginCore.Searching.NuSearchQuery.EnrichmentRuleStat>())
            .GroupBy(s => s.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var rows = new List<ActiveRuleInfo>();
        foreach (var p in processors)
        {
            var path = p.RulesFilePath ?? "(built-in default)";
            var (title, structure, ruleNames) = ParseStructure(path);

            long enrichMatches = 0; double enrichMs = 0;
            var breakdown = new List<string>();
            foreach (var n in ruleNames)
            {
                if (enrichByName.TryGetValue(n, out var st) && (st.Matches > 0 || st.Ms > 0))
                {
                    enrichMatches += st.Matches; enrichMs += st.Ms;
                    breakdown.Add($"{n}: {st.Matches:N0} matched · {st.Ms:N0} ms");
                }
            }

            long matched; string matchedSummary; string timingSummary = "";
            if (breakdown.Count > 0)
            {
                matched = enrichMatches;
                matchedSummary = $"Matched {enrichMatches:N0} result{(enrichMatches == 1 ? "" : "s")} · {enrichMs:N0} ms (field extraction)";
                timingSummary = string.Join("\n", breakdown);
            }
            else
            {
                matched = p.MatchedCount;
                matchedSummary = $"Matched {matched:N0} result{(matched == 1 ? "" : "s")}";
            }

            var tags = p.TagCounts;
            string tagSummary = tags.Count > 0
                ? "Tags: " + string.Join(", ", tags.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value:N0}"))
                : "";

            rows.Add(new ActiveRuleInfo
            {
                Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileName(path) : title,
                FilePath = path,
                AutoAdded = autoAdded.Contains(path),
                Matched = matched,
                MatchedSummary = matchedSummary,
                StructureSummary = structure,
                TagSummary = tagSummary,
                TimingSummary = timingSummary,
            });
        }
        return rows;
    }

    /// <summary>Parse a rule file for its title + a structure summary (sections, enabled rule count,
    /// and a breakdown by action type). Best-effort — a parse failure yields an empty summary.</summary>
    public static (string title, string structure, List<string> ruleNames) ParseStructure(string path)
    {
        try
        {
            if (!File.Exists(path)) return ("", "(rule file not found)", new());
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var set = JsonSerializer.Deserialize<UnifiedRuleSet>(File.ReadAllText(path), opts);
            if (set == null) return ("", "(could not parse rule file)", new());

            int sectionCount = set.Sections?.Count ?? 0;
            var rules = (set.Sections ?? new()).SelectMany(s => s.Rules ?? new()).Where(r => r.Enabled).ToList();
            var ruleNames = rules.Select(r => r.Name).Where(n => !string.IsNullOrEmpty(n)).ToList();
            var byAction = rules
                .GroupBy(r => string.IsNullOrEmpty(r.Action?.Type) ? "other" : r.Action.Type)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Count()} {g.Key}");

            var summary = $"{sectionCount} section{(sectionCount == 1 ? "" : "s")} · "
                + $"{rules.Count} rule{(rules.Count == 1 ? "" : "s")}"
                + (byAction.Any() ? " (" + string.Join(", ", byAction) + ")" : "");
            return (set.Title ?? "", summary, ruleNames);
        }
        catch (Exception ex)
        {
            return ("", $"(parse error: {ex.Message})", new());
        }
    }
}
