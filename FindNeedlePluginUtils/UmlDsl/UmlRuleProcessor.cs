using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FindNeedlePluginUtils.UmlDsl;

/// <summary>
/// Processes UML rules from JSON and generates UML output using the specified translator.
/// </summary>
public class UmlRuleProcessor
{
    private readonly IUmlSyntaxTranslator _translator;
    private UmlRuleDefinition _definition = new();

    public UmlRuleProcessor(IUmlSyntaxTranslator translator)
    {
        _translator = translator;
    }

    /// <summary>
    /// Loads rules from a JSON string.
    /// </summary>
    public void LoadRulesFromJson(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        _definition = JsonSerializer.Deserialize<UmlRuleDefinition>(json, options) 
            ?? throw new InvalidOperationException("Failed to deserialize UML rules");
    }

    /// <summary>
    /// Loads rules from a JSON file.
    /// </summary>
    public void LoadRulesFromFile(string filePath)
    {
        var json = File.ReadAllText(filePath);
        LoadRulesFromJson(json);
    }

    /// <summary>
    /// Loads rules from a UmlRuleDefinition object.
    /// </summary>
    public void LoadRules(UmlRuleDefinition definition)
    {
        _definition = definition;
    }

    /// <summary>
    /// Processes a list of log messages and returns the generated UML string.
    /// </summary>
    public string ProcessMessages(IEnumerable<LogMessage> messages)
    {
        var sb = new StringBuilder();
        sb.Append(_translator.GenerateHeader(_definition));
        sb.Append(_translator.GenerateParticipants(_definition.Participants));
        sb.AppendLine();

        foreach (var message in messages)
        {
            foreach (var rule in _definition.Rules)
            {
                if (MatchesRule(message, rule))
                {
                    var element = ResolveElement(message, rule);
                    sb.AppendLine(_translator.GenerateElement(element));
                }
            }
        }

        var footer = _translator.GenerateFooter();
        if (!string.IsNullOrEmpty(footer))
        {
            sb.AppendLine(footer);
        }

        return sb.ToString();
    }

    private bool MatchesRule(LogMessage message, UmlRule rule)
    {
        var content = message.Content;
        
        if (!content.Contains(rule.Match))
            return false;

        if (!string.IsNullOrEmpty(rule.Unmatch) && content.Contains(rule.Unmatch))
            return false;

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

    /// <summary>
    /// Resolves placeholders in the text template.
    /// Supported placeholders:
    /// - {afterMatch} - Everything after the matched text
    /// - {afterMatch:untilSpace} - Text after match until first space
    /// - {afterMatch:until:X} - Text after match until character X
    /// - {beforeMatch} - Everything before the matched text
    /// - {extract:regex} - Regex capture group
    /// </summary>
    private string ResolvePlaceholders(string template, string content, string matchedText)
    {
        var result = template;

        // {afterMatch:until:X}
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

        // {afterMatch:untilSpace}
        if (result.Contains("{afterMatch:untilSpace}"))
        {
            var afterText = GetAfterMatch(content, matchedText);
            var endIndex = afterText.IndexOf(' ');
            var extracted = endIndex >= 0 ? afterText.Substring(0, endIndex) : afterText;
            result = result.Replace("{afterMatch:untilSpace}", extracted);
        }

        // {afterMatch}
        if (result.Contains("{afterMatch}"))
        {
            var afterText = GetAfterMatch(content, matchedText);
            result = result.Replace("{afterMatch}", afterText);
        }

        // {beforeMatch}
        if (result.Contains("{beforeMatch}"))
        {
            var index = content.IndexOf(matchedText);
            var beforeText = index >= 0 ? content.Substring(0, index) : string.Empty;
            result = result.Replace("{beforeMatch}", beforeText);
        }

        // {extract:regex}
        var extractPattern = @"\{extract:([^}]+)\}";
        var extractMatch = Regex.Match(result, extractPattern);
        if (extractMatch.Success)
        {
            var regex = extractMatch.Groups[1].Value;
            var regexMatch = Regex.Match(content, regex);
            var extracted = regexMatch.Success && regexMatch.Groups.Count > 1 
                ? regexMatch.Groups[1].Value 
                : regexMatch.Value;
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

/// <summary>
/// Represents a log message to be processed.
/// </summary>
public class LogMessage
{
    public string Content { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTime? Timestamp { get; set; }
}
