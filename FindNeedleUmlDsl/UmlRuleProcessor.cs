using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace FindNeedleUmlDsl;

public class UmlRuleProcessor
{
    private readonly IUmlSyntaxTranslator _translator;
    private UmlRuleDefinition _definition = new();

    public UmlRuleDefinition Definition => _definition;

    /// <summary>Per-rule match counts from the most recent <see cref="ProcessMessages"/> call, in rule
    /// order. Lets callers report which rules actually contributed to the diagram (and which never fired).</summary>
    public List<UmlRuleUsage> LastUsage { get; private set; } = new();

    /// <summary>Source rows that matched a rule during the most recent <see cref="ProcessMessages"/>
    /// call, paired with the rule that matched. Lets the results viewer tag the rows used by the diagram.</summary>
    public List<UmlRowMatch> LastMatchedRows { get; private set; } = new();

    public UmlRuleProcessor(IUmlSyntaxTranslator translator)
    {
        _translator = translator;
    }

    public void LoadRulesFromJson(string json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        _definition = JsonSerializer.Deserialize<UmlRuleDefinition>(json, options) ?? throw new InvalidOperationException("Failed to deserialize UML rules");
    }

    public void LoadRulesFromFile(string filePath)
    {
        var json = System.IO.File.ReadAllText(filePath);
        LoadRulesFromJson(json);
    }

    public void LoadRules(UmlRuleDefinition definition) => _definition = definition;

    public string ProcessMessages(IEnumerable<LogMessage> messages)
    {
        var sb = new StringBuilder();
        sb.Append(_translator.GenerateHeader(_definition));
        sb.Append(_translator.GenerateParticipants(_definition.Participants));
        sb.AppendLine();

        // Track activation state for participants to avoid emitting invalid deactivate calls
        var activeParticipants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Per-rule match counts so callers can show which rules were actually used.
        var counts = new int[_definition.Rules.Count];
        var matchedRows = new List<UmlRowMatch>();

        foreach (var message in messages)
        {
            for (var ruleIndex = 0; ruleIndex < _definition.Rules.Count; ruleIndex++)
            {
                var rule = _definition.Rules[ruleIndex];
                if (!MatchesRule(message, rule))
                    continue;

                counts[ruleIndex]++;
                if (message.RowId >= 0)
                    matchedRows.Add(new UmlRowMatch
                    {
                        RowId = message.RowId,
                        RuleName = string.IsNullOrWhiteSpace(rule.Name) ? $"rule {ruleIndex + 1}" : rule.Name,
                    });
                var element = ResolveElement(message, rule);
                var type = (element.Type ?? string.Empty).Trim().ToLowerInvariant();

                // Normalize participant id used for activation checks
                var participant = (element.From ?? string.Empty).Trim();

                if (type == "activate")
                {
                    // Only emit activate if participant specified
                    if (!string.IsNullOrEmpty(participant))
                    {
                        // If already active, still emit (mermaid tolerates re-activate) and record state
                        activeParticipants.Add(participant);
                        sb.AppendLine(_translator.GenerateElement(element));
                    }
                    else
                    {
                        // skip malformed activate with no participant
                    }
                }
                else if (type == "deactivate")
                {
                    // Only emit deactivate if participant is currently active
                    if (!string.IsNullOrEmpty(participant) && activeParticipants.Contains(participant))
                    {
                        activeParticipants.Remove(participant);
                        sb.AppendLine(_translator.GenerateElement(element));
                    }
                    else
                    {
                        // Skip deactivating an inactive or unspecified participant to avoid mermaid parser errors
                    }
                }
                else if (type == "groupend")
                {
                    // groupend is emitted as 'end' in translator; always emit to avoid leaving blocks open
                    sb.AppendLine(_translator.GenerateElement(element));
                }
                else
                {
                    // Regular elements (message, note, delay, divider, etc.)
                    sb.AppendLine(_translator.GenerateElement(element));
                }
            }
        }

        var footer = _translator.GenerateFooter();
        if (!string.IsNullOrEmpty(footer)) sb.AppendLine(footer);

        LastMatchedRows = matchedRows;
        LastUsage = new List<UmlRuleUsage>(_definition.Rules.Count);
        for (var i = 0; i < _definition.Rules.Count; i++)
        {
            var r = _definition.Rules[i];
            LastUsage.Add(new UmlRuleUsage
            {
                Name = string.IsNullOrWhiteSpace(r.Name) ? $"rule {i + 1}" : r.Name,
                Match = r.Match,
                Count = counts[i],
            });
        }

        return sb.ToString();
    }

