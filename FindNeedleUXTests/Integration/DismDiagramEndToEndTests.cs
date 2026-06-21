using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicTextPlugin;
using FindNeedlePluginLib;
using findneedle.Implementations;
using FindPluginCore.PluginSubsystem;
using FindPluginCore.Searching;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Integration;

/// <summary>
/// Full-chain test of the shipped DISM diagram feature: a real dism.log → NuSearchQuery with the shipped
/// emitter rule (CommonRules/dism-interaction.rules.json) → deferred output → GenerateOutputsNow → a
/// Mermaid .mmd written by the real OutputRuleProcessor, which resolves and runs the shipped UML rules
/// file. Exercises rule loading, rulesFile resolution, dedupe, and the on-demand generation path.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class DismDiagramEndToEndTests
{
    private string _dir = null!;
    private string _logPath = null!;
    private string _emitterRule = null!;

    private static string FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            var c = Path.Combine(dir.FullName, relative);
            if (File.Exists(c)) return c;
            dir = dir.Parent;
        }
        return null;
    }

    private static IEnumerable<string> Session(int pid) => new[]
    {
        $"2026-06-20 09:33:36, Info DISM API: PID={pid} TID=16576 DismApi.dll: <----- Starting DismApi.dll session -----> - DismInitializeInternal",
        $"2026-06-20 09:33:36, Info DISM API: PID={pid} TID=16576 DismApi.dll: Host machine information: OS Version=10.0.26200, Running architecture=amd64 - DismInitializeInternal",
        $"2026-06-20 09:33:36, Info DISM API: PID={pid} TID=16576 Created g_internalDismSession - DismInitializeInternal",
        $"2026-06-20 09:33:36, Info DISM PID={pid} TID=15484 Successfully loaded the ImageSession at \"C:\\WINDOWS\\system32\\Dism\" - CDISMManager::LoadLocalImageSession",
        $"2026-06-20 09:33:36, Info DISM DISM Manager: PID={pid} TID=15484 Successfully created the local image session and provider store. - CDISMManager::CreateLocalImageSession",
        $"2026-06-20 09:33:36, Info DISM DISM Provider Store: PID={pid} TID=15484 Found and Initialized the DISM Logger. - CDISMProviderStore::Internal_InitializeLogger",
        $"2026-06-20 09:33:38, Info DISM DISM Imaging Provider: PID={pid} TID=15484 The provider FfuManager does not support CreateDismImage on C:\\ - CGenericImagingManager::CreateDismImage",
        $"2026-06-20 09:33:40, Info DISM API: PID={pid} TID=16576 DismApi.dll: <----- Ending DismApi.dll session -----> - DismShutdownInternal",
    };

    [TestInitialize]
    public void Setup()
    {
        _emitterRule = FindRepoFile(Path.Combine("FindNeedleUX", "CommonRules", "dism-interaction.rules.json"));
        Assert.IsNotNull(_emitterRule, "shipped DISM emitter rule must be present");

        _dir = Path.Combine(Path.GetTempPath(), $"FN_dismE2E_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _logPath = Path.Combine(_dir, "dism.log");
        var lines = new List<string>();
        for (int i = 0; i < 4; i++) lines.AddRange(Session(24000 + i)); // 4 sessions → dedupe + counts
        File.WriteAllLines(_logPath, lines);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static FolderLocation LogLocation(string path)
    {
        var loc = new FolderLocation { path = path };
        loc.SetExtensionProcessorList(new List<IFileExtensionProcessor> { new PlainTextProcessor() });
        return loc;
    }

    [TestMethod]
    public void DismLog_GeneratesMermaidDiagram_OnDemand()
    {
        var q = new NuSearchQuery { OverrideStorageType = StorageType.InMemory, DeferOutputs = true };
        q.Locations.Add(LogLocation(_logPath));
        q.RulesConfigPaths = new List<string> { _emitterRule };

        q.Step1_LoadAllLocationsInMemory();
        q.Step2_GetFilteredResults();
        q.Step3_ResultsToProcessors();
        q.Step4_ProcessAllResultsToOutput();

        Assert.IsTrue(q.HasOutputRules, "the DISM emitter is an output rule");
        Assert.IsTrue(q.GeneratedRuleOutputFiles.Count == 0, "deferred: nothing generated during the search");

        // Explicit, on-demand generation.
        var files = q.GenerateOutputsNow();
        var mmd = files.FirstOrDefault(f => f.EndsWith(".mmd", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(mmd, "a Mermaid .mmd diagram should be produced; got: " + string.Join(", ", files));

        var diagram = File.ReadAllText(mmd);
        StringAssert.StartsWith(diagram, "sequenceDiagram");
        StringAssert.Contains(diagram, "DismApi");
        StringAssert.Contains(diagram, "Provider Store");
        StringAssert.Contains(diagram, "load local image session");
        StringAssert.Contains(diagram, "shutdown session");
        // Dedupe across the 4 sessions: counted, not duplicated.
        StringAssert.Contains(diagram, "×4");
        // The two host-info extracts resolved independently (regression guard).
        StringAssert.Contains(diagram, "amd64");
        Assert.IsFalse(diagram.Contains("(10.0.26200)"), "architecture must not echo the OS version");

        try { if (File.Exists(mmd)) File.Delete(mmd); } catch { }
    }
}
