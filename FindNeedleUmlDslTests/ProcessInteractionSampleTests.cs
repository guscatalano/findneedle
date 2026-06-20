using System.Collections.Generic;
using System.IO;
using System.Linq;
using FindNeedleUmlDsl;
using FindNeedleUmlDsl.MermaidUML;

namespace FindNeedleUmlDslTests;

/// <summary>
/// Verifies the bundled "process interaction" UML sample actually produces a Mermaid sequence diagram
/// from the demo log lines (client → broker → worker → store). Guards the shipped sample end-to-end:
/// uses the real CommonRules/process-interaction-uml.rules.json and the demo log content.
/// </summary>
[TestClass]
public class ProcessInteractionSampleTests
{
    // The demo log's lines (Samples/AutoRuleDemo/process-interaction-demo.log).
    private static readonly string[] DemoLog =
    {
        "Client process started pid=4120",
        "Client sending request to Broker (req=42)",
        "Broker received request from pid=4120 (req=42)",
        "Broker dispatching to Worker pid=7880 (req=42)",
        "Worker pid=7880 processing job 42",
        "Worker writing result to Store (job 42)",
        "Store persisted job 42",
        "Worker returning result to Broker (job 42)",
        "Broker responding to Client (req=42)",
        "Client received response (req=42)",
    };

    /// <summary>Locate the shipped uml rules file by walking up from the test bin to the repo.</summary>
    private static string FindRulesFile()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "FindNeedleUX", "CommonRules", "process-interaction-uml.rules.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [TestMethod]
    public void DemoLog_ProducesSequenceDiagram()
    {
        var rules = FindRulesFile();
        Assert.IsNotNull(rules, "the bundled process-interaction-uml.rules.json should be found in the repo");

        var processor = new UmlRuleProcessor(new MermaidSyntaxTranslator());
        processor.LoadRulesFromFile(rules);

        var messages = DemoLog.Select(l => new LogMessage { Content = l }).ToList();
        var diagram = processor.ProcessMessages(messages);

        Assert.IsTrue(diagram.Contains("sequenceDiagram"), "should be a Mermaid sequence diagram");
        // The handoffs across the four participants are present.
        StringAssert.Contains(diagram, "Broker");
        StringAssert.Contains(diagram, "Worker");
        StringAssert.Contains(diagram, "Store");
        // A couple of the actual messages.
        Assert.IsTrue(diagram.Contains("dispatch") || diagram.Contains("Worker"), "worker dispatch message present");
        Assert.IsTrue(diagram.Contains("write result") || diagram.Contains("Store"), "store write message present");
    }
}
