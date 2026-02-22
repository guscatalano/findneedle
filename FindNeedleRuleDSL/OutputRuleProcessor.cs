using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FindNeedlePluginLib;

namespace FindNeedleRuleDSL;

/// <summary>
/// Processes output rules to export search results in various formats (CSV, JSON, XML, TXT).
/// </summary>
public class OutputRuleProcessor
{
    private static readonly string[] DefaultFields = new[]
    {
        "timestamp", "level", "source", "message"
    };

    /// <summary>
    /// Process output rules and write results to files.
    /// </summary>
    public void ProcessOutputRules(
        List<ISearchResult> results,
        IEnumerable<object> outputSections,
        object? ruleSet = null)
    {
        foreach (var section in outputSections)
        {
            try
            {
                ProcessSection(results, section);
            }
            catch (Exception ex)
            {
                // Ensure full exception is written to the main application log for debugging
                FindNeedlePluginLib.Logger.Instance.Log($"Error processing output section: {ex}");
            }
        }
    }

    private void ProcessSection(List<ISearchResult> results, object section)
    {
        try
        {
            FindNeedlePluginLib.Logger.Instance.Log($"Processing output section of type: {section?.GetType().FullName}");
            if (section is IDictionary<string, object?> d)
            {
                FindNeedlePluginLib.Logger.Instance.Log($"Section keys: {string.Join(",", d.Keys)}");
            }
        }
        catch { }
        // Obtain rules robustly from different section representations (dynamic, dictionary, JsonElement)
        var rules = GetRulesFromSection(section);

        foreach (var ruleObj in rules)
        {
            try
            {
                if (!IsRuleEnabled(ruleObj))
                    continue;

                var action = GetProp(ruleObj, "action") ?? GetProp(ruleObj, "Action");
                var actionType = GetString(action, "type") ?? GetString(action, "Type");
                if (string.IsNullOrEmpty(actionType) || !actionType.Equals("output", StringComparison.OrdinalIgnoreCase))
                    continue;

                var format = GetString(action, "format") ?? GetString(action, "Format") ?? "csv";
                var path = ExpandPath(GetString(action, "path") ?? GetString(action, "Path") ?? GetDefaultPath(format));
                var fields = GetStringList(action, "fields") ?? DefaultFields.ToList();
                var includeHeaders = GetBool(action, "includeHeaders") ?? true;
                var delimiter = GetString(action, "delimiter") ?? ",";
                var pretty = GetBool(action, "pretty") ?? true;

                // Filter results if rule has match pattern. Use regex when provided (supports alternation like "Exception|crash").
                var filteredResults = results;
                var match = GetString(ruleObj, "match") ?? GetString(ruleObj, "Match");
                if (!string.IsNullOrEmpty(match))
                {
                    try
                    {
                        filteredResults = results.Where(r => Regex.IsMatch(r.GetSearchableData() ?? string.Empty, match, RegexOptions.IgnoreCase)).ToList();
                    }
                    catch
                    {
                        // Fallback: treat '|' as separator and check substrings
                        var parts = match.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToArray();
                        filteredResults = results.Where(r => parts.Any(p => (r.GetSearchableData() ?? string.Empty).IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                    }
                }

                // If rule requests tagged output, filter results by applied tags stored in extended properties (if supported)
                var tag = GetString(ruleObj, "tag") ?? GetString(ruleObj, "Tag");
                if (!string.IsNullOrEmpty(tag))
                {
                    // Our ISearchResult doesn't expose tags directly; try to read from result's GetSearchableData / GetMessage or extended dictionary if present
                    filteredResults = filteredResults.Where(r =>
                    {
                        try
                        {
                            // If the result is a dictionary-like object produced by plugins, try reflection
                            var resObj = r as object;
                            var prop = resObj.GetType().GetProperty("Tags");
                            if (prop != null)
                            {
                                var t = prop.GetValue(resObj) as IEnumerable<string>;
                                if (t != null && t.Contains(tag, StringComparer.OrdinalIgnoreCase)) return true;
                            }
                        }
                        catch { }
                        // Fallback: look for tag text in searchable data
                        var sd = r.GetSearchableData() ?? string.Empty;
                        if (sd.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                        var msg = r.GetMessage() ?? string.Empty;
                        if (msg.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                        return false;
                    }).ToList();
                }

                WriteOutput(filteredResults, format, path, fields, includeHeaders, delimiter, pretty);
            }
            catch (Exception ex)
            {
                FindNeedlePluginLib.Logger.Instance.Log($"Error processing individual output rule: {ex.Message}");
            }
        }
    }

    private IEnumerable<object> GetRulesFromSection(object section)
    {
        // section may be JsonElement, IDictionary<string,object>, or dynamic object
        if (section == null) return Enumerable.Empty<object>();
        // JsonElement
        if (section is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Object && (je.TryGetProperty("rules", out var r1) || je.TryGetProperty("Rules", out r1)))
            {
                if (r1.ValueKind == JsonValueKind.Array)
                    return r1.EnumerateArray().Select(x => (object)x).ToList();
            }
            return Enumerable.Empty<object>();
        }

        if (section is IDictionary<string, object?> dict)
        {
            if (dict.TryGetValue("rules", out var r) || dict.TryGetValue("Rules", out r))
            {
                if (r is IEnumerable<object> objs) return objs;
                if (r is IEnumerable<dynamic> dyn) return dyn.Cast<object>();
            }
            return Enumerable.Empty<object>();
        }

        // dynamic/POCO
        try
        {
            var prop = section.GetType().GetProperty("Rules") ?? section.GetType().GetProperty("rules");
            if (prop != null)
            {
                var val = prop.GetValue(section);
                if (val is IEnumerable<object> objs) return objs;
                if (val is IEnumerable<dynamic> dyn) return dyn.Cast<object>();
            }
        }
        catch { }

        // Fallback: try serializing the section and parsing JSON to find a "rules" array
        try
        {
            var json = JsonSerializer.Serialize(section);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("rules", out var rulesEl) || doc.RootElement.TryGetProperty("Rules", out rulesEl))
            {
                if (rulesEl.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<object>();
                    foreach (var it in rulesEl.EnumerateArray())
                    {
                        list.Add(it);
                    }
                    return list;
                }
            }
        }
        catch { }

        return Enumerable.Empty<object>();
    }

    private object? GetProp(object? obj, string name)
    {
        if (obj == null) return null;
        if (obj is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Object && je.TryGetProperty(name, out var p)) return p;
            if (je.ValueKind == JsonValueKind.Object && je.TryGetProperty(name.ToLowerInvariant(), out var p2)) return p2;
            return null;
        }
        if (obj is IDictionary<string, object?> dict)
        {
            if (dict.TryGetValue(name, out var v)) return v;
            if (dict.TryGetValue(name.ToLowerInvariant(), out var v2)) return v2;
            return null;
        }
        try
        {
            var pi = obj.GetType().GetProperty(name) ?? obj.GetType().GetProperty(char.ToUpperInvariant(name[0]) + name.Substring(1));
            return pi?.GetValue(obj);
        }
        catch { return null; }
    }

    private string? GetString(object? obj, string name)
    {
        var v = GetProp(obj, name);
        if (v == null) return null;
        if (v is JsonElement ve)
        {
            if (ve.ValueKind == JsonValueKind.String) return ve.GetString();
            return ve.GetRawText();
        }
        return v.ToString();
    }

    private List<string>? GetStringList(object? obj, string name)
    {
        var v = GetProp(obj, name);
        if (v == null) return null;
        if (v is IEnumerable<object> objs) return objs.Select(o => o?.ToString() ?? string.Empty).ToList();
        if (v is JsonElement ve && ve.ValueKind == JsonValueKind.Array) return ve.EnumerateArray().Select(x => x.GetString() ?? x.GetRawText()).ToList();
        return null;
    }

    private bool? GetBool(object? obj, string name)
    {
        var v = GetProp(obj, name);
        if (v == null) return null;
        if (v is bool b) return b;
        if (v is JsonElement ve)
        {
            if (ve.ValueKind == JsonValueKind.True) return true;
            if (ve.ValueKind == JsonValueKind.False) return false;
            if (ve.ValueKind == JsonValueKind.String && bool.TryParse(ve.GetString(), out var pb)) return pb;
            return null;
        }
        if (v is string s && bool.TryParse(s, out var pb2)) return pb2;
        return null;
    }

    private bool IsRuleEnabled(object? rule)
    {
        var en = GetBool(rule, "enabled") ?? GetBool(rule, "Enabled");
        return en ?? true;
    }

    private void WriteOutput(
        List<ISearchResult> results,
        string format,
        string path,
        List<string> fields,
        bool includeHeaders,
        string delimiter,
        bool pretty)
    {
        // Ensure directory exists
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        switch (format.ToLower())
        {
            case "csv":
                WriteCsv(results, path, fields, includeHeaders, delimiter);
                break;
            case "json":
                WriteJson(results, path, fields, pretty);
                break;
            case "xml":
                WriteXml(results, path, fields, pretty);
                break;
            case "txt":
            case "text":
                WriteTxt(results, path, fields);
                break;
            default:
                System.Diagnostics.Debug.WriteLine($"Unknown output format: {format}");
                break;
        }

        // Write to main application log so it's included in the detailed log and optionally echoed to console
        FindNeedlePluginLib.Logger.Instance.Log($"Output written: {path} ({results.Count} results)");
    }

    private void WriteCsv(List<ISearchResult> results, string path, List<string> fields, bool includeHeaders, string delimiter)
    {
        var sb = new StringBuilder();

        if (includeHeaders)
        {
            sb.AppendLine(string.Join(delimiter, fields.Select(EscapeCsvField)));
        }

        foreach (var result in results)
        {
            var values = fields.Select(f => EscapeCsvField(GetFieldValue(result, f)));
            sb.AppendLine(string.Join(delimiter, values));
        }

        File.WriteAllText(path, sb.ToString());
    }

    private void WriteJson(List<ISearchResult> results, string path, List<string> fields, bool pretty)
    {
        var items = results.Select(result =>
        {
            var dict = new Dictionary<string, object>();
            foreach (var field in fields)
            {
                dict[field] = GetFieldValue(result, field);
            }
            return dict;
        }).ToList();

        var options = new JsonSerializerOptions
        {
            WriteIndented = pretty
        };

        var json = JsonSerializer.Serialize(items, options);
        File.WriteAllText(path, json);
    }

    private void WriteXml(List<ISearchResult> results, string path, List<string> fields, bool pretty)
    {
        var root = new XElement("Results");

        foreach (var result in results)
        {
            var item = new XElement("Result");
            foreach (var field in fields)
            {
                item.Add(new XElement(SanitizeXmlName(field), GetFieldValue(result, field)));
            }
            root.Add(item);
        }

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        
        if (pretty)
        {
            doc.Save(path);
        }
        else
        {
            File.WriteAllText(path, doc.ToString(SaveOptions.DisableFormatting));
        }
    }

    private void WriteTxt(List<ISearchResult> results, string path, List<string> fields)
    {
        var sb = new StringBuilder();

        foreach (var result in results)
        {
            var parts = fields.Select(f => $"{f}={GetFieldValue(result, f)}");
            sb.AppendLine(string.Join(" | ", parts));
        }

        File.WriteAllText(path, sb.ToString());
    }

    private string GetFieldValue(ISearchResult result, string field)
    {
        return field.ToLower() switch
        {
            "timestamp" or "time" or "logtime" => result.GetLogTime().ToString("yyyy-MM-dd HH:mm:ss"),
            "level" => result.GetLevel().ToString(),
            "source" => result.GetSource(),
            "message" or "msg" => result.GetMessage(),
            "searchable" or "data" => result.GetSearchableData(),
            "machine" or "machinename" => result.GetMachineName(),
            "username" or "user" => result.GetUsername(),
            "taskname" or "task" => result.GetTaskName(),
            "opcode" => result.GetOpCode(),
            "resultsource" or "file" => result.GetResultSource(),
            _ => result.GetSearchableData() // Default to searchable data
        };
    }

    private string ExpandPath(string path)
    {
        var now = DateTime.Now;
        var outputBase = AppDomain.CurrentDomain.BaseDirectory;
        var outputFolder = Path.Combine(outputBase, "output");
        if (!Directory.Exists(outputFolder))
        {
            try { Directory.CreateDirectory(outputFolder); } catch { }
        }

        return path
            .Replace("{date}", now.ToString("yyyy-MM-dd"))
            .Replace("{time}", now.ToString("HHmmss"))
            .Replace("{datetime}", now.ToString("yyyy-MM-dd_HHmmss"))
            // {output} maps to application "output" folder
            .Replace("{output}", outputFolder.TrimEnd(Path.DirectorySeparatorChar))
            // keep {temp} for backward compatibility but map it to output folder as requested
            .Replace("{temp}", outputFolder.TrimEnd(Path.DirectorySeparatorChar));
    }

    private string GetDefaultPath(string format)
    {
        var ext = format.ToLower() switch
        {
            "json" => ".json",
            "xml" => ".xml",
            "txt" or "text" => ".txt",
            _ => ".csv"
        };
        return Path.Combine(Path.GetTempPath(), $"findneedle_output_{DateTime.Now:yyyyMMdd_HHmmss}{ext}");
    }

    private string EscapeCsvField(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }

    private string SanitizeXmlName(string name)
    {
        // Replace invalid XML element name characters
        var sanitized = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                sanitized.Append(c);
            else
                sanitized.Append('_');
        }
        
        // Ensure it starts with a letter or underscore
        if (sanitized.Length > 0 && char.IsDigit(sanitized[0]))
            sanitized.Insert(0, '_');
            
        return sanitized.Length > 0 ? sanitized.ToString() : "field";
    }
}
