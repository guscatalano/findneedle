using System;
using System.IO;
using FindNeedleUX.Pages.NativeResultViewer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Tests for the result viewer's "Rule filter" logic (<see cref="NativeResultsPageViewModel"/>):
/// CompileRuleFilters loads exclude/include rules from RuleDSL files, and RulePass applies the
/// blacklist-then-whitelist semantics (with unmatch rescue) against a row's searchable text.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
public class RuleFilterTests
{
    private string _file;

    [TestCleanup]
    public void Cleanup() { try { if (_file != null && File.Exists(_file)) File.Delete(_file); } catch { } }

    private string WriteRules(string rulesJson)
    {
        _file = Path.Combine(Path.GetTempPath(), $"fn_rulefilter_{Guid.NewGuid():N}.rules.json");
        File.WriteAllText(_file,
            "{\"schemaVersion\":\"2.0\",\"sections\":[{\"name\":\"t\",\"purpose\":\"filter\",\"rules\":[" + rulesJson + "]}]}");
        return _file;
    }

    [TestMethod]
    public void ExcludeRule_HidesMatchingRows()
    {
        var vm = new NativeResultsPageViewModel();
        vm.CompileRuleFilters(new[] { WriteRules(
            "{\"name\":\"n\",\"match\":\"noise\",\"enabled\":true,\"action\":{\"type\":\"exclude\"}}") });
        Assert.IsFalse(vm.RulePass("this has noise in it"));
        Assert.IsTrue(vm.RulePass("a clean line"));
    }

    [TestMethod]
    public void IncludeRule_KeepsOnlyMatchingRows()
    {
        var vm = new NativeResultsPageViewModel();
        vm.CompileRuleFilters(new[] { WriteRules(
            "{\"name\":\"k\",\"match\":\"keep\",\"enabled\":true,\"action\":{\"type\":\"include\"}}") });
        Assert.IsTrue(vm.RulePass("please keep this"));
        Assert.IsFalse(vm.RulePass("discard this one"));
    }

    [TestMethod]
    public void ExcludeWithUnmatch_RescuesUnmatchedRows()
    {
        var vm = new NativeResultsPageViewModel();
        vm.CompileRuleFilters(new[] { WriteRules(
            "{\"name\":\"e\",\"match\":\"error\",\"unmatch\":\"expected\",\"enabled\":true,\"action\":{\"type\":\"exclude\"}}") });
        Assert.IsFalse(vm.RulePass("an error occurred"));      // excluded
        Assert.IsTrue(vm.RulePass("error but expected here")); // unmatch rescues it
    }

    [TestMethod]
    public void DisabledRule_IsIgnored()
    {
        var vm = new NativeResultsPageViewModel();
        vm.CompileRuleFilters(new[] { WriteRules(
            "{\"name\":\"n\",\"match\":\"noise\",\"enabled\":false,\"action\":{\"type\":\"exclude\"}}") });
        Assert.IsTrue(vm.RulePass("this has noise"), "a disabled rule must not filter");
    }

    [TestMethod]
    public void NoFilterRules_PassEverything()
    {
        var vm = new NativeResultsPageViewModel();
        vm.CompileRuleFilters(Array.Empty<string>());
        Assert.IsTrue(vm.RulePass("anything at all"));
    }
}
