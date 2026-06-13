using System;
using System.IO;
using System.Linq;
using FindNeedlePluginLib;
using FindNeedlePluginLib.TestClasses;
using FindNeedleUX.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Unit tests for <see cref="SearchRulesPageViewModel"/>. Covers TESTING_PLAN.md
/// U-B1..U-B4: file loading happy path, malformed JSON, missing file, and
/// LoadRulesFromQuery re-entrancy guard. View-model has no WinUI dependencies
/// so these run without a window or dispatcher.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
public class SearchRulesPageViewModelTests
{
    private string _scratchDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _scratchDir = Path.Combine(Path.GetTempPath(), $"SearchRulesVM_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scratchDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_scratchDir)) Directory.Delete(_scratchDir, recursive: true); } catch { }
    }

    /// <summary>
    /// Test double that lets each test inspect <see cref="GetCurrentQuery"/> and
    /// <see cref="NotifyStateChanged"/> calls. No statics, no static event wiring.
    /// </summary>
    private sealed class FakeQueryState : IQueryStateService
    {
        public ISearchQuery? Query { get; set; }
        public int NotifyCount { get; private set; }
        public int GetCurrentQueryCount { get; private set; }

        public ISearchQuery? GetCurrentQuery()
        {
            GetCurrentQueryCount++;
            return Query;
        }

        public void NotifyStateChanged() => NotifyCount++;
    }

    private string WriteScratchFile(string name, string contents)
    {
        var path = Path.Combine(_scratchDir, name);
        File.WriteAllText(path, contents);
        return path;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // U-B1: Valid file → 1 valid item, sections parsed.
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void U_B1_LoadRuleFile_ValidFile_ProducesValidItemWithSections()
    {
        var rulesJson = @"{
            ""title"": ""Test"",
            ""sections"": [
                { ""name"": ""S1"", ""purpose"": ""filter"",     ""description"": ""d1"", ""rules"": [{}, {}] },
                { ""name"": ""S2"", ""purpose"": ""enrichment"", ""description"": ""d2"", ""rules"": [{}] }
            ]
        }";
        var path = WriteScratchFile("valid.rules.json", rulesJson);
        var vm = new SearchRulesPageViewModel(new FakeQueryState());

        vm.LoadRuleFile(path);

        Assert.AreEqual(1, vm.RuleFiles.Count, "exactly one RuleFileItem appended");
        Assert.IsTrue(vm.RuleFiles[0].IsValid, "valid file should be IsValid=true");
        Assert.IsNull(vm.RuleFiles[0].ValidationError);
        Assert.AreEqual(2, vm.RuleFiles[0].Sections.Count, "both sections parsed");
        Assert.AreEqual("S1", vm.RuleFiles[0].Sections[0].Name);
        Assert.AreEqual("filter", vm.RuleFiles[0].Sections[0].Purpose);
        Assert.AreEqual(2, vm.RuleFiles[0].Sections[0].RuleCount);
        Assert.AreEqual(2, vm.RuleSections.Count, "both visible under 'All' filter");
    }

