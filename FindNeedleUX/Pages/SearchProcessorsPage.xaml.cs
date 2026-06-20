using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using FindNeedleRuleDSL;
using FindNeedleUX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FindNeedleUX.Pages;

/// <summary>A row in the "Active rules" list: one RuleDSL rule set applied to the current search,
/// with its static structure and per-run match stats.</summary>
public sealed class ActiveRuleRow
{
    public string Title { get; init; } = "";
    public string FilePath { get; init; } = "";
    public bool AutoAdded { get; init; }
    public string MatchedSummary { get; init; } = "";
    public string StructureSummary { get; init; } = "";
    public string TagSummary { get; init; } = "";
    public Visibility AutoAddedVisibility => AutoAdded ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TagVisibility => string.IsNullOrEmpty(TagSummary) ? Visibility.Collapsed : Visibility.Visible;
}

/// <summary>
/// "Active rules" — repurposed from the old (now-defunct) processor toggle page. Shows the RuleDSL
/// rule sets processing the current search: where each came from (manual vs auto-added), what it
/// contains (sections + rules broken down by action), and how many results it matched at run time.
/// </summary>
public sealed partial class SearchProcessorsPage : Page
{
    private readonly ObservableCollection<ActiveRuleRow> _rows = new();

    public SearchProcessorsPage()
    {
        this.InitializeComponent();
        RulesList.ItemsSource = _rows;
        this.Loaded += (_, _) => Build();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Build();

    private void Build()
    {
        _rows.Clear();

        var processors = MiddleLayerService.LastRuleProcessors ?? new List<FindNeedleRuleDSLPlugin>();
        var autoAdded = new HashSet<string>(
            MiddleLayerService.LastAutoAddedRules ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

        if (processors.Count == 0)
        {
            EmptyBar.Title = "No active rules";
            EmptyBar.Message = "The current search isn't applying any RuleDSL rules. Add rules in "
                + "Configure ▸ Rules, or enable Auto-add rules, then run a search.";
            EmptyBar.IsOpen = true;
            SubtitleText.Text = "The RuleDSL rule sets processing the current search.";
            return;
        }

        EmptyBar.IsOpen = false;
        int totalMatched = 0;

        foreach (var p in processors)
        {
            var path = p.RulesFilePath ?? "(built-in default)";
            var (title, structure) = ParseStructure(path);
            int matched = p.MatchedCount;
            totalMatched += matched;

            var tags = p.TagCounts;
            string tagSummary = tags.Count > 0
                ? "Tags: " + string.Join(", ", tags.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value:N0}"))
                : "";

            _rows.Add(new ActiveRuleRow
            {
                Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileName(path) : title,
                FilePath = path,
                AutoAdded = autoAdded.Contains(path),
                MatchedSummary = $"Matched {matched:N0} result{(matched == 1 ? "" : "s")}",
                StructureSummary = structure,
                TagSummary = tagSummary,
            });
        }

        SubtitleText.Text = $"{processors.Count} rule set{(processors.Count == 1 ? "" : "s")} active · "
            + $"{totalMatched:N0} total matches in the last search.";
    }

    /// <summary>Parse a rule file for its title + a structure summary (sections, enabled rule count,
    /// and a breakdown by action type). Best-effort — a parse failure yields an empty summary.</summary>
    private static (string title, string structure) ParseStructure(string path)
    {
        try
        {
            if (!File.Exists(path)) return ("", "(rule file not found)");
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var set = JsonSerializer.Deserialize<UnifiedRuleSet>(File.ReadAllText(path), opts);
            if (set == null) return ("", "(could not parse rule file)");

            int sectionCount = set.Sections?.Count ?? 0;
            var rules = (set.Sections ?? new()).SelectMany(s => s.Rules ?? new()).Where(r => r.Enabled).ToList();
            var byAction = rules
                .GroupBy(r => string.IsNullOrEmpty(r.Action?.Type) ? "other" : r.Action.Type)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Count()} {g.Key}");

            var summary = $"{sectionCount} section{(sectionCount == 1 ? "" : "s")} · "
                + $"{rules.Count} rule{(rules.Count == 1 ? "" : "s")}"
                + (byAction.Any() ? " (" + string.Join(", ", byAction) + ")" : "");
            return (set.Title ?? "", summary);
        }
        catch (Exception ex)
        {
            return ("", $"(parse error: {ex.Message})");
        }
    }
}
