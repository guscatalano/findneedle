using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicTextPlugin;
using findneedle.Implementations;
using FindNeedlePluginLib;
using FindNeedlePluginLib.Interfaces;
using FindPluginCore.PluginSubsystem;
using FindPluginCore.Searching;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Integration;

/// <summary>
/// End-to-end proof that a RuleDSL "extract" enrichment rule writes fields into the stored row during
/// the scan: ProcessId / ThreadId / Provider become real columns (queryable) when enrichment is on, and
/// the row is untouched when it's off. Uses a real DISM-shaped .log through FolderLocation +
/// PlainTextProcessor + NuSearchQuery (InMemory storage).
/// </summary>
[TestClass]
public class EnrichmentEndToEndTests
{
    private string _dir = null!;
    private string _logPath = null!;
    private string _rulesPath = null!;

    private const string RulesJson = @"{
      ""schemaVersion"": ""2.0"", ""version"": ""1.0"", ""title"": ""DISM fields"",
      ""sections"": [ {
        ""name"": ""DismFields"", ""purpose"": ""enrichment"", ""providers"": [""*""],
        ""rules"": [
          { ""name"":""prov"", ""field"":""message"", ""match"":""\\bDISM\\b"",
            ""action"": { ""type"":""extract"", ""pattern"":""\\bDISM\\b"", ""set"": { ""Source"":""DISM"" } } },
          { ""name"":""pidtid"", ""field"":""message"", ""match"":""PID=\\d+\\s+TID=\\d+"",
            ""action"": { ""type"":""extract"", ""pattern"":""PID=(?<pid>\\d+)\\s+TID=(?<tid>\\d+)"",
                          ""set"": { ""ProcessId"":""{pid}"", ""ThreadId"":""{tid}"" } } }
        ] } ] }";

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "FN_enrich_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _logPath = Path.Combine(_dir, "dism.log");
        File.WriteAllLines(_logPath, new[]
        {
            "2026-06-20 09:33:36, Info                  DISM   PID=24288 TID=15484 Successfully loaded the ImageSession",
            "2026-06-20 09:33:37, Info                  DISM   PID=24288 TID=15484 second dism line",
            "an unrelated line without the marker or pid",
        });
        _rulesPath = Path.Combine(_dir, "dism-fields.rules.json");
        File.WriteAllText(_rulesPath, RulesJson);
    }

    [TestCleanup]
    public void Cleanup() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private static FolderLocation LogLocation(string path)
    {
        var loc = new FolderLocation { path = path };
        loc.SetExtensionProcessorList(new List<IFileExtensionProcessor> { new PlainTextProcessor() });
        return loc;
    }

    private List<ISearchResult> RunAndRead(bool enrich)
    {
        var query = new NuSearchQuery { OverrideStorageType = StorageType.InMemory, EnrichmentEnabled = enrich };
        query.RulesConfigPaths.Add(_rulesPath);
        query.Locations.Add(LogLocation(_logPath));
        query.RunThrough();

        var rows = new List<ISearchResult>();
        query.ResultStorage!.GetFilteredResultsInBatches(b => rows.AddRange(b), 1000);
        return rows;
    }

    [TestMethod]
    public void Enrichment_On_StoresExtractedFields()
    {
        var rows = RunAndRead(enrich: true);

        var dism = rows.First(r => r.GetMessage().Contains("Successfully loaded"));
        Assert.AreEqual("24288", dism.GetProcessId(), "PID extracted into ProcessId");
        Assert.AreEqual("15484", dism.GetThreadId(), "TID extracted into ThreadId");
        Assert.AreEqual("DISM", dism.GetSource(), "Provider set to DISM");

        // A non-DISM line is left alone.
        var other = rows.First(r => r.GetMessage().Contains("unrelated"));
        Assert.AreEqual("", other.GetProcessId());
        Assert.AreEqual("dism.log", other.GetSource(), "untouched rows keep the file basename as provider");
    }

    [TestMethod]
    public void Enrichment_Off_LeavesFieldsEmpty()
    {
        var rows = RunAndRead(enrich: false);

        var dism = rows.First(r => r.GetMessage().Contains("Successfully loaded"));
        Assert.AreEqual("", dism.GetProcessId(), "no enrichment ⇒ ProcessId stays empty");
        Assert.AreEqual("", dism.GetThreadId());
        Assert.AreEqual("dism.log", dism.GetSource(), "no enrichment ⇒ provider stays the file basename");
    }
}
