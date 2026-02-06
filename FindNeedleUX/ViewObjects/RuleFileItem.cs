using System.Collections.Generic;

namespace FindNeedleUX.ViewObjects;

/// <summary>
/// Represents a single rule file (.rules.json) in the UI.
/// </summary>
public class RuleFileItem
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public List<RuleSectionItem> Sections { get; set; } = new();
    public bool IsValid { get; set; } = true;
    public string? ValidationError { get; set; }
}
