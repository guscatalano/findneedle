using System.IO;
using System.Linq;
using FindNeedleUX.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Tests for <see cref="RecentLocationsStore"/> — the recently-used location paths. Uses the storage
/// seam to redirect to a temp file. [DoNotParallelize] because it mutates a static redirected path.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
[DoNotParallelize]
public class RecentLocationsStoreTests
{
    private string _file = null!;

    [TestInitialize]
    public void Setup()
    {
        _file = Path.Combine(Path.GetTempPath(), $"FN_recent_{System.Guid.NewGuid():N}.json");
        RecentLocationsStore.SetStorageLocationForTests(_file);
    }

    [TestCleanup]
    public void Cleanup()
    {
        RecentLocationsStore.ResetStorageForTests();
        try { if (File.Exists(_file)) File.Delete(_file); } catch { }
    }

    [TestMethod]
    public void Empty_ByDefault()
    {
        Assert.AreEqual(0, RecentLocationsStore.Get().Count);
        Assert.AreEqual(RecentLocationsStore.DefaultMax, RecentLocationsStore.MaxRecent);
    }

    [TestMethod]
    public void Record_AddsMostRecentFirst()
    {
        RecentLocationsStore.Record(@"C:\a");
        RecentLocationsStore.Record(@"C:\b");
        CollectionAssert.AreEqual(new[] { @"C:\b", @"C:\a" }, RecentLocationsStore.Get());
    }

    [TestMethod]
    public void Record_MovesExistingToFront_NoDuplicate()
    {
        RecentLocationsStore.Record(@"C:\a");
        RecentLocationsStore.Record(@"C:\b");
        RecentLocationsStore.Record(@"C:\a"); // re-use a
        CollectionAssert.AreEqual(new[] { @"C:\a", @"C:\b" }, RecentLocationsStore.Get());
    }

    [TestMethod]
    public void Record_IsCaseInsensitiveForDedup()
    {
        RecentLocationsStore.Record(@"C:\Logs");
        RecentLocationsStore.Record(@"c:\logs");
        Assert.AreEqual(1, RecentLocationsStore.Get().Count);
    }

    [TestMethod]
    public void Record_TrimsToCap()
    {
        RecentLocationsStore.MaxRecent = 3;
        for (int i = 0; i < 6; i++) RecentLocationsStore.Record($@"C:\p{i}");
        var got = RecentLocationsStore.Get();
        Assert.AreEqual(3, got.Count);
        CollectionAssert.AreEqual(new[] { @"C:\p5", @"C:\p4", @"C:\p3" }, got);
    }

    [TestMethod]
    public void LoweringMax_TrimsExisting()
    {
        for (int i = 0; i < 5; i++) RecentLocationsStore.Record($@"C:\p{i}");
        RecentLocationsStore.MaxRecent = 2;
        Assert.AreEqual(2, RecentLocationsStore.Get().Count);
    }

    [TestMethod]
    public void Max_IsClamped()
    {
        RecentLocationsStore.MaxRecent = 9999;
        Assert.IsTrue(RecentLocationsStore.MaxRecent <= 50);
        RecentLocationsStore.MaxRecent = 0;
        Assert.IsTrue(RecentLocationsStore.MaxRecent >= 1);
    }

    [TestMethod]
    public void Remove_And_Clear()
    {
        RecentLocationsStore.Record(@"C:\a");
        RecentLocationsStore.Record(@"C:\b");
        RecentLocationsStore.Remove(@"C:\a");
        CollectionAssert.AreEqual(new[] { @"C:\b" }, RecentLocationsStore.Get());
        RecentLocationsStore.Clear();
        Assert.AreEqual(0, RecentLocationsStore.Get().Count);
    }

    [TestMethod]
    public void Persists_AcrossReload()
    {
        RecentLocationsStore.MaxRecent = 7;
        RecentLocationsStore.Record(@"C:\persisted");
        RecentLocationsStore.SetStorageLocationForTests(_file); // re-point at same file
        Assert.AreEqual(7, RecentLocationsStore.MaxRecent);
        CollectionAssert.AreEqual(new[] { @"C:\persisted" }, RecentLocationsStore.Get());
    }

    [TestMethod]
    public void Record_IgnoresBlank()
    {
        RecentLocationsStore.Record("");
        RecentLocationsStore.Record("   ");
        RecentLocationsStore.Record(null);
        Assert.AreEqual(0, RecentLocationsStore.Get().Count);
    }
}
