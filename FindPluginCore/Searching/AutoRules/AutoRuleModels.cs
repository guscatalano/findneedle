using System.Collections.Generic;

namespace FindPluginCore.Searching.AutoRules;

/// <summary>
/// A condition deciding whether an <see cref="AutoRuleEntry"/> should be auto-added to a search.
/// All <i>populated</i> criteria must match (logical AND); an empty criterion is ignored. If
/// <see cref="Always"/> is true the rule always applies regardless of the other fields. With no
/// criterion set and <see cref="Always"/> false, the condition never matches (so a half-filled
/// entry can't silently apply to everything).
/// </summary>
public sealed class AutoRuleCondition
{
    /// <summary>Apply to every search, regardless of content (a personal default ruleset).</summary>
    public bool Always { get; set; }

    /// <summary>File extensions (".etl", ".evtx", ".log"); matches if any loaded path has one.</summary>
    public List<string> Extensions { get; set; } = new();

    /// <summary>Source kinds present in the search (see <see cref="AutoRuleSourceKinds"/>).</summary>
    public List<string> SourceTypes { get; set; } = new();

    /// <summary>Glob patterns matched against loaded file paths (e.g. <c>*cbs*.log</c>).</summary>
    public List<string> PathGlobs { get; set; } = new();

    /// <summary>ETW/Event Log provider names; matches if any is present in the search's metadata.</summary>
    public List<string> Providers { get; set; } = new();

    /// <summary>Inclusive Windows build-number range (from ETW trace metadata). Null = unbounded.</summary>
    public int? MinBuild { get; set; }
    public int? MaxBuild { get; set; }
}

/// <summary>Canonical source-kind tokens used in <see cref="AutoRuleCondition.SourceTypes"/>.</summary>
public static class AutoRuleSourceKinds
{
    public const string Etw = "ETW";
    public const string EventLog = "EventLog";
    public const string Folder = "Folder";
    public const string File = "File";
    public const string Zip = "Zip";
}

/// <summary>One auto-add rule: a RuleDSL file plus the condition under which it auto-applies.</summary>
public sealed class AutoRuleEntry
{
    /// <summary>Stable id (GUID string) so the UI can edit/remove a specific entry.</summary>
    public string Id { get; set; } = System.Guid.NewGuid().ToString("N");

    /// <summary>Display name shown in the management UI and the "auto-added" list.</summary>
    public string Name { get; set; } = "";

    /// <summary>Absolute path to the <c>*.rules.json</c> file to apply.</summary>
    public string RulePath { get; set; } = "";

    /// <summary>True for rules that came from the bundled common library (vs. user-added).</summary>
    public bool BuiltIn { get; set; }

    /// <summary>Per-entry on/off. Disabled entries are never auto-added.</summary>
    public bool Enabled { get; set; } = true;

    public AutoRuleCondition Condition { get; set; } = new();
}

/// <summary>
/// Facts about the current search the resolver matches conditions against. Built from the loaded
/// locations (paths + kinds) plus any metadata that's already known (providers / build).
/// </summary>
public sealed class AutoRuleContext
{
    /// <summary>Loaded file paths (used for extension + glob matching).</summary>
    public List<string> Paths { get; set; } = new();

    /// <summary>Source kinds present (see <see cref="AutoRuleSourceKinds"/>).</summary>
    public HashSet<string> SourceTypes { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>Provider names known for the search (may be empty if not yet scanned).</summary>
    public HashSet<string> Providers { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>Windows build number from ETW metadata, if known.</summary>
    public int? Build { get; set; }
}
