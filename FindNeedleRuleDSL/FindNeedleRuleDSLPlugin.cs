using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FindNeedlePluginLib;

namespace FindNeedleRuleDSL;

/// <summary>
/// A flexible DSL-based result processor that uses JSON rule definitions
/// to tag and categorize search results.
/// </summary>
public class FindNeedleRuleDSLPlugin : IResultProcessor
{
    private readonly List<ISearchResult> _matchedResults = new();
    private readonly Dictionary<string, int> _tagCounts = new();
    private UnifiedRuleSet? _ruleSet;
    private readonly string _provider;
    private readonly string? _rulesFilePath;

    public FindNeedleRuleDSLPlugin(string provider = "EventLog", string? rulesFilePath = null)
    {
        _provider = provider;
        _rulesFilePath = rulesFilePath;
    }

    public string GetClassName()
    {
        Type me = GetType();
        if (me.FullName == null)
        {
            throw new InvalidOperationException("FullName was null");
        }
        return me.FullName;
    }

    public string GetFriendlyName()
    {
        return "FindNeedle Rule DSL Processor";
    }

    public string GetOutputFile(string optionalOutputFolder = "")
    {
        return "";
    }

    public string GetOutputText()
    {
        var sb = new StringBuilder();
        var totalMatches = _matchedResults.Count;
        sb.AppendLine($"Found {totalMatches} result{(totalMatches != 1 ? "s" : "")}");
        sb.AppendLine($"FindNeedle Rule DSL: Processed {totalMatches} results");
        
        if (_tagCounts.Count > 0)
        {
            sb.AppendLine("Tags found:");
            foreach (var kvp in _tagCounts.OrderByDescending(x => x.Value))
            {
                sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
            }
        }

        return sb.ToString();
    }

    public string GetDescription()
    {
        return "Processes search results using JSON-based DSL rules for flexible categorization and tagging";
    }

    public void ProcessResults(List<ISearchResult> results)
    {
        _matchedResults.Clear();
        _tagCounts.Clear();

        try
        {
            LoadRules();
            if (_ruleSet == null)
            {
                return;
            }

            var processor = new UnifiedRuleProcessor(_ruleSet, _provider);
            var matches = processor.Process(
                results.Cast<object>().ToList(),
                obj => (obj as ISearchResult)?.GetSearchableData() ?? string.Empty
            );

            foreach (var (rule, result, action) in matches)
            {
                _matchedResults.Add(result as ISearchResult ?? throw new InvalidCastException());

                if (action.Type == "tag" && !string.IsNullOrEmpty(action.Tag))
                {
                    if (_tagCounts.ContainsKey(action.Tag))
                    {
                        _tagCounts[action.Tag]++;
                    }
                    else
                    {
                        _tagCounts[action.Tag] = 1;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error processing rules: {ex.Message}");
        }
    }

    private void LoadRules()
    {
        if (_ruleSet != null)
        {
            return;
        }

        var rulesFile = _rulesFilePath ?? GetDefaultRulesFile();
        if (!File.Exists(rulesFile))
        {
            System.Diagnostics.Debug.WriteLine($"Rules file not found: {rulesFile}");
            return;
        }

        try
        {
            var json = File.ReadAllText(rulesFile);
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            _ruleSet = JsonSerializer.Deserialize<UnifiedRuleSet>(json, options);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading rules from {rulesFile}: {ex.Message}");
        }
    }

    private static string GetDefaultRulesFile()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(appDir, "rules", "default.rules.json");
    }

    /// <summary>
    /// Gets the count of results matching a specific tag.
    /// </summary>
    public int GetTagCount(string tag)
    {
        return _tagCounts.TryGetValue(tag, out var count) ? count : 0;
    }

    /// <summary>
    /// Gets all tags that were found.
    /// </summary>
    public IEnumerable<string> GetFoundTags()
    {
        return _tagCounts.Keys;
    }

    /// <summary>
    /// Gets all matched results.
    /// </summary>
    public IEnumerable<ISearchResult> GetMatchedResults()
    {
        return _matchedResults.AsReadOnly();
    }
}