    [TestMethod]
    public void U_B1_LoadRuleFile_ValidFile_SyncsToQuery()
    {
        var path = WriteScratchFile("v.rules.json", @"{ ""sections"": [] }");
        var fake = new FakeQueryState { Query = new FakeSearchQuery() };
        var vm = new SearchRulesPageViewModel(fake);

        vm.LoadRuleFile(path);

        Assert.AreEqual(1, fake.Query!.RulesConfigPaths.Count, "query path list should contain the loaded file");
        Assert.AreEqual(path, fake.Query.RulesConfigPaths[0]);
        Assert.AreEqual(1, fake.NotifyCount, "exactly one NotifyStateChanged on successful load");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // U-B2: Malformed JSON → 1 invalid item, ValidationError populated.
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void U_B2_LoadRuleFile_MalformedJson_ProducesInvalidItemWithError()
    {
        // Truncated JSON — fails JsonDocument.Parse.
        var path = WriteScratchFile("bad.rules.json", @"{ ""sections"": [ { ""name"":");
        var vm = new SearchRulesPageViewModel(new FakeQueryState());

        vm.LoadRuleFile(path);

        Assert.AreEqual(1, vm.RuleFiles.Count);
        Assert.IsFalse(vm.RuleFiles[0].IsValid, "malformed JSON → IsValid=false");
        Assert.IsNotNull(vm.RuleFiles[0].ValidationError, "ValidationError should carry the parse failure");
        Assert.IsTrue(vm.RuleFiles[0].ValidationError!.Length > 0);
        Assert.AreEqual(0, vm.RuleSections.Count, "no sections from a failed parse");
    }

    [TestMethod]
    public void U_B2_LoadRuleFile_InvalidFile_DoesNotSyncToQuery()
    {
        var path = WriteScratchFile("bad.rules.json", "{ broken");
        var fake = new FakeQueryState { Query = new FakeSearchQuery() };
        var vm = new SearchRulesPageViewModel(fake);

        vm.LoadRuleFile(path);

        // Invalid items must not be promoted into the query's authoritative path list.
        Assert.AreEqual(0, fake.Query!.RulesConfigPaths.Count);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // U-B3: File-not-found → invalid, "File not found" error.
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void U_B3_LoadRuleFile_MissingFile_ProducesInvalidItemWithFileNotFoundError()
    {
        var bogus = Path.Combine(_scratchDir, "does-not-exist.rules.json");
        Assert.IsFalse(File.Exists(bogus));
        var vm = new SearchRulesPageViewModel(new FakeQueryState());

        vm.LoadRuleFile(bogus);

        Assert.AreEqual(1, vm.RuleFiles.Count);
        Assert.IsFalse(vm.RuleFiles[0].IsValid);
        Assert.AreEqual("File not found", vm.RuleFiles[0].ValidationError);
        Assert.AreEqual(bogus, vm.RuleFiles[0].FilePath);
        Assert.AreEqual("does-not-exist.rules.json", vm.RuleFiles[0].FileName);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // U-B4: LoadRulesFromQuery does not re-enter SyncRulesToQuery during the load.
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void U_B4_LoadRulesFromQuery_DoesNotSyncBackDuringLoad()
    {
        // Seed query with three valid files.
        var p1 = WriteScratchFile("a.rules.json", @"{ ""sections"": [] }");
        var p2 = WriteScratchFile("b.rules.json", @"{ ""sections"": [] }");
        var p3 = WriteScratchFile("c.rules.json", @"{ ""sections"": [] }");

        var query = new FakeSearchQuery();
        query.RulesConfigPaths.Add(p1);
        query.RulesConfigPaths.Add(p2);
        query.RulesConfigPaths.Add(p3);

        var fake = new FakeQueryState { Query = query };
        var vm = new SearchRulesPageViewModel(fake);

        vm.LoadRulesFromQuery();

        // The guard suppresses SyncRulesToQuery for every LoadRuleFile call made
        // inside LoadRulesFromQuery — so NotifyStateChanged is never invoked, and
        // the query's path list is never mutated mid-load.
        Assert.AreEqual(0, fake.NotifyCount,
            "Re-entrancy guard should suppress all NotifyStateChanged calls during LoadRulesFromQuery.");
        Assert.AreEqual(3, query.RulesConfigPaths.Count, "query path list should be unchanged");
        Assert.AreEqual(p1, query.RulesConfigPaths[0]);
        Assert.AreEqual(p2, query.RulesConfigPaths[1]);
        Assert.AreEqual(p3, query.RulesConfigPaths[2]);

        // And the VM should be in a consistent post-load state.
        Assert.AreEqual(3, vm.RuleFiles.Count);
    }

    [TestMethod]
    public void U_B4_LoadRulesFromQuery_AfterLoad_SubsequentAddDoesSync()
    {
        // After LoadRulesFromQuery finishes, the guard should be cleared so a
        // user-initiated LoadRuleFile DOES sync.
        var seedPath = WriteScratchFile("seed.rules.json", @"{ ""sections"": [] }");
        var query = new FakeSearchQuery();
        query.RulesConfigPaths.Add(seedPath);

        var fake = new FakeQueryState { Query = query };
        var vm = new SearchRulesPageViewModel(fake);
        vm.LoadRulesFromQuery();

        Assert.AreEqual(0, fake.NotifyCount, "no notify during initial load");

        // User picks a new file.
        var newPath = WriteScratchFile("new.rules.json", @"{ ""sections"": [] }");
        vm.LoadRuleFile(newPath);

        Assert.AreEqual(1, fake.NotifyCount, "post-load LoadRuleFile must sync");
        Assert.AreEqual(2, query.RulesConfigPaths.Count);
        Assert.IsTrue(query.RulesConfigPaths.Contains(newPath));
    }

    // ─── Bonus coverage for RemoveFile + SetPurposeFilter (used by event handlers) ──

    [TestMethod]
    public void RemoveFile_RemovesItemAndItsSections_AndResyncs()
    {
        var path = WriteScratchFile("r.rules.json", @"{
            ""sections"": [
                { ""name"": ""S1"", ""purpose"": ""filter"", ""rules"": [] }
            ]
        }");
        var fake = new FakeQueryState { Query = new FakeSearchQuery() };
        var vm = new SearchRulesPageViewModel(fake);
        vm.LoadRuleFile(path);
        Assert.AreEqual(1, vm.RuleFiles.Count);
        Assert.AreEqual(1, vm.RuleSections.Count);

        vm.RemoveFile(vm.RuleFiles[0]);

        Assert.AreEqual(0, vm.RuleFiles.Count);
        Assert.AreEqual(0, vm.RuleSections.Count);
        Assert.AreEqual(0, fake.Query!.RulesConfigPaths.Count, "query should be re-synced (empty)");
        Assert.AreEqual(2, fake.NotifyCount, "one notify for load, one for remove");
    }

    [TestMethod]
    public void SetPurposeFilter_LimitsVisibleSectionsToMatchingPurpose()
    {
        var path = WriteScratchFile("f.rules.json", @"{
            ""sections"": [
                { ""name"": ""SFilter"",     ""purpose"": ""filter"",     ""rules"": [] },
                { ""name"": ""SEnrich"",     ""purpose"": ""enrichment"", ""rules"": [] },
                { ""name"": ""SOutput"",     ""purpose"": ""output"",     ""rules"": [] }
            ]
        }");
        var vm = new SearchRulesPageViewModel(new FakeQueryState());
        vm.LoadRuleFile(path);
        Assert.AreEqual(3, vm.RuleSections.Count, "all 3 visible under default 'All'");

        vm.SetPurposeFilter("filter");
        Assert.AreEqual(1, vm.RuleSections.Count);
        Assert.AreEqual("filter", vm.RuleSections[0].Purpose);

        vm.SetPurposeFilter("enrichment");
        Assert.AreEqual(1, vm.RuleSections.Count);
        Assert.AreEqual("enrichment", vm.RuleSections[0].Purpose);

        vm.SetPurposeFilter("All");
        Assert.AreEqual(3, vm.RuleSections.Count);
    }
}
