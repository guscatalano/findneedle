namespace FindNeedlePluginUtils.UmlDsl;

/// <summary>
/// Represents a resolved UML element ready for rendering.
/// </summary>
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

/// <summary>
/// Interface for translating resolved UML elements to specific syntax.
/// </summary>
public interface IUmlSyntaxTranslator
{
    /// <summary>
    /// Gets the name of the UML syntax (e.g., "PlantUML", "Mermaid").
    /// </summary>
    string SyntaxName { get; }

    /// <summary>
    /// Gets the file extension for the output (e.g., ".pu", ".mmd").
    /// </summary>
    string FileExtension { get; }

    /// <summary>
    /// Generates the diagram header/preamble.
    /// </summary>
    string GenerateHeader(UmlRuleDefinition definition);

    /// <summary>
    /// Generates syntax for a single UML element.
    /// </summary>
    string GenerateElement(ResolvedUmlElement element);

    /// <summary>
    /// Generates the diagram footer/closing.
    /// </summary>
    string GenerateFooter();

    /// <summary>
    /// Generates participant/actor declarations.
    /// </summary>
    string GenerateParticipants(List<UmlParticipant> participants);
}
