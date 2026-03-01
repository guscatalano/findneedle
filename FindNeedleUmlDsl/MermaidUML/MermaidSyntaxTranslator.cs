using System.Text;
using System.Collections.Generic;

namespace FindNeedleUmlDsl.MermaidUML;

public class MermaidSyntaxTranslator : IUmlSyntaxTranslator
{
    public string SyntaxName => "Mermaid";
    public string FileExtension => ".mmd";

    public string GenerateHeader(UmlRuleDefinition definition)
    {
        var sb = new StringBuilder();
        sb.AppendLine("sequenceDiagram");
        if (!string.IsNullOrEmpty(definition.Title)) sb.AppendLine($"    title {definition.Title}");
        return sb.ToString();
    }

    public string GenerateParticipants(List<UmlParticipant> participants)
    {
        var sb = new StringBuilder();
        foreach (var p in participants)
        {
            var displayName = p.DisplayName ?? p.Id;
            var keyword = p.Type.ToLower() switch { "actor" => "actor", _ => "participant" };
            // If display name contains whitespace or special chars, quote it for mermaid
            var needsQuote = displayName != p.Id && (displayName.IndexOf(' ') >= 0 || displayName.IndexOf('.') >= 0 || displayName.IndexOf('-') >= 0);
            if (displayName != p.Id)
            {
                if (needsQuote)
                {
                    sb.AppendLine($"    {keyword} {p.Id} as \"{EscapeText(displayName)}\"");
                }
                else
                {
                    sb.AppendLine($"    {keyword} {p.Id} as {displayName}");
                }
            }
            else
            {
                sb.AppendLine($"    {keyword} {p.Id}");
            }
        }
        return sb.ToString();
    }

    public string GenerateElement(ResolvedUmlElement element)
    {
        var indent = "    ";
        return element.Type.ToLower() switch
        {
            "message" => indent + GenerateMessage(element) + "\n",
            // Emit single-line notes (lowercase 'note') to match mermaid parser expectations
            "note" => indent + GenerateNote(element) + "\n",
            "activate" => $"{indent}activate {element.From}\n",
            "deactivate" => $"{indent}deactivate {element.From}\n",
            // Use single-quoted notes consistently for divider/delay/group to avoid parser issues
            "divider" => $"{indent}note over {element.From}: '{EscapeText(element.Text)}'\n",
            // For delays, emit a single-line note on the right
            "delay" => $"{indent}note right of {element.From}: '{EscapeText(element.Text)}'\n",
            "group" => $"{indent}rect rgb(200, 200, 200)\n{indent}note left of {element.From}: '{EscapeText(element.Text)}'\n",
            "groupend" => $"{indent}end\n",
            _ => $"{indent}%% Unknown element type: {element.Type}\n"
        };
    }

    private string GenerateMessage(ResolvedUmlElement element)
    {
        var arrow = element.ArrowStyle.ToLower() switch
        {
            "dashed" => "-->>",
            "async" => "-)+",
            "dotted" => "-->>",
            "response" => "-->>",
            _ => "->>"
        };

        var text = EscapeText(element.Text);
        // Wrap message text in single-quotes to avoid parser ambiguities; EscapeText will escape single quotes
        return $"{element.From}{arrow}{element.To}: '{text}'";
    }

    private string GenerateNote(ResolvedUmlElement element)
    {
        var position = element.NotePosition?.ToLower() switch
        {
            "left" => $"note left of {element.From}",
            "right" => $"note right of {element.From}",
            "over" => $"note over {element.From}",
            _ => $"note over {element.From}"
        };
        // Quote note text to avoid parser tokenization issues (use lowercase 'note' and single quotes)
        return $"{position}: '{EscapeText(element.Text)}'";
    }

    private static string EscapeText(string text)
    {
        if (text == null) return string.Empty;
        // Normalize whitespace and remove problematic newlines which break mermaid sequence lines
        var s = text.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ").Trim();

        // Remove common numbered-list markers at start (e.g. "1. ", "1) ") which can confuse the parser
        s = System.Text.RegularExpressions.Regex.Replace(s, "^\\s*\\d+[\\.\\)]\\s+", string.Empty);

        // Escape backslashes and quotes for safe quoting in mermaid
        s = s.Replace("\\", "\\\\");
        s = s.Replace("\"", "\\\"");
        // Escape single quotes since we emit single-quoted strings
        s = s.Replace("'", "\\'");

        // Escape angle brackets to avoid HTML interpretation
        s = s.Replace("<", "&lt;").Replace(">", "&gt;");

        return s;
    }

    public string GenerateFooter() => string.Empty;
}
