using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
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

    /// <summary>
    /// Evaluates all rules in a section against a result.
    /// </summary>
    public EvaluationResult EvaluateRules(ISearchResult result, dynamic ruleSection)
    {
        var evalResult = new EvaluationResult();

        try
        {
            var rules = ruleSection.Rules as List<dynamic> ?? new List<dynamic>();
            
            foreach (var rule in rules)
            {
                if (rule.Enabled == false)
                    continue;

                if (EvaluateRule(result, rule))
                {
                    ExecuteActions(rule, evalResult);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error evaluating rules: {ex.Message}");
        }

        return evalResult;
    }

    /// <summary>
    /// Evaluates a single rule against a result.
    /// </summary>
    private bool EvaluateRule(ISearchResult result, dynamic rule)
    {
        // Check datetime range first if specified
        if (rule.DateRange != null)
        {
            if (!EvaluateDateRange(result, rule.DateRange))
                return false;
        }

        // Check field-specific match
        string fieldValue = ExtractFieldValue(result, rule.Field);

        // Evaluate match pattern
        if (!string.IsNullOrEmpty(rule.Match))
        {
            if (!MatchesPattern(fieldValue, rule.Match))
                return false;
        }

        // Evaluate unmatch pattern (negative match)
        if (!string.IsNullOrEmpty(rule.Unmatch))
        {
            if (MatchesPattern(fieldValue, rule.Unmatch))
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
            var fieldName = (string?)dateRange.Field ?? "logTime";

            // Handle relative time (withinLast)
            if (!string.IsNullOrEmpty(dateRange.WithinLast))
            {
                var span = ParseTimeSpan((string)dateRange.WithinLast);
                var threshold = DateTime.Now.Subtract(span);
                return logTime >= threshold;
            }

            // Handle absolute times
            if (!string.IsNullOrEmpty(dateRange.After))
            {
                var after = ParseDateTime((string)dateRange.After);
                if (after.HasValue && logTime < after.Value)
                    return false;
            }

            if (!string.IsNullOrEmpty(dateRange.Before))
            {
                var before = ParseDateTime((string)dateRange.Before);
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
    private void ExecuteActions(dynamic rule, EvaluationResult evalResult)
    {
        try
        {
            // Handle both single action and actions array
            var actions = new List<dynamic>();
            
            if (rule.Actions != null)
            {
                actions.AddRange(rule.Actions as IEnumerable<dynamic> ?? new List<dynamic>());
            }
            else if (rule.Action != null)
            {
                actions.Add(rule.Action);
            }

            foreach (var action in actions)
            {
                ExecuteAction(action, evalResult);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error executing actions: {ex.Message}");
        }
    }

    /// <summary>
    /// Executes a single action.
    /// </summary>
    private void ExecuteAction(dynamic action, EvaluationResult evalResult)
    {
        var actionType = (string?)action.Type ?? string.Empty;

        switch (actionType.ToLower())
        {
            case "include":
                evalResult.Include = true;
                break;

            case "exclude":
                evalResult.Include = false;
                break;

            case "tag":
                var tagValue = (string?)action.Value ?? (string?)action.Tag;
                if (!string.IsNullOrEmpty(tagValue))
                {
                    evalResult.Tags.Add(tagValue);
                }
                break;

            case "route":
                var processor = (string?)action.Processor;
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
