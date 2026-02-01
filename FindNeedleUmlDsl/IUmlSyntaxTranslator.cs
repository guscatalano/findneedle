using System;
using System.Collections.Generic;

namespace FindNeedleUmlDsl;

public class ResolvedUmlElement
{
    public string Type { get; set; } = "message";
    public string? From { get; set; }
    public string? To { get; set; }
    public string Text { get; set; } = string.Empty;
    public string ArrowStyle { get; set; } = "solid";
    public string? NotePosition { get; set; }
    public DateTime? Timestamp { get; set; }
}

public interface IUmlSyntaxTranslator
{
    string SyntaxName { get; }
    string FileExtension { get; }
    string GenerateHeader(UmlRuleDefinition definition);
    string GenerateElement(ResolvedUmlElement element);
    string GenerateFooter();
    string GenerateParticipants(List<UmlParticipant> participants);
}
