using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FindNeedleUX.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// The settings file must never be observable in a half-written state. A plain WriteAllText truncates the
/// destination first, so a reader landing in that window parses nothing, hits the "corrupt file => start
/// fresh" fallback, and the user's settings silently reset. Save() writes a temp file and swaps it in
/// instead. This matters more as soon as more than one instance can run.
/// [DoNotParallelize] — mutates the static singleton.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
[DoNotParallelize]
public class SettingsAtomicWriteTests
{
    private string _path;

    [TestInitialize]
    public void Init()
    {
        _path = Path.Combine(Path.GetTempPath(), $"viewer-settings-atomic-{Guid.NewGuid():N}.json");
        ResultsViewerSettings.SetStorageLocationForTests(_path);
    }

    [TestCleanup]
    public void Cleanup()
    {
        ResultsViewerSettings.ResetStorageForTests();
        foreach (var f in new[] { _path, _path + ".tmp" })
            try { File.Delete(f); } catch { /* best-effort */ }
    }

    [TestMethod]
    public void Save_WritesValidJson_AndLeavesNoTempFileBehind()
    {
        ResultsViewerSettings.FilterPaneWidth = 321;   // inside [220, 760]

        Assert.IsTrue(File.Exists(_path), "settings should have been written");
        Assert.IsFalse(File.Exists(_path + ".tmp"),
            "the temp file must be swapped in, not left lying next to the real one");

        // Parses, and actually contains what we set.
        using var doc = JsonDocument.Parse(File.ReadAllText(_path));
        Assert.AreEqual(321d, doc.RootElement.GetProperty("FilterPaneWidth").GetDouble());
    }

    [TestMethod]
    public void Save_OverExistingFile_IsNeverObservedEmpty()
    {
        ResultsViewerSettings.FilterPaneWidth = 240;
        var before = new FileInfo(_path).Length;
        Assert.IsTrue(before > 0);

        // Rewrite repeatedly; after every single save the file must load back with the value just
        // written. With a truncating write the file passes through zero length on each one, and a
        // reader in that window falls into "corrupt => defaults". Read back through the app's own
        // loader (which also covers its retry on a transient sharing violation) rather than racing the
        // file with a raw ReadAllText, which is what made this test flake under an indexer/antivirus.
        for (int i = 300; i < 310; i++)
        {
            ResultsViewerSettings.FilterPaneWidth = i;
            ResultsViewerSettings.ReloadFromDiskForTests();
            Assert.AreEqual((double)i, ResultsViewerSettings.FilterPaneWidth,
                "the value just saved must load back — an empty or half-written file would read as the default");
        }
    }

    [TestMethod]
    public void Save_WhenFileIsMissing_CreatesIt()
    {
        ResultsViewerSettings.FilterPaneWidth = 331;
        File.Delete(_path);

        ResultsViewerSettings.FilterPaneWidth = 332; // must take the create path, not the replace path

        Assert.IsTrue(File.Exists(_path));
        using var doc = JsonDocument.Parse(File.ReadAllText(_path));
        Assert.AreEqual(332d, doc.RootElement.GetProperty("FilterPaneWidth").GetDouble());
    }
}
