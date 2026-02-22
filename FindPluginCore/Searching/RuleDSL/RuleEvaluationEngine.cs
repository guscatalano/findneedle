using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using FindNeedlePluginLib;

namespace findneedle.RuleDSL;

/// <summary>
/// Evaluates RuleDSL rules against ISearchResult objects.
/// Handles field-specific matching, datetime filtering, and action execution.
/// </summary>
public class RuleEvaluationEngine
{
    public class EvaluationResult
    {
        public bool Include { get; set; } = true;
        public List<string> Tags { get; set; } = new();
        public List<string> RouteTo { get; set; } = new();
    }

    // Convert JsonElement into CLR types (Dictionary/List/primitive) without using JsonDocument after conversion
    private object? ConvertJsonElementToClr(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                var dict = new Dictionary<string, object?>();
                foreach (var prop in el.EnumerateObject())
                {
                    dict[prop.Name] = ConvertJsonElementToClr(prop.Value);
                }
                return dict;
            case JsonValueKind.Array:
                var list = new List<object?>();
                foreach (var it in el.EnumerateArray()) list.Add(ConvertJsonElementToClr(it));
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

    // Helper: get string property from dynamic or JsonElement
    private string? GetStringProp(dynamic obj, string name)
    {
        try
        {
            if (obj is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.Object)
                {
                    if (je.TryGetProperty(name, out var prop))
                    {
                        if (prop.ValueKind == JsonValueKind.String) return prop.GetString();
                        // For non-string, return raw text
                        return prop.GetRawText();
                    }
                    // try lowercase
                    if (je.TryGetProperty(name.ToLowerInvariant(), out var prop2))
                    {
                        if (prop2.ValueKind == JsonValueKind.String) return prop2.GetString();
                        return prop2.GetRawText();
                    }
                }
                return null;
            }
            else if (obj is IDictionary<string, object?> dict)
            {
                if (dict.TryGetValue(name, out var v) || dict.TryGetValue(name.ToLowerInvariant(), out v))
                {
                    return v?.ToString();
                }
                return null;
            }
            else
            {
                // dynamic object
                try
                {
                    var val = ((object)obj).GetType().GetProperty(name)?.GetValue((object)obj);
                    if (val == null)
                    {
                        val = ((object)obj).GetType().GetProperty(char.ToUpperInvariant(name[0]) + name.Substring(1))?.GetValue((object)obj);
                    }
                    return val?.ToString();
                }
                catch
                {
                    try { return (string?)obj.GetType().GetProperty(name)?.GetValue(obj); } catch { return null; }
                }
            }
        }
        catch { return null; }
    }

