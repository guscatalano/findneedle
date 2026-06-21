using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BasicTextPlugin;
using FindNeedlePluginLib;
using findneedle.Implementations;
using FindPluginCore.PluginSubsystem;
using FindPluginCore.Searching;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Integration;

/// <summary>
/// Verifies the UI's deferred-output behaviour: with <see cref="NuSearchQuery.DeferOutputs"/> set, a
/// search with only an output rule stays on the lazy path (no full-list consolidation, Step4 writes
/// nothing), and the output is produced only when <see cref="NuSearchQuery.GenerateOutputsNow"/> is
/// called. This is what keeps "just view a log" fast even with a UML/export rule enabled.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class DeferredOutputsTests
{
    private string _dir = null!;
    private string _logPath = null!;
    private string _rulePath = null!;
    private string _outCsv = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"FN_defer_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _logPath = Path.Combine(_dir, "sample.log");
        File.WriteAllLines(_logPath, new[]
        {
            "[2024-01-15 09:00:00] INFO: Application started",
            "[2024-01-15 09:01:23] ERROR: Database connection failed",
            "[2024-01-15 09:02:15] WARNING: Memory at 85%",
            "[2024-01-15 09:03:45] ERROR: File not found",
        });

        _outCsv = Path.Combine(_dir, "out.csv");
        _rulePath = Path.Combine(_dir, "out.rules.json");
        var rule = new
        {
            schemaVersion = "2.0",
            version = "1.0",
            title = "csv output",
            sections = new[]
            {
                new
                {
                    name = "Out",
                    purpose = "output",
                    providers = new[] { "*" },
                    rules = new[]
                    {
                        new
                        {
                            name = "csv",
                            match = ".*",
                            enabled = true,
                            action = new { type = "output", format = "csv", path = _outCsv,
                                fields = new[] { "time", "level", "message" } },
                        },
                    },
                },
            },
        };
        File.WriteAllText(_rulePath, JsonSerializer.Serialize(rule));
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

    private NuSearchQuery NewQuery(bool deferOutputs)
    {
        var q = new NuSearchQuery { OverrideStorageType = StorageType.InMemory, DeferOutputs = deferOutputs };
        q.Locations.Add(LogLocation(_logPath));
        q.RulesConfigPaths = new List<string> { _rulePath };
        return q;
    }

    private static void RunSteps(NuSearchQuery q)
    {
        q.Step1_LoadAllLocationsInMemory();
        q.Step2_GetFilteredResults();
        q.Step3_ResultsToProcessors();
        q.Step4_ProcessAllResultsToOutput();
    }

    [TestMethod]
    public void DeferOutputs_StaysLazy_AndWritesNothing_UntilGenerate()
    {
        var q = NewQuery(deferOutputs: true);
        RunSteps(q);

        Assert.IsTrue(q.HasOutputRules, "the rule set has an output section");
        Assert.AreEqual(0, q.CurrentResultList.Count,
            "output-only rule + DeferOutputs → search stays lazy (no consolidation)");
        Assert.IsFalse(File.Exists(_outCsv), "Step4 must not write the output when deferred");

        // Explicit, on-demand generation produces the file and consolidates the list.
        var files = q.GenerateOutputsNow();
        Assert.IsTrue(File.Exists(_outCsv), "GenerateOutputsNow should write the output");
        Assert.IsTrue(files.Any(f => string.Equals(f, _outCsv, StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(4, q.CurrentResultList.Count, "generation consolidated the rows from storage");
    }

    [TestMethod]
    public void WithoutDefer_OutputRunsDuringSearch()
    {
        // Baseline (the CLI/RunThrough behaviour): outputs run in Step4.
        var q = NewQuery(deferOutputs: false);
        RunSteps(q);
        Assert.IsTrue(File.Exists(_outCsv), "with outputs not deferred, Step4 writes the file");
    }
}
