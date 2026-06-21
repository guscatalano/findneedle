using System.IO;
using System.Linq;
using FindNeedleUX.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Tests for <see cref="StatusBarCatalog"/> — the customizable status-bar items. Uses the storage seam
/// to redirect to a temp file. [DoNotParallelize] because it mutates a static redirected path.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
[DoNotParallelize]
public class StatusBarCatalogTests
{
    private string _file = null!;

    [TestInitialize]
    public void Setup()
    {
        _file = Path.Combine(Path.GetTempPath(), $"FN_statusbar_{System.Guid.NewGuid():N}.json");
        StatusBarCatalog.SetStorageLocationForTests(_file);
    }

    [TestCleanup]
    public void Cleanup()
    {
        StatusBarCatalog.ResetStorageForTests();
        try { if (File.Exists(_file)) File.Delete(_file); } catch { }
    }

    [TestMethod]
    public void Default_WhenNothingStored_ReturnsDefaults()
    {
        CollectionAssert.AreEqual(StatusBarCatalog.Defaults.ToList(), StatusBarCatalog.GetSelectedIds());
    }

    [TestMethod]
    public void Defaults_IncludeRunView_AndAreValid()
    {
        Assert.IsTrue(StatusBarCatalog.Defaults.Contains("run_view"), "Run → View Results is a default item");
        Assert.IsTrue(StatusBarCatalog.Defaults.All(StatusBarCatalog.IsValidId));
    }

    [TestMethod]
    public void Toggle_RemovesThenAdds_AndPersists()
    {
        StatusBarCatalog.SetSelectedIds(new[] { "locations", "rules" });
        StatusBarCatalog.Toggle("rules");                 // remove
        CollectionAssert.AreEqual(new[] { "locations" }, StatusBarCatalog.GetSelectedIds());
        StatusBarCatalog.Toggle("run_view");              // add
        CollectionAssert.AreEqual(new[] { "locations", "run_view" }, StatusBarCatalog.GetSelectedIds());

        StatusBarCatalog.SetStorageLocationForTests(_file); // prove persistence
        CollectionAssert.AreEqual(new[] { "locations", "run_view" }, StatusBarCatalog.GetSelectedIds());
    }

    [TestMethod]
    public void Toggle_IgnoresUnknownId()
    {
        StatusBarCatalog.SetSelectedIds(new[] { "locations" });
        StatusBarCatalog.Toggle("bogus");
        CollectionAssert.AreEqual(new[] { "locations" }, StatusBarCatalog.GetSelectedIds());
    }

    [TestMethod]
    public void Move_Reorders()
    {
        StatusBarCatalog.SetSelectedIds(new[] { "locations", "rules", "lastrun" });
        StatusBarCatalog.Move("lastrun", -1);
        CollectionAssert.AreEqual(new[] { "locations", "lastrun", "rules" }, StatusBarCatalog.GetSelectedIds());
    }

    [TestMethod]
    public void Move_AtEdge_IsNoOp()
    {
        StatusBarCatalog.SetSelectedIds(new[] { "locations", "rules" });
        StatusBarCatalog.Move("locations", -1);
        CollectionAssert.AreEqual(new[] { "locations", "rules" }, StatusBarCatalog.GetSelectedIds());
    }

    [TestMethod]
    public void SetSelectedIds_DropsUnknownAndDuplicates()
    {
        StatusBarCatalog.SetSelectedIds(new[] { "locations", "nope", "locations", "rules" });
        CollectionAssert.AreEqual(new[] { "locations", "rules" }, StatusBarCatalog.GetSelectedIds());
    }

    [TestMethod]
    public void IsSelected_Reflects()
    {
        StatusBarCatalog.SetSelectedIds(new[] { "run_view" });
        Assert.IsTrue(StatusBarCatalog.IsSelected("run_view"));
        Assert.IsFalse(StatusBarCatalog.IsSelected("rules"));
    }

    [TestMethod]
    public void EmptySelection_FallsBackToDefaults()
    {
        StatusBarCatalog.SetSelectedIds(new[] { "locations" });
        StatusBarCatalog.Toggle("locations"); // now empty
        CollectionAssert.AreEqual(StatusBarCatalog.Defaults.ToList(), StatusBarCatalog.GetSelectedIds());
    }

    [TestMethod]
    public void CatalogIds_UniqueAndValid()
    {
        var ids = StatusBarCatalog.All.Select(a => a.Id).ToList();
        Assert.AreEqual(ids.Count, ids.Distinct().Count());
        Assert.IsTrue(StatusBarCatalog.Defaults.All(StatusBarCatalog.IsValidId));
    }
}
