using System;
using System.IO;
using System.Linq;
using FindNeedleUX.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUX.UnitTests.Services;

/// <summary>
/// What opening a second log does to the workspace, at the service level: the decision
/// (<see cref="WorkspaceOpenPolicy"/>) applied by <see cref="MiddleLayerService.OpenIntoWorkspace"/>.
/// Add keeps what is loaded (sources AND rule files) and appends; Replace starts over; the same path
/// opened twice is one source; rule files never duplicate. The window-level flow (the Ask dialog, the
/// second-instance hand-off) is covered by the FlaUI <c>SecondOpenUITests</c>.
/// </summary>
[TestClass]
[DoNotParallelize]
public class OpenIntoWorkspaceTests
{
    private string _dir, _a, _b, _rulesA, _rulesB;

    [TestInitialize]
    public void Init()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fn_open_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _a = Path.Combine(_dir, "a.log"); File.WriteAllText(_a, "[2026-01-01 00:00:00] INFO a\n");
        _b = Path.Combine(_dir, "b.log"); File.WriteAllText(_b, "[2026-01-01 00:00:01] INFO b\n");
        _rulesA = Path.Combine(_dir, "a.rules.json"); File.WriteAllText(_rulesA, "{\"sections\":[]}");
        _rulesB = Path.Combine(_dir, "b.rules.json"); File.WriteAllText(_rulesB, "{\"sections\":[]}");
        MiddleLayerService.NewWorkspace();
    }

    [TestCleanup]
    public void Cleanup()
    {
        MiddleLayerService.NewWorkspace();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static string[] Sources() => MiddleLayerService.Locations.Select(l => l.GetName()).ToArray();

    [TestMethod]
    public void SecondOpen_Add_KeepsTheFirstSource_AndItsRules()
    {
        MiddleLayerService.OpenIntoWorkspace(OpenIntoWorkspaceMode.Add, new[] { _a }, new[] { _rulesA });
        MiddleLayerService.OpenIntoWorkspace(OpenIntoWorkspaceMode.Add, new[] { _b });

        CollectionAssert.AreEquivalent(new[] { _a, _b }, Sources());
        CollectionAssert.AreEqual(new[] { _rulesA }, MiddleLayerService.WorkspaceRulePaths.ToArray(), "the first open's rule file survives the second open");
        Assert.IsFalse(MiddleLayerService.IsWorkspaceEmpty);
    }

    [TestMethod]
    public void SecondOpen_Replace_StartsOver_SourcesAndRules()
    {
        MiddleLayerService.OpenIntoWorkspace(OpenIntoWorkspaceMode.Add, new[] { _a }, new[] { _rulesA });
        MiddleLayerService.OpenIntoWorkspace(OpenIntoWorkspaceMode.Replace, new[] { _b }, new[] { _rulesB });

        CollectionAssert.AreEqual(new[] { _b }, Sources());
        CollectionAssert.AreEqual(new[] { _rulesB }, MiddleLayerService.WorkspaceRulePaths.ToArray(), "Replace drops the old rule file too");
    }

    [TestMethod]
    public void SameFileOpenedTwice_IsOneSource()
    {
        MiddleLayerService.OpenIntoWorkspace(OpenIntoWorkspaceMode.Add, new[] { _a });
        MiddleLayerService.OpenIntoWorkspace(OpenIntoWorkspaceMode.Add, new[] { _a.ToUpperInvariant() });
        Assert.AreEqual(1, MiddleLayerService.Locations.Count, "the same path (any case) is not loaded twice");
    }

    [TestMethod]
    public void SameRuleFileTwice_IsListedOnce()
    {
        MiddleLayerService.OpenIntoWorkspace(OpenIntoWorkspaceMode.Add, new[] { _a }, new[] { _rulesA });
        MiddleLayerService.OpenIntoWorkspace(OpenIntoWorkspaceMode.Add, new[] { _b }, new[] { _rulesA });
        Assert.AreEqual(1, MiddleLayerService.WorkspaceRulePaths.Count);
    }

    [TestMethod]
    public void OpeningIntoAnEmptyWorkspace_NeverAsks_WhateverTheSetting()
    {
        // The policy: an empty workspace has nothing to discard, so even "Ask" just adds.
        Assert.AreEqual(OpenIntoWorkspaceMode.Add, WorkspaceOpenPolicy.Decide(workspaceEmpty: true, OpenIntoWorkspaceMode.Ask));
        Assert.AreEqual(OpenIntoWorkspaceMode.Add, WorkspaceOpenPolicy.Decide(workspaceEmpty: true, OpenIntoWorkspaceMode.Replace));
        // With something loaded the setting decides.
        Assert.AreEqual(OpenIntoWorkspaceMode.Ask, WorkspaceOpenPolicy.Decide(workspaceEmpty: false, OpenIntoWorkspaceMode.Ask));
        Assert.AreEqual(OpenIntoWorkspaceMode.Replace, WorkspaceOpenPolicy.Decide(workspaceEmpty: false, OpenIntoWorkspaceMode.Replace));
        Assert.AreEqual(OpenIntoWorkspaceMode.Add, WorkspaceOpenPolicy.Decide(workspaceEmpty: false, OpenIntoWorkspaceMode.Add));
    }

    [TestMethod]
    public void TheDefaultIsAdd_SoASecondEvtxJoinsTheFirst()
    {
        Assert.AreEqual(OpenIntoWorkspaceMode.Add, ResultsViewerSettings.DefaultOpenIntoWorkspace,
            "opening a second log must not silently discard the first; Add is the default, Replace/Ask are opt-in");
    }
}
