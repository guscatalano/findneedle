using System.Collections.Generic;
using System.Text.Json;

namespace FindNeedleUX.Services.Mcp;

/// <summary>
/// Authoritative RuleDSL schema reference returned by the MCP <c>rule_schema</c> tool, so an agent can
/// author rules from the grammar + valid value sets instead of reverse-engineering them from examples.
/// </summary>
internal static class RuleSchemaHelp
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    private static string Json(object o) => JsonSerializer.Serialize(o, Pretty);

    public static object Schema() => new
    {
        schema =
            "RuleDSL rule file (schemaVersion \"2.0\"):\n" +
            "{ schemaVersion, version?, title?, description?, sections: [ Section ] }\n" +
            "Section = { name, description?, purpose: filter|enrichment|output, providers: [\"*\"], rules: [ Rule ] }\n" +
            "Rule = { name, match: <.NET regex over the rule's field, default message>, unmatch?: <regex>, enabled?: true, action: Action }\n" +
            "Action = { type, ...type-specific }:\n" +
            "  filter:     type=include (keep matches) | exclude (drop matches)\n" +
            "  enrichment: type=extract { pattern: <regex with (?<name>...) groups>, set: { targetField: \"{name}\" }, strip?: bool }\n" +
            "            | type=tag { value }  |  type=redact { replacement }\n" +
            "  output:     type=uml { syntax: mermaid|plantuml, path: \"{output}/x_{datetime}.mmd\", generateImage: bool, rulesFile? }\n" +
            "            | type=output { format: csv|json|xml|txt, path, fields?: [..], includeHeaders?: bool }",
        purposes = new[] { "filter", "enrichment", "output" },
        actionTypesByPurpose = new
        {
            filter = new[] { "include", "exclude" },
            enrichment = new[] { "extract", "tag", "redact" },
            output = new[] { "uml", "output" },
        },
        matchFields = new[]
        {
            "message", "provider", "taskname", "source", "level",
            "processid", "threadid", "eventid", "channel", "machinename", "username", "opcode",
        },
        notes = new[]
        {
            "match/unmatch are .NET regular expressions (use (?i) for case-insensitive); they run against the message text.",
            "A UML output rule always writes its .mmd/.puml text; the rendered image is skipped if the tool is missing — check uml_tools and install_uml_tool.",
            "Workflow: validate_rule(json) -> save_rule(name,json) -> set_rules([name]) -> run_search(ignoreCache:true). read_rule / rule_examples give working templates.",
        },
        examples = new[]
        {
            new { title = "filter: keep only errors and warnings", json = FilterExample() },
            new { title = "enrichment: extract pid/tid from the message into columns", json = ExtractExample() },
            new { title = "output: Mermaid sequence diagram", json = UmlExample() },
        },
    };

    private static string FilterExample() => Json(new
    {
        schemaVersion = "2.0",
        title = "Errors and warnings only",
        sections = new[]
        {
            new
            {
                name = "ErrorsOnly",
                purpose = "filter",
                providers = new[] { "*" },
                rules = new[]
                {
                    new { name = "keep-errors", match = "(?i)error|warn", action = new { type = "include" } },
                },
            },
        },
    });

    private static string ExtractExample() => Json(new
    {
        schemaVersion = "2.0",
        title = "Extract process and thread ids",
        sections = new[]
        {
            new
            {
                name = "Ids",
                purpose = "enrichment",
                providers = new[] { "*" },
                rules = new[]
                {
                    new
                    {
                        name = "pid-tid",
                        match = "pid=(?<pid>[0-9]+) tid=(?<tid>[0-9]+)",
                        action = (object)new
                        {
                            type = "extract",
                            pattern = "pid=(?<pid>[0-9]+) tid=(?<tid>[0-9]+)",
                            set = new Dictionary<string, string> { ["processid"] = "{pid}", ["threadid"] = "{tid}" },
                        },
                    },
                },
            },
        },
    });

    private static string UmlExample() => Json(new
    {
        schemaVersion = "2.0",
        title = "Mermaid sequence diagram",
        sections = new[]
        {
            new
            {
                name = "Seq",
                purpose = "output",
                providers = new[] { "*" },
                rules = new[]
                {
                    new
                    {
                        name = "emit-sequence",
                        match = "started|stopped|connected",
                        action = (object)new
                        {
                            type = "uml",
                            syntax = "mermaid",
                            path = "{output}/diagram_{datetime}.mmd",
                            generateImage = false,
                        },
                    },
                },
            },
        },
    });
}