    private bool MatchesRule(LogMessage message, UmlRule rule)
    {
        var content = message.Content ?? string.Empty;
        // Prefer regex matching so rules can use alternation and captures (e.g. "foo|bar").
        // If rule explicitly requests regex, use it and fail on invalid regex
        if (rule.UseRegex == true)
        {
            if (!Regex.IsMatch(content, rule.Match, RegexOptions.IgnoreCase))
                return false;
        }
        else if (rule.UseRegex == false)
        {
            // explicit substring match
            if (!content.Contains(rule.Match, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        else
        {
            // Default: try regex first, fall back to substring
            try
            {
                if (!Regex.IsMatch(content, rule.Match, RegexOptions.IgnoreCase))
                    return false;
            }
            catch
            {
                if (!content.Contains(rule.Match, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }

        if (!string.IsNullOrEmpty(rule.Unmatch))
        {
            try
            {
                if (Regex.IsMatch(content, rule.Unmatch, RegexOptions.IgnoreCase))
                    return false;
            }
            catch
            {
                if (content.Contains(rule.Unmatch, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }

        return true;
    }

    private ResolvedUmlElement ResolveElement(LogMessage message, UmlRule rule)
    {
        var action = rule.Action;
        return new ResolvedUmlElement
        {
            Type = action.Type,
            From = action.From,
            To = action.To,
            Text = ResolvePlaceholders(action.Text, message.Content, rule.Match),
            ArrowStyle = action.ArrowStyle,
            NotePosition = action.NotePosition,
            Timestamp = message.Timestamp
        };
    }

    private string ResolvePlaceholders(string template, string content, string matchedText)
    {
        var result = template;
        var untilPattern = @"\{afterMatch:until:(.)\}";
        var untilMatch = Regex.Match(result, untilPattern);
        if (untilMatch.Success)
        {
            var delimiter = untilMatch.Groups[1].Value[0];
            var afterText = GetAfterMatch(content, matchedText);
            var endIndex = afterText.IndexOf(delimiter);
            var extracted = endIndex >= 0 ? afterText.Substring(0, endIndex) : afterText;
            result = Regex.Replace(result, untilPattern, extracted);
        }

        if (result.Contains("{afterMatch:untilSpace}"))
        {
            var afterText = GetAfterMatch(content, matchedText);
            var endIndex = afterText.IndexOf(' ');
            var extracted = endIndex >= 0 ? afterText.Substring(0, endIndex) : afterText;
            result = result.Replace("{afterMatch:untilSpace}", extracted);
        }

        if (result.Contains("{afterMatch}"))
        {
            var afterText = GetAfterMatch(content, matchedText);
            result = result.Replace("{afterMatch}", afterText);
        }

        if (result.Contains("{beforeMatch}"))
        {
            var index = content.IndexOf(matchedText);
            var beforeText = index >= 0 ? content.Substring(0, index) : string.Empty;
            result = result.Replace("{beforeMatch}", beforeText);
        }

        var extractPattern = @"\{extract:([^}]+)\}";
        var extractMatch = Regex.Match(result, extractPattern);
        if (extractMatch.Success)
        {
            var regex = extractMatch.Groups[1].Value;
            var regexMatch = Regex.Match(content, regex);
            var extracted = regexMatch.Success && regexMatch.Groups.Count > 1 ? regexMatch.Groups[1].Value : regexMatch.Value;
            result = Regex.Replace(result, extractPattern, extracted);
        }

        return result;
    }

    private static string GetAfterMatch(string content, string matchedText)
    {
        var index = content.IndexOf(matchedText);
        return index >= 0 ? content.Substring(index + matchedText.Length) : string.Empty;
    }
}

public class LogMessage
{
    public string Content { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTime? Timestamp { get; set; }
    /// <summary>Stable id of the source result row (so callers can map a diagram match back to the
    /// row in the results viewer). -1 when unknown.</summary>
    public long RowId { get; set; } = -1;
}
