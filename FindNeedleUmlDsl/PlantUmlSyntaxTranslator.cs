using System.Text;
using System.Collections.Generic;

namespace FindNeedleUmlDsl;

public class PlantUmlSyntaxTranslator : IUmlSyntaxTranslator
{
    public string SyntaxName => "PlantUML";
    public string FileExtension => ".pu";

    public string GenerateHeader(UmlRuleDefinition definition)
    {
        var sb = new StringBuilder();
        sb.AppendLine("@startuml");
        if (!string.IsNullOrEmpty(definition.Title))
        {
            sb.AppendLine($"title {definition.Title}");
        }
        return sb.ToString();
    }

    public string GenerateParticipants(List<UmlParticipant> participants)
    {
        var sb = new StringBuilder();
        foreach (var p in participants)
        {
            var displayName = p.DisplayName ?? p.Id;
            var keyword = p.Type.ToLower() switch
            {
                "actor" => "actor",
                "database" => "database",
                "queue" => "queue",
                "entity" => "entity",
                "boundary" => "boundary",
                "control" => "control",
                "collections" => "collections",
                _ => "participant"
            };

            if (displayName != p.Id)
            {
                sb.AppendLine($"{keyword} \"{displayName}\" as {p.Id}");
            }
            else
            {
                sb.AppendLine($"{keyword} {p.Id}");
            }
        }
        return sb.ToString();
    }

    public string GenerateElement(ResolvedUmlElement element)
    {
        return element.Type.ToLower() switch
        {
            "message" => GenerateMessage(element),
            "note" => GenerateNote(element),
            "activate" => $"activate {element.From}",
            "deactivate" => $"deactivate {element.From}",
            "divider" => $"== {element.Text} ==",
            "delay" => $"...{element.Text}...",
            "group" => $"group {element.Text}",
            "groupend" => "end",
            _ => $"' Unknown element type: {element.Type}"
        };
    }

    private string GenerateMessage(ResolvedUmlElement element)
    {
        var arrow = element.ArrowStyle.ToLower() switch
        {
            "dashed" => "-->",
            "async" => "->>",
            "dotted" => "..>",
            "response" => "-->",
            _ => "->"
        };
        return $"{element.From} {arrow} {element.To} : {element.Text}";
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
        return $"{position} : {element.Text}";
    }

    public string GenerateFooter()
    {
        return "@enduml";
    }
}
