using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace findneedle.RuleDSL;

/// <summary>
/// Loads and manages RuleDSL configuration files.
/// </summary>
public class RuleLoader
{
    private readonly JsonSerializerOptions _jsonOptions;

    public RuleLoader()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
    }

    /// <summary>
    /// Loads rules from file paths. Returns merged rule set.
    /// </summary>
    public dynamic? LoadRulesFromPaths(IEnumerable<string> rulePaths)
    {
        if (rulePaths == null || !rulePaths.Any())
            return null;

        var mergedRules = new { schemaVersion = "1.0", version = "1.0", sections = new List<dynamic>() };
        var allSections = new List<dynamic>();

        foreach (var path in rulePaths)
        {
            try
            {
                var rules = LoadRulesFromFile(path);
                if (rules != null && rules.Sections != null)
                {
                    allSections.AddRange(rules.Sections as IEnumerable<dynamic> ?? new List<dynamic>());
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading rules from {path}: {ex.Message}");
            }
        }

        return new { schemaVersion = "1.0", version = "1.0", sections = allSections };
    }

    /// <summary>
    /// Loads rules from a single file path.
    /// </summary>
    public dynamic? LoadRulesFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Rules file not found: {filePath}");
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var rules = JsonSerializer.Deserialize<dynamic>(json, _jsonOptions);
            return rules;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse rules file {filePath}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Discovers rule files in a directory.
    /// </summary>
    public List<string> DiscoverRuleFiles(string? directory = null)
    {
        directory ??= Path.Combine(AppContext.BaseDirectory, "Rules");

        if (!Directory.Exists(directory))
            return new List<string>();

        return Directory.GetFiles(directory, "*.rules.json")
            .OrderBy(f => f)
            .ToList();
    }

    /// <summary>
    /// Gets sections by purpose (filter, enrichment, uml).
    /// </summary>
    public List<dynamic> GetSectionsByPurpose(dynamic? ruleSet, string purpose)
    {
        if (ruleSet == null)
            return new List<dynamic>();

        try
        {
            var sections = ruleSet.Sections as IEnumerable<dynamic> ?? new List<dynamic>();
            return sections
                .Where(s => s.Purpose != null && ((string)s.Purpose).Equals(purpose, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch
        {
            return new List<dynamic>();
        }
    }
}
