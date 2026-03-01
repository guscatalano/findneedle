This folder contains sample UML rule definitions and a short guide to generate sequence diagrams from log messages.

Files
- `sample-uml.rules.json` - A small UML rule set that maps log message patterns to sequence diagram messages.
- `crash-detection-uml.rules.json` - (existing) example focused on crash detection.

How to generate a Mermaid (.mmd) or PlantUML (.pu) diagram from `FindNeedleRuleDSL/Examples/sample.log` using the code in this repo:

1. Build the repository so the `FindNeedleUmlDsl` project compiles.
2. Use the following sample code (C#) to produce a `.mmd` (Mermaid) file:

```csharp
using System;
using System.IO;
using System.Linq;
using FindNeedleUmlDsl;
using FindNeedleUmlDsl.MermaidUML;

class Gen
{
    static void Main()
    {
        var logPath = Path.Combine("..", "FindNeedleRuleDSL", "Examples", "sample.log");
        var rulesPath = Path.Combine("..", "FindNeedleUmlDsl", "Examples", "sample-uml.rules.json");

        var lines = File.ReadAllLines(logPath).Select(l => new UmlRuleProcessor.LogMessage { Content = l, Timestamp = DateTime.Now }).ToList();

        var translator = new MermaidSyntaxTranslator();
        var proc = new UmlRuleProcessor(translator);
        proc.LoadRulesFromFile(rulesPath);
        var mmd = proc.ProcessMessages(lines);
        File.WriteAllText("sample-output.mmd", mmd);
        Console.WriteLine("Mermaid output written: sample-output.mmd");
    }
}
```

3. Optionally render the `.mmd` to PNG/HTML using the Mermaid CLI (`mmdc`) or open the `.mmd` in a Mermaid live editor.

4. For PlantUML, use `FindNeedleUmlDsl.PlantUML.PlantUmlSyntaxTranslator` in place of `MermaidSyntaxTranslator`, write `.pu` file and render with PlantUML.

Notes
- The repository already includes `UmlRuleProcessor`, `MermaidSyntaxTranslator`, and `PlantUMLGenerator` implementations. Integrating this into the main `findneedle` search flow may require wiring in an output action that invokes the assembler and generator (considered advanced integration).
- If you'd like, I can add a small command-line utility under `tools/` that automates these steps and can be run by `dotnet run`.
