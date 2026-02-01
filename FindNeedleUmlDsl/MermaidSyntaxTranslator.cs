using System.Text;
using System.Collections.Generic;

namespace FindNeedleUmlDsl;

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
            if (displayName != p.Id) sb.AppendLine($"    {keyword} {p.Id} as {displayName}"); else sb.AppendLine($"    {keyword} {p.Id}");
        }
        return sb.ToString();
    }

    public string GenerateElement(ResolvedUmlElement element)
    {
        var indent = "    ";
        return element.Type.ToLower() switch
        {
            "message" => indent + GenerateMessage(element),
            "note" => indent + GenerateNote(element),
            "activate" => $"{indent}activate {element.From}",
            "deactivate" => $"{indent}deactivate {element.From}",
            "divider" => $"{indent}Note over {element.From}: {element.Text}",
            "delay" => $"{indent}Note right of {element.From}: ...{element.Text}...",
            "group" => $"{indent}rect rgb(200, 200, 200)\n{indent}Note left of {element.From}: {element.Text}",
            "groupend" => $"{indent}end",
            _ => $"{indent}%% Unknown element type: {element.Type}"
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
        return $"{element.From}{arrow}{element.To}: {text}";
    }

    private string GenerateNote(ResolvedUmlElement element)
    {
        var position = element.NotePosition?.ToLower() switch
        {
            "left" => $"Note left of {element.From}",
            "right" => $"Note right of {element.From}",
            "over" => $"Note over {element.From}",
            _ => $"Note over {element.From}"
        };
        return $"{position}: {EscapeText(element.Text)}";
    }

    private static string EscapeText(string text) => text.Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    public string GenerateFooter() => string.Empty;
}
