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

        // Collect raw JSON text for each section and return as a JsonElement root { "sections": [ ... ] }
        var sectionJsonParts = new List<string>();
        foreach (var path in rulePaths)
        {
            try
            {
                // Read JSON directly and extract the "sections" array in a robust way
                var json = File.ReadAllText(path);
                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;
                    JsonElement sectionsElement;
                    if (root.TryGetProperty("sections", out sectionsElement) || root.TryGetProperty("Sections", out sectionsElement))
                    {
                        if (sectionsElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in sectionsElement.EnumerateArray())
                            {
                                sectionJsonParts.Add(item.GetRawText());
                            }
                            continue;
                        }
                    }
                }
                // Fallback: try the dynamic deserializer path and attempt to extract sections
                var rules = LoadRulesFromFile(path);
                if (rules != null)
                {
                    try
                    {
                        var dynJson = JsonSerializer.Serialize(rules);
                        using var doc2 = JsonDocument.Parse(dynJson);
                        if (doc2.RootElement.TryGetProperty("sections", out JsonElement sec2) && sec2.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in sec2.EnumerateArray())
                            {
                                sectionJsonParts.Add(item.GetRawText());
                            }
                            continue;
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading rules from {path}: {ex.Message}");
            }
        }

        // Build combined JSON document
        var combined = "{\"sections\":[" + string.Join(",", sectionJsonParts) + "]}";
        try
        {
            using var doc = JsonDocument.Parse(combined);
            var root = doc.RootElement;
            var sectionsEl = root.GetProperty("sections");
            var allSectionsObj = new List<object?>();
            foreach (var item in sectionsEl.EnumerateArray())
            {
                allSectionsObj.Add(ConvertJsonElement(item));
            }
            return new { SchemaVersion = "1.0", Version = "1.0", Sections = allSectionsObj };
        }
        catch
        {
            // Fallback to dynamic empty structure
            return new { SchemaVersion = "1.0", Version = "1.0", Sections = new List<dynamic>() };
        }
    }

    // Convert JsonElement into plain CLR objects (Dictionary / List / primitive) for dynamic access
    private object? ConvertJsonElement(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                var dict = new Dictionary<string, object?>();
                foreach (var prop in el.EnumerateObject())
                {
                    dict[prop.Name] = ConvertJsonElement(prop.Value);
                }
                return dict;
            case JsonValueKind.Array:
                var list = new List<object?>();
                foreach (var it in el.EnumerateArray()) list.Add(ConvertJsonElement(it));
                return list;
            case JsonValueKind.String:
                return el.GetString();
            case JsonValueKind.Number:
                if (el.TryGetInt64(out var l)) return l;
                if (el.TryGetDouble(out var d)) return d;
                return el.GetRawText();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            default:
                return el.GetRawText();
        }
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
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            // Convert into CLR objects to avoid JsonDocument lifetime issues
            return ConvertJsonElement(root);
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
            var matched = new List<dynamic>();
            IEnumerable<object>? sections = null;
            try
            {
                sections = ruleSet.Sections as IEnumerable<object>;
            }
            catch { }

            if (sections == null)
            {
                // try reflection-based access
                try
                {
                    var rsObj = (object)ruleSet;
                    var prop = rsObj.GetType().GetProperty("Sections") ?? rsObj.GetType().GetProperty("sections");
                    if (prop != null)
                    {
                        sections = prop.GetValue(rsObj) as IEnumerable<object>;
                    }
                }
                catch { }
            }

            if (sections == null)
                return new List<dynamic>();

            foreach (var s in sections)
            {
                try
                {
                    string? pval = null;
                    if (s == null) continue;
                    // dictionary produced by ConvertJsonElement
                    if (s is Dictionary<string, object?> dict)
                    {
                        if (dict.TryGetValue("purpose", out var pv) && pv != null) pval = pv.ToString();
                        else if (dict.TryGetValue("Purpose", out var pv2) && pv2 != null) pval = pv2.ToString();
                    }
                    else if (s is JsonElement je && je.ValueKind == JsonValueKind.Object)
                    {
                        if (je.TryGetProperty("purpose", out var pp)) pval = pp.GetString();
                        else if (je.TryGetProperty("Purpose", out var pp2)) pval = pp2.GetString();
                    }
                    else
                    {
                        // try dynamic / reflection
                        try
                        {
                            var prop = s.GetType().GetProperty("purpose") ?? s.GetType().GetProperty("Purpose");
                            if (prop != null)
                            {
                                var v = prop.GetValue(s);
                                if (v != null) pval = v.ToString();
                            }
                        }
                        catch { }
                    }

                    if (!string.IsNullOrEmpty(pval) && pval.Equals(purpose, StringComparison.OrdinalIgnoreCase))
                    {
                        matched.Add(s);
                    }
                }
                catch { }
            }

            return matched;
        }
        catch
        {
            return new List<dynamic>();
        }
    }
}
