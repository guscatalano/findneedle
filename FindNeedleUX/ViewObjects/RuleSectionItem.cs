namespace FindNeedleUX.ViewObjects;

/// <summary>
/// Represents a rule section from a rule file in the UI.
/// </summary>
public class RuleSectionItem
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty; // "filter", "enrichment", "uml"
    public bool Enabled { get; set; } = true;
    public int RuleCount { get; set; } = 0;
    public string SourceFile { get; set; } = string.Empty;
    public string SourceFileName { get; set; } = string.Empty;

    public string PurposeDisplay => Purpose switch
    {
        "filter" => "Filter",
        "enrichment" => "Enrichment",
        "uml" => "UML Diagram",
        "output" => "Output/Export",
        _ => Purpose
    };
}