    private bool? GetBoolProp(object obj, string name)
    {
        try
        {
            if (obj is IDictionary<string, object?> dict)
            {
                if (dict.TryGetValue(name, out var v) || dict.TryGetValue(name.ToLowerInvariant(), out v))
                {
                    if (v is bool b) return b;
                    if (v is string s && bool.TryParse(s, out var pb)) return pb;
                }
                return null;
            }
            if (obj is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.True) return true;
                if (je.ValueKind == JsonValueKind.False) return false;
                if (je.TryGetProperty(name, out var p))
                {
                    if (p.ValueKind == JsonValueKind.True) return true;
                    if (p.ValueKind == JsonValueKind.False) return false;
                    if (p.ValueKind == JsonValueKind.String && bool.TryParse(p.GetString(), out var pb)) return pb;
                }
                return null;
            }
            // dynamic reflection
            try
            {
                var val = ((object)obj).GetType().GetProperty(name)?.GetValue((object)obj);
                if (val is bool bb) return bb;
                if (val is string ss && bool.TryParse(ss, out var pb2)) return pb2;
            }
            catch { }
            return null;
        }
        catch { return null; }
    }

    private bool HasProp(dynamic obj, string name)
    {
        try
        {
            if (obj is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.Object)
                {
                    if (je.TryGetProperty(name, out var _)) return true;
                    if (je.TryGetProperty(name.ToLowerInvariant(), out var _)) return true;
                }
                return false;
            }
            else if (obj is IDictionary<string, object?> dict)
            {
                return dict.ContainsKey(name) || dict.ContainsKey(name.ToLowerInvariant());
            }
            else
            {
                return ((object)obj).GetType().GetProperty(name) != null || ((object)obj).GetType().GetProperty(char.ToUpperInvariant(name[0]) + name.Substring(1)) != null;
            }
        }
        catch { return false; }
    }

    private dynamic? GetObjectProp(dynamic obj, string name)
    {
        try
        {
            if (obj is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.Object)
                {
                    if (je.TryGetProperty(name, out var prop)) return prop;
                    if (je.TryGetProperty(name.ToLowerInvariant(), out var prop2)) return prop2;
                }
                return null;
            }
            else if (obj is IDictionary<string, object?> dict)
            {
                if (dict.TryGetValue(name, out var v) || dict.TryGetValue(name.ToLowerInvariant(), out v)) return v;
                return null;
            }
            else
            {
                try { return ((object)obj).GetType().GetProperty(name)?.GetValue((object)obj); } catch { }
                try { return ((object)obj).GetType().GetProperty(char.ToUpperInvariant(name[0]) + name.Substring(1))?.GetValue((object)obj); } catch { }
                return null;
            }
        }
        catch { return null; }
    }

    /// <summary>
    /// Evaluates all rules in a section against a result.
    /// </summary>
    public EvaluationResult EvaluateRules(ISearchResult result, object ruleSection)
    {
        var evalResult = new EvaluationResult();

        try
        {
            IEnumerable<object> rulesEnumerable = Enumerable.Empty<object>();

            // If ruleSection is a JsonElement, convert it first; otherwise try to obtain a rules collection
            object? rulesObj = null;
            if (ruleSection is JsonElement rsJe)
            {
                var conv = ConvertJsonElementToClr(rsJe);
                if (conv is IDictionary<string, object?> convDict)
                {
                    if (convDict.TryGetValue("rules", out var ro) || convDict.TryGetValue("Rules", out ro))
                        rulesObj = ro;
                }
            }
            else
            {
                // Try helper to fetch Rules property from dictionary or dynamic
                rulesObj = GetObjectProp(ruleSection, "Rules");
                if (rulesObj == null && ruleSection is IDictionary<string, object?> dictSection)
                {
                    if (dictSection.TryGetValue("rules", out var ro) || dictSection.TryGetValue("Rules", out ro))
                        rulesObj = ro;
                }
            }

            if (rulesObj is IEnumerable<object> rulesList)
            {
                rulesEnumerable = rulesList;
            }
            else
            {
                rulesEnumerable = Enumerable.Empty<object>();
            }

            foreach (var rule in rulesEnumerable)
            {
                // Determine enabled flag for JsonElement or dynamic
                bool enabled = true;
                if (rule is JsonElement ruleJe)
                {
                    if (ruleJe.ValueKind == JsonValueKind.Object && (ruleJe.TryGetProperty("enabled", out var enProp) || ruleJe.TryGetProperty("Enabled", out enProp)))
                    {
                        if (enProp.ValueKind == JsonValueKind.False)
                            enabled = false;
                    }
                }
                else
                {
                    var en = GetBoolProp(rule, "Enabled");
                    if (en.HasValue && en.Value == false) enabled = false;
                }

                if (!enabled)
                    continue;

                if (EvaluateRule(result, rule))
                {
                    ExecuteActions(rule, evalResult);
                }
            }
        }
        catch (Exception ex)
        {
            try { FindNeedlePluginLib.Logger.Instance.Log($"Error evaluating rules: {ex}"); } catch { }
        }

        // Tags are stored only in the EvaluationResult; outputs may query evalResult.Tags when processing per-result
        // (No global TagStore is available in this build.)

        return evalResult;
    }

    /// <summary>
    /// Evaluates a single rule against a result.
    /// </summary>
    private bool EvaluateRule(ISearchResult result, object rule)
    {
        // Check datetime range first if specified
        var dateRangeObj = GetObjectProp(rule, "DateRange");
        if (dateRangeObj != null)
        {
            if (!EvaluateDateRange(result, dateRangeObj))
                return false;
        }

        // Check field-specific match
        var fieldName = GetStringProp(rule, "Field");
        string fieldValue = ExtractFieldValue(result, fieldName);

        // Evaluate match pattern
        var matchPattern = GetStringProp(rule, "Match");
        if (!string.IsNullOrEmpty(matchPattern))
        {
            if (!MatchesPattern(fieldValue, matchPattern))
                return false;
        }

        // Evaluate unmatch pattern (negative match)
        var unmatchPattern = GetStringProp(rule, "Unmatch");
        if (!string.IsNullOrEmpty(unmatchPattern))
        {
            if (MatchesPattern(fieldValue, unmatchPattern))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Extracts field value from ISearchResult based on field name.
    /// </summary>
    private string ExtractFieldValue(ISearchResult result, string? fieldName)
    {
        if (string.IsNullOrEmpty(fieldName))
        {
            // No field specified, use searchable data
            return result.GetSearchableData() ?? string.Empty;
        }

        return fieldName.ToLower() switch
        {
            "level" => result.GetLevel().ToString(),
            "source" => result.GetSource() ?? string.Empty,
            "machinename" => result.GetMachineName() ?? string.Empty,
            "username" => result.GetUsername() ?? string.Empty,
            "taskname" => result.GetTaskName() ?? string.Empty,
            "opcode" => result.GetOpCode() ?? string.Empty,
            "logtime" => result.GetLogTime().ToString("O"),
            "message" => result.GetMessage() ?? string.Empty,
            "searchabledata" => result.GetSearchableData() ?? string.Empty,
            _ => result.GetSearchableData() ?? string.Empty
        };
    }

    /// <summary>
    /// Matches a field value against a regex pattern.
    /// </summary>
    private bool MatchesPattern(string value, string pattern)
    {
        try
        {
            return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase);
        }
        catch
        {
            // Fallback to simple string contains
            return value.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Evaluates datetime range constraints.
    /// </summary>
    private bool EvaluateDateRange(ISearchResult result, dynamic dateRange)
    {
        try
        {
            var logTime = result.GetLogTime();
            var fieldName = GetStringProp(dateRange, "Field") ?? GetStringProp(dateRange, "field") ?? "logTime";

            // Handle relative time (withinLast)
            var withinLast = GetStringProp(dateRange, "WithinLast") ?? GetStringProp(dateRange, "withinLast");
            if (!string.IsNullOrEmpty(withinLast))
            {
                var span = ParseTimeSpan(withinLast);
                var threshold = DateTime.Now.Subtract(span);
                return logTime >= threshold;
            }

            // Handle absolute times
            var afterStr = GetStringProp(dateRange, "After") ?? GetStringProp(dateRange, "after");
            if (!string.IsNullOrEmpty(afterStr))
            {
                var after = ParseDateTime(afterStr);
                if (after.HasValue && logTime < after.Value)
                    return false;
            }

            var beforeStr = GetStringProp(dateRange, "Before") ?? GetStringProp(dateRange, "before");
            if (!string.IsNullOrEmpty(beforeStr))
            {
                var before = ParseDateTime(beforeStr);
                if (before.HasValue && logTime > before.Value)
                    return false;
            }

            return true;
        }
        catch
        {
            return true; // If parsing fails, don't filter
        }
    }

    /// <summary>
    /// Parses relative time strings like "1h", "24h", "7d".
    /// </summary>
    private TimeSpan ParseTimeSpan(string timeStr)
    {
        if (string.IsNullOrEmpty(timeStr))
            return TimeSpan.Zero;

        timeStr = timeStr.ToLower().Trim();

        if (timeStr.EndsWith("h"))
        {
            if (int.TryParse(timeStr[..^1], out var hours))
                return TimeSpan.FromHours(hours);
        }
        else if (timeStr.EndsWith("d"))
        {
            if (int.TryParse(timeStr[..^1], out var days))
                return TimeSpan.FromDays(days);
        }
        else if (timeStr.EndsWith("m"))
        {
            if (int.TryParse(timeStr[..^1], out var minutes))
                return TimeSpan.FromMinutes(minutes);
        }

        return TimeSpan.Zero;
    }

    /// <summary>
    /// Parses absolute datetime strings (ISO 8601 or relative like "-30d").
    /// </summary>
    private DateTime? ParseDateTime(string dateStr)
    {
        if (string.IsNullOrEmpty(dateStr))
            return null;

        // Handle relative dates like "-30d"
        if (dateStr.StartsWith("-"))
        {
            var span = ParseTimeSpan(dateStr[1..]);
            return DateTime.Now.Subtract(span);
        }

        // Try ISO 8601 format
        if (DateTime.TryParseExact(dateStr, "O", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result))
            return result;

        // Try common date formats
        if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result2))
            return result2;

        return null;
    }

    /// <summary>
    /// Executes rule actions and populates evaluation result.
    /// </summary>
    private void ExecuteActions(object rule, EvaluationResult evalResult)
    {
        try
        {
            // Handle both single action and actions array, supporting JsonElement and dynamic
            var actionsList = new List<dynamic>();
            var actionsObj = GetObjectProp(rule, "Actions");
            if (actionsObj != null)
            {
                if (actionsObj is JsonElement actionsJe && actionsJe.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in actionsJe.EnumerateArray())
                        actionsList.Add(a);
                }
                else
                {
                    try
                    {
                        if (actionsObj is IEnumerable<object> objEnum) actionsList.AddRange(objEnum.Cast<dynamic>());
                        else actionsList.Add(actionsObj);
                    }
                    catch
                    {
                        actionsList.Add(actionsObj);
                    }
                }
            }
            else
            {
                var actionObj = GetObjectProp(rule, "Action");
                if (actionObj != null)
                {
                    actionsList.Add(actionObj);
                }
            }

            foreach (var action in actionsList)
            {
                ExecuteAction(action, evalResult);
            }
        }
        catch (Exception ex)
        {
            try { FindNeedlePluginLib.Logger.Instance.Log($"Error executing actions: {ex}"); } catch { }
        }
    }

    /// <summary>
    /// Executes a single action.
    /// </summary>
    private void ExecuteAction(object action, EvaluationResult evalResult)
    {
        var actionType = GetStringProp(action, "Type") ?? GetStringProp(action, "type") ?? string.Empty;

        switch (actionType.ToLower())
        {
            case "include":
                evalResult.Include = true;
                break;

            case "exclude":
                evalResult.Include = false;
                break;

            case "tag":
                var tagValue = GetStringProp(action, "Value") ?? GetStringProp(action, "Tag");
                if (!string.IsNullOrEmpty(tagValue))
                {
                    evalResult.Tags.Add(tagValue);
                }
                break;

            case "route":
                var processor = GetStringProp(action, "Processor") ?? GetStringProp(action, "processor");
                if (!string.IsNullOrEmpty(processor))
                {
                    evalResult.RouteTo.Add(processor);
                }
                break;

            case "message":
                // UML message - store for visualization
                break;

            case "notification":
                // Notification action - could trigger alerts
                break;
        }
    }
}
