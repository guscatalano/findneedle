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
    public void Labels_UseTheAgreedVocabulary()
    {
        // Ids are stable (persisted); labels follow the terminology table.
        Assert.AreEqual("Sources", QuickActionCatalog.Find("locations").Label);
        Assert.AreEqual("Rule files", QuickActionCatalog.Find("rules_config").Label);
        Assert.AreEqual("Auto rules", QuickActionCatalog.Find("auto_rules").Label);
        Assert.AreEqual("Outputs", QuickActionCatalog.Find("processor_output").Label);
        Assert.AreEqual("Diagram tools", QuickActionCatalog.Find("diagram").Label);
        Assert.AreEqual("Inspect ETL", QuickActionCatalog.Find("inspect_etl").Label);
    }

    [TestMethod]
    public void Catalog_NeverRepeatsTheHomeCards()
    {
        // The Tools row is "jump to a tool" only. Everything with a first-class button in the Home cards
        // (open file / folder / with rules, Known logs, Recent searches, Run search, Open results) must
        // NOT be pinnable, or the row repeats the cards right above it.
        foreach (var retired in new[] { "open_file", "open_folder", "open_rules", "log_finder", "cached", "run_search", "results" })
            Assert.IsFalse(QuickActionCatalog.IsValidId(retired), $"'{retired}' is on a Home card; it must not be a tile");
    }

    [TestMethod]
    public void StoredSelection_OfRetiredIdsOnly_FallsBackToDefaults()
    {
        // A profile saved before the cards took over may hold only ids that are no longer tiles.
        File.WriteAllText(_file, "[\"open_file\",\"results\",\"run_search\"]");
        QuickActionCatalog.SetStorageLocationForTests(_file);
        CollectionAssert.AreEqual(QuickActionCatalog.Defaults.ToList(), QuickActionCatalog.GetSelectedIds());
    }

    [TestMethod]
    public void Default_WhenNothingStored_ReturnsDefaults()
    {
        CollectionAssert.AreEqual(QuickActionCatalog.Defaults.ToList(), QuickActionCatalog.GetSelectedIds());
    }

    [TestMethod]
    public void SetSelectedIds_RoundTripsThroughDisk()
    {
        QuickActionCatalog.SetSelectedIds(new[] { "diagram", "inspect_etl" });
        // Re-point at the same file to prove it persisted (not just held in memory).
        QuickActionCatalog.SetStorageLocationForTests(_file);
        CollectionAssert.AreEqual(new[] { "diagram", "inspect_etl" }, QuickActionCatalog.GetSelectedIds());
    }

    [TestMethod]
    public void SetSelectedIds_DropsUnknownAndDuplicateIds()
    {
        QuickActionCatalog.SetSelectedIds(new[] { "inspect_etl", "not_a_real_id", "inspect_etl", "diagram" });
        CollectionAssert.AreEqual(new[] { "inspect_etl", "diagram" }, QuickActionCatalog.GetSelectedIds());
    }

    [TestMethod]
    public void Add_AppendsAndPersists()
    {
        QuickActionCatalog.SetSelectedIds(new[] { "inspect_etl" });
        var after = QuickActionCatalog.Add("diagram");
        CollectionAssert.AreEqual(new[] { "inspect_etl", "diagram" }, after);
        CollectionAssert.AreEqual(new[] { "inspect_etl", "diagram" }, QuickActionCatalog.GetSelectedIds());
    }

    [TestMethod]
    public void Add_IgnoresDuplicateAndUnknown()
    {
        QuickActionCatalog.SetSelectedIds(new[] { "inspect_etl" });
        QuickActionCatalog.Add("inspect_etl");      // already present
        QuickActionCatalog.Add("bogus");          // unknown
        CollectionAssert.AreEqual(new[] { "inspect_etl" }, QuickActionCatalog.GetSelectedIds());
    }

    [TestMethod]
    public void Remove_DropsAndPersists()
    {
        QuickActionCatalog.SetSelectedIds(new[] { "inspect_etl", "auto_rules", "diagram" });
        var after = QuickActionCatalog.Remove("auto_rules");
        CollectionAssert.AreEqual(new[] { "inspect_etl", "diagram" }, after);
        CollectionAssert.AreEqual(new[] { "inspect_etl", "diagram" }, QuickActionCatalog.GetSelectedIds());
    }

    [TestMethod]
    public void Move_ReordersWithinBounds()
    {
        QuickActionCatalog.SetSelectedIds(new[] { "inspect_etl", "auto_rules", "diagram" });
        QuickActionCatalog.Move("diagram", -1);
        CollectionAssert.AreEqual(new[] { "inspect_etl", "diagram", "auto_rules" }, QuickActionCatalog.GetSelectedIds());
    }

    [TestMethod]
    public void Move_AtEdge_IsNoOp()
    {
        QuickActionCatalog.SetSelectedIds(new[] { "inspect_etl", "auto_rules" });
        QuickActionCatalog.Move("inspect_etl", -1);  // already first
        CollectionAssert.AreEqual(new[] { "inspect_etl", "auto_rules" }, QuickActionCatalog.GetSelectedIds());
        QuickActionCatalog.Move("auto_rules", +1); // already last
        CollectionAssert.AreEqual(new[] { "inspect_etl", "auto_rules" }, QuickActionCatalog.GetSelectedIds());
    }

    [TestMethod]
    public void Available_ExcludesSelected()
    {
        QuickActionCatalog.SetSelectedIds(new[] { "inspect_etl" });
        var available = QuickActionCatalog.Available().Select(a => a.Id).ToList();
        Assert.IsFalse(available.Contains("inspect_etl"), "selected action should not be offered again");
        Assert.IsTrue(available.Contains("diagram"), "unselected action should be available");
        Assert.AreEqual(QuickActionCatalog.All.Count - 1, available.Count);
    }

    [TestMethod]
    public void RemovingAll_FallsBackToDefaults()
    {
        QuickActionCatalog.SetSelectedIds(new[] { "inspect_etl" });
        QuickActionCatalog.Remove("inspect_etl");
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
