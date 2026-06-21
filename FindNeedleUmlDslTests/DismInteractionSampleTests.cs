using System.Collections.Generic;
using System.IO;
using System.Linq;
using FindNeedleUmlDsl;
using FindNeedleUmlDsl.MermaidUML;

namespace FindNeedleUmlDslTests;

/// <summary>
/// Verifies the shipped generic DISM UML rules (CommonRules/dism-interaction-uml.rules.json) turn real
/// DISM milestone lines into a Mermaid sequence diagram (DismApi → Manager → Provider Store →
/// providers). The sample lines are representative dism.log entries (verified against a live log).
/// </summary>
[TestClass]
public class DismInteractionSampleTests
{
    // Representative dism.log lines covering the session milestones the rules key off of.
    private static readonly string[] DismLog =
    {
        "2026-06-20 09:33:36, Info DISM API: PID=24288 TID=16576 DismApi.dll: <----- Starting DismApi.dll session -----> - DismInitializeInternal",
        "2026-06-20 09:33:36, Info DISM API: PID=24288 TID=16576 DismApi.dll: Host machine information: OS Version=10.0.26200, Running architecture=amd64, Number of processors=16 - DismInitializeInternal",
        "2026-06-20 09:33:36, Info DISM API: PID=24288 TID=16576 Created g_internalDismSession - DismInitializeInternal",
        "2026-06-20 09:33:36, Info DISM PID=24288 TID=15484 Successfully loaded the ImageSession at \"C:\\WINDOWS\\system32\\Dism\" - CDISMManager::LoadLocalImageSession",
        "2026-06-20 09:33:36, Info DISM DISM Provider Store: PID=24288 TID=15484 Found and Initialized the DISM Logger. - CDISMProviderStore::Internal_InitializeLogger",
        "2026-06-20 09:33:36, Info DISM DISM Manager: PID=24288 TID=15484 Successfully created the local image session and provider store. - CDISMManager::CreateLocalImageSession",
        "2026-06-20 09:33:36, Info DISM DISM FFU Provider: PID=24288 TID=15484 [C:\\] is not recognized by the DISM FFU provider. - CFfuImage::Initialize",
        "2026-06-20 09:33:36, Info DISM DISM Imaging Provider: PID=24288 TID=15484 The provider FfuManager does not support CreateDismImage on C:\\ - CGenericImagingManager::CreateDismImage",
        "2026-06-20 09:33:40, Info DISM API: PID=24288 TID=16576 DismApi.dll: <----- Ending DismApi.dll session -----> - DismShutdownInternal",
    };

    private static string FindRulesFile()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "FindNeedleUX", "CommonRules", "dism-interaction-uml.rules.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [TestMethod]
    public void DismLog_ProducesSequenceDiagram()
    {
        var rules = FindRulesFile();
        Assert.IsNotNull(rules, "the bundled dism-interaction-uml.rules.json should be found in the repo");

        var processor = new UmlRuleProcessor(new MermaidSyntaxTranslator());
        processor.LoadRulesFromFile(rules);

        var messages = DismLog.Select(l => new LogMessage { Content = l }).ToList();
        var diagram = processor.ProcessMessages(messages);

        Assert.IsTrue(diagram.Contains("sequenceDiagram"), "should be a Mermaid sequence diagram");
        // Participants present.
        StringAssert.Contains(diagram, "DismApi");
        StringAssert.Contains(diagram, "DISM Manager");
        StringAssert.Contains(diagram, "Provider Store");
        // Key milestone messages present.
        Assert.IsTrue(diagram.Contains("load local image session"), "load-image-session message");
        Assert.IsTrue(diagram.Contains("create image session"), "create-image-session message");
        Assert.IsTrue(diagram.Contains("10.0.26200"), "OS version extracted from host info note");
    }

    [TestMethod]
    public void DismRules_OnlyFireOnMilestones_NotEveryLine()
    {
        var rules = FindRulesFile();
        var processor = new UmlRuleProcessor(new MermaidSyntaxTranslator());
        processor.LoadRulesFromFile(rules);

        // A high-frequency, non-milestone line shouldn't add anything to the diagram.
        var noise = new List<LogMessage>
        {
            new() { Content = "2026-06-20 09:33:36, Info DISM API: PID=24288 TID=16576 Enter CCommandThread::ExecuteLoop - CCommandThread::ExecuteLoop" },
        };
        var diagram = processor.ProcessMessages(noise);
        // Only the sequenceDiagram header + participants, no message/note lines.
        Assert.IsFalse(diagram.Contains("->>"), "noise line should not emit a message arrow");
        Assert.IsFalse(diagram.Contains("note "), "noise line should not emit a note");
    }
}
