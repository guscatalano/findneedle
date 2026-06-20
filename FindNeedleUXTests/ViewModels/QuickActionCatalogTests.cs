using System.IO;
using System.Linq;
using FindNeedleUX.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Tests for <see cref="QuickActionCatalog"/> — the customizable welcome-page quick actions. Uses the
/// storage seam to redirect persistence to a temp file (so the dev's real quick-actions.json is never
/// touched). [DoNotParallelize] because it mutates a static redirected path.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
[DoNotParallelize]
public class QuickActionCatalogTests
{
    private string _file = null!;

    [TestInitialize]
    public void Setup()
    {
        _file = Path.Combine(Path.GetTempPath(), $"FN_quickactions_{System.Guid.NewGuid():N}.json");
        QuickActionCatalog.SetStorageLocationForTests(_file);
    }

    [TestCleanup]
    public void Cleanup()
    {
        QuickActionCatalog.ResetStorageForTests();
        try { if (File.Exists(_file)) File.Delete(_file); } catch { }
    }

    [TestMethod]
    public void Default_WhenNothingStored_ReturnsDefaults()
    {
        CollectionAssert.AreEqual(QuickActionCatalog.Defaults.ToList(), QuickActionCatalog.GetSelectedIds());
    }

    [TestMethod]
    public void SetSelectedIds_RoundTripsThroughDisk()
    {
        QuickActionCatalog.SetSelectedIds(new[] { "results", "open_file" });
        // Re-point at the same file to prove it persisted (not just held in memory).
        QuickActionCatalog.SetStorageLocationForTests(_file);
        CollectionAssert.AreEqual(new[] { "results", "open_file" }, QuickActionCatalog.GetSelectedIds());
    }

    [TestMethod]
    public void SetSelectedIds_DropsUnknownAndDuplicateIds()
    {
        QuickActionCatalog.SetSelectedIds(new[] { "open_file", "not_a_real_id", "open_file", "results" });
        CollectionAssert.AreEqual(new[] { "open_file", "results" }, QuickActionCatalog.GetSelectedIds());
    }

    [TestMethod]
    public void Add_AppendsAndPersists()
    {
        QuickActionCatalog.SetSelectedIds(new[] { "open_file" });
        var after = QuickActionCatalog.Add("results");
        CollectionAssert.AreEqual(new[] { "open_file", "results" }, after);
        CollectionAssert.AreEqual(new[] { "open_file", "results" }, QuickActionCatalog.GetSelectedIds());
    }

    [TestMethod]
    public void Add_IgnoresDuplicateAndUnknown()
    {
        QuickActionCatalog.SetSelectedIds(new[] { "open_file" });
        QuickActionCatalog.Add("open_file");      // already present
        QuickActionCatalog.Add("bogus");          // unknown
        CollectionAssert.AreEqual(new[] { "open_file" }, QuickActionCatalog.GetSelectedIds());
    }

    [TestMethod]
    public void Remove_DropsAndPersists()
    {
        QuickActionCatalog.SetSelectedIds(new[] { "open_file", "open_folder", "results" });
        var after = QuickActionCatalog.Remove("open_folder");
        CollectionAssert.AreEqual(new[] { "open_file", "results" }, after);
        CollectionAssert.AreEqual(new[] { "open_file", "results" }, QuickActionCatalog.GetSelectedIds());
    }

    [TestMethod]
    public void Move_ReordersWithinBounds()
    {
        QuickActionCatalog.SetSelectedIds(new[] { "open_file", "open_folder", "results" });
        QuickActionCatalog.Move("results", -1);
        CollectionAssert.AreEqual(new[] { "open_file", "results", "open_folder" }, QuickActionCatalog.GetSelectedIds());
    }

    [TestMethod]
    public void Move_AtEdge_IsNoOp()
    {
        QuickActionCatalog.SetSelectedIds(new[] { "open_file", "open_folder" });
        QuickActionCatalog.Move("open_file", -1);  // already first
        CollectionAssert.AreEqual(new[] { "open_file", "open_folder" }, QuickActionCatalog.GetSelectedIds());
        QuickActionCatalog.Move("open_folder", +1); // already last
        CollectionAssert.AreEqual(new[] { "open_file", "open_folder" }, QuickActionCatalog.GetSelectedIds());
    }

    [TestMethod]
    public void Available_ExcludesSelected()
    {
        QuickActionCatalog.SetSelectedIds(new[] { "open_file" });
        var available = QuickActionCatalog.Available().Select(a => a.Id).ToList();
        Assert.IsFalse(available.Contains("open_file"), "selected action should not be offered again");
        Assert.IsTrue(available.Contains("results"), "unselected action should be available");
        Assert.AreEqual(QuickActionCatalog.All.Count - 1, available.Count);
    }

    [TestMethod]
    public void RemovingAll_FallsBackToDefaults()
    {
        QuickActionCatalog.SetSelectedIds(new[] { "open_file" });
        QuickActionCatalog.Remove("open_file");
        // Empty selection is meaningless on the welcome page → defaults are shown.
        CollectionAssert.AreEqual(QuickActionCatalog.Defaults.ToList(), QuickActionCatalog.GetSelectedIds());
    }

    [TestMethod]
    public void EveryCatalogId_IsValid_AndUnique()
    {
        var ids = QuickActionCatalog.All.Select(a => a.Id).ToList();
        Assert.AreEqual(ids.Count, ids.Distinct().Count(), "catalog ids must be unique");
        Assert.IsTrue(ids.All(QuickActionCatalog.IsValidId));
        Assert.IsTrue(QuickActionCatalog.Defaults.All(QuickActionCatalog.IsValidId), "defaults must be real catalog ids");
    }
}
