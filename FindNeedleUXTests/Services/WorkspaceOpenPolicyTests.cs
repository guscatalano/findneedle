using System;
using System.IO;
using FindNeedleUX.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Services;

/// <summary>
/// The one rule behind every open path (Open log file / folder / with rules, Recent searches, Known logs,
/// drag-and-drop): a loaded workspace is never silently discarded. <see cref="WorkspaceOpenPolicy.Decide"/>
/// is pure; the setting behind it (<see cref="ResultsViewerSettings.OpenIntoWorkspace"/>) defaults to Add,
/// round-trips through the settings file, and honours the pre-redesign drag-and-drop value.
/// [DoNotParallelize] — the settings tests mutate the static singleton.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
[DoNotParallelize]
public class WorkspaceOpenPolicyTests
{
    private string _settingsPath;

    [TestInitialize]
    public void Init()
    {
        _settingsPath = Path.Combine(Path.GetTempPath(), $"viewer-settings-openpolicy-{Guid.NewGuid():N}.json");
        ResultsViewerSettings.SetStorageLocationForTests(_settingsPath);
        ResultsViewerSettings.ReloadFromDiskForTests();
    }

    [TestCleanup]
    public void Cleanup()
    {
        ResultsViewerSettings.ResetStorageForTests();
        try { File.Delete(_settingsPath); } catch { }
    }

    // ----- Decide (pure) -----

    [TestMethod]
    public void EmptyWorkspace_AlwaysAdds_NeverPrompts()
    {
        foreach (OpenIntoWorkspaceMode setting in Enum.GetValues(typeof(OpenIntoWorkspaceMode)))
            Assert.AreEqual(OpenIntoWorkspaceMode.Add, WorkspaceOpenPolicy.Decide(workspaceEmpty: true, setting),
                $"an empty workspace must just add (setting was {setting})");
    }

    [TestMethod]
    public void LoadedWorkspace_Add_Appends()
        => Assert.AreEqual(OpenIntoWorkspaceMode.Add, WorkspaceOpenPolicy.Decide(false, OpenIntoWorkspaceMode.Add));

    [TestMethod]
    public void LoadedWorkspace_Replace_ClearsThenAdds()
        => Assert.AreEqual(OpenIntoWorkspaceMode.Replace, WorkspaceOpenPolicy.Decide(false, OpenIntoWorkspaceMode.Replace));

    [TestMethod]
    public void LoadedWorkspace_Ask_Asks()
        => Assert.AreEqual(OpenIntoWorkspaceMode.Ask, WorkspaceOpenPolicy.Decide(false, OpenIntoWorkspaceMode.Ask));

    [TestMethod]
    public void Decide_NeverReturnsAsk_ForAnEmptyWorkspace()
        => Assert.AreNotEqual(OpenIntoWorkspaceMode.Ask, WorkspaceOpenPolicy.Decide(true, OpenIntoWorkspaceMode.Ask));

    // ----- the setting -----

    [TestMethod]
    public void Setting_DefaultIsAdd()
    {
        Assert.AreEqual(OpenIntoWorkspaceMode.Add, ResultsViewerSettings.DefaultOpenIntoWorkspace);
        Assert.AreEqual(OpenIntoWorkspaceMode.Add, ResultsViewerSettings.OpenIntoWorkspace, "fresh install → Add");
        Assert.IsFalse(File.Exists(_settingsPath), "reading the default must not write the file");
    }

    [TestMethod]
    public void Setting_RoundTripsEveryValue()
    {
        foreach (OpenIntoWorkspaceMode mode in Enum.GetValues(typeof(OpenIntoWorkspaceMode)))
        {
            ResultsViewerSettings.OpenIntoWorkspace = mode;
            ResultsViewerSettings.ReloadFromDiskForTests();
            Assert.AreEqual(mode, ResultsViewerSettings.OpenIntoWorkspace, $"{mode} did not survive a reload");
        }
    }

    [TestMethod]
    public void Setting_BroadcastsChanged_SoHomeAndSettingsStayInSync()
    {
        int fired = 0;
        Action handler = () => fired++;
        ResultsViewerSettings.Changed += handler;
        try
        {
            ResultsViewerSettings.OpenIntoWorkspace = OpenIntoWorkspaceMode.Ask;
            Assert.AreEqual(1, fired);
        }
        finally { ResultsViewerSettings.Changed -= handler; }
    }

    [TestMethod]
    public void Setting_HonoursLegacyDragDropValue_UntilANewChoiceIsMade()
    {
        // A pre-redesign settings file that only has the old drag-and-drop-only key.
        File.WriteAllText(_settingsPath, "{ \"DragDropMode\": \"ClearAndAdd\" }");
        ResultsViewerSettings.ReloadFromDiskForTests();
        Assert.AreEqual(OpenIntoWorkspaceMode.Replace, ResultsViewerSettings.OpenIntoWorkspace, "ClearAndAdd → Replace");

        File.WriteAllText(_settingsPath, "{ \"DragDropMode\": \"Prompt\" }");
        ResultsViewerSettings.ReloadFromDiskForTests();
        Assert.AreEqual(OpenIntoWorkspaceMode.Add, ResultsViewerSettings.OpenIntoWorkspace,
            "legacy Prompt was the old DEFAULT, not a choice — it must fall through to the new default (Add), not migrate to Ask");

        File.WriteAllText(_settingsPath, "{ \"DragDropMode\": \"AddToExisting\" }");
        ResultsViewerSettings.ReloadFromDiskForTests();
        Assert.AreEqual(OpenIntoWorkspaceMode.Add, ResultsViewerSettings.OpenIntoWorkspace, "AddToExisting → Add");

        // The new key wins once set.
        ResultsViewerSettings.OpenIntoWorkspace = OpenIntoWorkspaceMode.Replace;
        ResultsViewerSettings.ReloadFromDiskForTests();
        Assert.AreEqual(OpenIntoWorkspaceMode.Replace, ResultsViewerSettings.OpenIntoWorkspace);
    }

    [TestMethod]
    public void Setting_UnknownPersistedValue_FallsBackToDefault()
    {
        File.WriteAllText(_settingsPath, "{ \"OpenIntoWorkspace\": \"Banana\", \"DragDropMode\": \"Nope\" }");
        ResultsViewerSettings.ReloadFromDiskForTests();
        Assert.AreEqual(OpenIntoWorkspaceMode.Add, ResultsViewerSettings.OpenIntoWorkspace);
    }

    [TestMethod]
    public void Parse_IsCaseInsensitive_AndRejectsGarbage()
    {
        Assert.AreEqual(OpenIntoWorkspaceMode.Ask, WorkspaceOpenPolicy.Parse("ask"));
        Assert.AreEqual(OpenIntoWorkspaceMode.Replace, WorkspaceOpenPolicy.Parse(" REPLACE "));
        Assert.IsNull(WorkspaceOpenPolicy.Parse(null));
        Assert.IsNull(WorkspaceOpenPolicy.Parse(""));
        Assert.IsNull(WorkspaceOpenPolicy.Parse("Prompt"), "the legacy names are not new-mode names");
        Assert.IsNull(WorkspaceOpenPolicy.FromLegacyDragDropValue("Add"), "and vice versa");
    }
}
