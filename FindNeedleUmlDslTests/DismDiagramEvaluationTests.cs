using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FindNeedleUmlDsl;
using FindNeedleUmlDsl.MermaidUML;

namespace FindNeedleUmlDslTests;

/// <summary>
/// Exercises the shipped DISM UML rules (CommonRules/dism-interaction-uml.rules.json) over realistic,
/// multi-session dism.log content covering every rule, plus noise lines that must NOT match. Also dumps
/// the rendered diagram to %TEMP%\fn_dism_eval.mmd for manual inspection / evaluation.
/// </summary>
[TestClass]
public class DismDiagramEvaluationTests
{
    // One full DISM session touching every milestone rule. Real dism.log formatting (padded columns,
    // "PID= TID=", trailing " - Function").
    private static IEnumerable<string> Session(int pid) => new[]
    {
        $"2026-06-20 09:33:36, Info                  DISM   API: PID={pid} TID=16576 DismApi.dll: <----- Starting DismApi.dll session for DISM.EXE -----> - DismInitializeInternal",
        $"2026-06-20 09:33:36, Info                  DISM   API: PID={pid} TID=16576 DismApi.dll: Host machine information: OS Version=10.0.26200, Running architecture=amd64, Number of processors=16 - DismInitializeInternal",
        $"2026-06-20 09:33:36, Info                  DISM   API: PID={pid} TID=16576 Created g_internalDismSession - DismInitializeInternal",
        $"2026-06-20 09:33:36, Info                  DISM   PID={pid} TID=15484 Successfully loaded the ImageSession at \"C:\\WINDOWS\\system32\\Dism\" - CDISMManager::LoadLocalImageSession",
        $"2026-06-20 09:33:36, Info                  DISM   DISM Provider Store: PID={pid} TID=15484 Found and Initialized the DISM Logger. - CDISMProviderStore::Internal_InitializeLogger",
        $"2026-06-20 09:33:36, Info                  DISM   DISM Manager: PID={pid} TID=15484 Successfully created the local image session and provider store. - CDISMManager::CreateLocalImageSession",
        // noise — must not match any rule
        $"2026-06-20 09:33:37, Info                  DISM   DISM Provider Store: PID={pid} TID=15484 Connecting to the provider located at C:\\Windows - CDISMProviderStore::Internal_GetProvider",
        $"2026-06-20 09:33:37, Info                  DISM   DISM Manager: PID={pid} TID=15484 Enter CCommandThread::ExecuteLoop - CCommandThread::ExecuteLoop",
        $"2026-06-20 09:33:38, Info                  DISM   DISM Imaging Provider: PID={pid} TID=15484 The provider FfuManager does not support CreateDismImage on C:\\ - CGenericImagingManager::CreateDismImage",
        $"2026-06-20 09:33:38, Info                  DISM   DISM Imaging Provider: PID={pid} TID=15484 The provider WimManager successfully created and initialized the image - CGenericImagingManager::CreateDismImage",
        $"2026-06-20 09:33:40, Info                  DISM   API: PID={pid} TID=16576 DismApi.dll: <----- Ending DismApi.dll session -----> - DismShutdownInternal",
    };

    private static string FindUmlRules()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            var c = Path.Combine(dir.FullName, "FindNeedleUX", "CommonRules", "dism-interaction-uml.rules.json");
            if (File.Exists(c)) return c;
            dir = dir.Parent;
        }
        return null;
    }

    private static UmlRuleProcessor LoadProcessor()
    {
        var rules = FindUmlRules();
        Assert.IsNotNull(rules, "shipped dism-interaction-uml.rules.json must be found");
        var p = new UmlRuleProcessor(new MermaidSyntaxTranslator());
        p.LoadRulesFromFile(rules);
        return p;
    }

    private static List<LogMessage> Messages(int sessions)
    {
        var list = new List<LogMessage>();
        for (int i = 0; i < sessions; i++)
            list.AddRange(Session(24000 + i).Select(l => new LogMessage { Content = l }));
        return list;
    }

    [TestMethod]
    public void Diagram_CoversEveryMilestone_AndDumpsForReview()
    {
        var p = LoadProcessor();
        var diagram = p.ProcessMessages(Messages(sessions: 3));

        // Dump for manual inspection / evaluation (not cleaned up on purpose).
        var dump = Path.Combine(Path.GetTempPath(), "fn_dism_eval.mmd");
        File.WriteAllText(dump, diagram);
        Console.WriteLine("Diagram written to: " + dump);
        Console.WriteLine(diagram);

        Assert.IsTrue(diagram.StartsWith("sequenceDiagram"), "is a Mermaid sequence diagram");

        // Participants.
        foreach (var pcp in new[] { "DismApi", "DISM Manager", "Provider Store", "Imaging Providers" })
            StringAssert.Contains(diagram, pcp, $"participant {pcp} present");

        // Each milestone rule produced its element.
        StringAssert.Contains(diagram, "DismApi session start");
        StringAssert.Contains(diagram, "10.0.26200");                       // host-info OS-version extract
        StringAssert.Contains(diagram, "amd64");                            // host-info architecture extract (distinct from OS)
        Assert.IsFalse(diagram.Contains("(10.0.26200)"),
            "the architecture placeholder must not repeat the OS version");
        StringAssert.Contains(diagram, "load local image session");
        StringAssert.Contains(diagram, "create image session");
        StringAssert.Contains(diagram, "provider store + logger ready");
        StringAssert.Contains(diagram, "image provider selected");          // provider-selected rule fired
        StringAssert.Contains(diagram, "shutdown session");

        // Provider-negotiate extracted the provider name.
        StringAssert.Contains(diagram, "Ffu");
    }

    [TestMethod]
    public void EveryDeclaredRule_Fires_AtLeastOnce()
    {
        var p = LoadProcessor();
        p.ProcessMessages(Messages(sessions: 1));
        var unfired = p.LastUsage.Where(u => u.Count == 0).Select(u => u.Name).ToList();
        Assert.AreEqual(0, unfired.Count,
            "every DISM rule should match the realistic session; unfired: " + string.Join(", ", unfired));
    }

    [TestMethod]
    public void NoiseLines_DoNotInflateTheDiagram()
    {
        var p = LoadProcessor();
        var noise = new List<LogMessage>
        {
            new() { Content = "2026-06-20 09:33:37, Info DISM DISM Manager: PID=1 TID=2 Enter CCommandThread::ExecuteLoop - X" },
            new() { Content = "2026-06-20 09:33:37, Info DISM DISM Provider Store: PID=1 TID=2 Connecting to the provider located at C:\\ - Y" },
            new() { Content = "totally unrelated application log line with no DISM structure" },
        };
        var diagram = p.ProcessMessages(noise);
        Assert.IsFalse(diagram.Contains("->>"), "noise emits no message arrows");
        Assert.IsFalse(diagram.Contains("Note "), "noise emits no notes");
    }

    [TestMethod]
    public void RepeatedSessions_AreCounted_NotDuplicated()
    {
        var p = LoadProcessor();
        var oneArrows = CountArrows(p.ProcessMessages(Messages(1)));
        var manyDiagram = p.ProcessMessages(Messages(7));
        Assert.AreEqual(oneArrows, CountArrows(manyDiagram), "dedupe keeps the shape stable across sessions");
        StringAssert.Contains(manyDiagram, "×7", "the 7 sessions are annotated as a count");
    }

    private static int CountArrows(string d) => d.Split('\n').Count(l => l.Contains("->>") || l.Contains("-->>"));
}
