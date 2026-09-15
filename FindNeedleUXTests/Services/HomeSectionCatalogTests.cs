using System;
using System.IO;
using System.Linq;
using FindNeedleUX.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUX.UnitTests.Services;

/// <summary>Home's section arrangement: show / hide, collapse, order within a column, the fixed Open card.</summary>
[TestClass]
[DoNotParallelize]
public class HomeSectionCatalogTests
{
    private string _file;

    [TestInitialize]
    public void Init()
    {
        _file = Path.Combine(Path.GetTempPath(), $"fn_home_{Guid.NewGuid():N}.json");
        HomeSectionCatalog.SetStorageLocationForTests(_file);
    }

    [TestCleanup]
    public void Cleanup()
    {
        HomeSectionCatalog.ResetStorageForTests();
        try { if (File.Exists(_file)) File.Delete(_file); } catch { }
    }

    private static string[] Ids(HomeColumn c) => HomeSectionCatalog.Order(c).Select(s => s.Id).ToArray();

    [TestMethod]
    public void Defaults_MatchTheShippedLayout()
    {
        CollectionAssert.AreEqual(new[] { "open", "recent", "known" }, Ids(HomeColumn.Left));
        CollectionAssert.AreEqual(new[] { "workspace", "workspaces" }, Ids(HomeColumn.Right));
        CollectionAssert.AreEqual(new[] { "tools" }, Ids(HomeColumn.Bottom));
        foreach (var s in HomeSectionCatalog.All)
        {
            Assert.IsFalse(HomeSectionCatalog.IsHidden(s.Id), $"{s.Id} shown by default");
            Assert.IsFalse(HomeSectionCatalog.IsCollapsed(s.Id), $"{s.Id} expanded by default");
        }
    }

    [TestMethod]
    public void TheOpenCard_IsFixed_CannotBeHiddenCollapsedOrMoved()
    {
        HomeSectionCatalog.SetHidden("open", true);
        HomeSectionCatalog.SetCollapsed("open", true);
        Assert.IsFalse(HomeSectionCatalog.IsHidden("open"));
        Assert.IsFalse(HomeSectionCatalog.IsCollapsed("open"));
        Assert.IsFalse(HomeSectionCatalog.Move("open", +1));
        // Nothing moves above it either.
        Assert.IsFalse(HomeSectionCatalog.Move("recent", -1), "recent is already first among the movable sections");
        Assert.AreEqual("open", Ids(HomeColumn.Left)[0]);
    }

    [TestMethod]
    public void Move_ReordersWithinTheColumn_AndPersists()
    {
        Assert.IsTrue(HomeSectionCatalog.Move("known", -1));
        CollectionAssert.AreEqual(new[] { "open", "known", "recent" }, Ids(HomeColumn.Left));
        Assert.IsFalse(HomeSectionCatalog.Move("known", -1), "already at the top of the movable sections");
        Assert.IsFalse(HomeSectionCatalog.Move("recent", +1), "already last");
        // A right-column section never lands in the left column.
        Assert.IsTrue(HomeSectionCatalog.Move("workspaces", -1));
        CollectionAssert.AreEqual(new[] { "workspaces", "workspace" }, Ids(HomeColumn.Right));
        CollectionAssert.AreEqual(new[] { "open", "known", "recent" }, Ids(HomeColumn.Left), "untouched");

        // Re-point at the same file: the order came from disk.
        HomeSectionCatalog.SetStorageLocationForTests(_file);
        CollectionAssert.AreEqual(new[] { "open", "known", "recent" }, Ids(HomeColumn.Left));
    }

    [TestMethod]
    public void HiddenAndCollapsed_AreIndependent_AndPersist()
    {
        HomeSectionCatalog.SetCollapsed("recent", true);
        HomeSectionCatalog.SetHidden("known", true);
        Assert.IsTrue(HomeSectionCatalog.IsCollapsed("recent"));
        Assert.IsFalse(HomeSectionCatalog.IsHidden("recent"));
        Assert.IsTrue(HomeSectionCatalog.IsHidden("known"));
        Assert.IsFalse(HomeSectionCatalog.IsCollapsed("known"));
        // Hidden sections keep their place in the order (the pencil lists them unchecked).
        CollectionAssert.AreEqual(new[] { "open", "recent", "known" }, Ids(HomeColumn.Left));

        HomeSectionCatalog.SetStorageLocationForTests(_file);
        Assert.IsTrue(HomeSectionCatalog.IsCollapsed("recent"));
        Assert.IsTrue(HomeSectionCatalog.IsHidden("known"));

        HomeSectionCatalog.SetCollapsed("recent", false);
        HomeSectionCatalog.SetHidden("known", false);
        Assert.IsFalse(HomeSectionCatalog.IsCollapsed("recent"));
        Assert.IsFalse(HomeSectionCatalog.IsHidden("known"));
    }

    [TestMethod]
    public void Reset_RestoresTheShippedLayout()
    {
        HomeSectionCatalog.Move("known", -1);
        HomeSectionCatalog.SetHidden("workspaces", true);
        HomeSectionCatalog.SetCollapsed("tools", true);
        HomeSectionCatalog.Reset();
        CollectionAssert.AreEqual(new[] { "open", "recent", "known" }, Ids(HomeColumn.Left));
        Assert.IsFalse(HomeSectionCatalog.IsHidden("workspaces"));
        Assert.IsFalse(HomeSectionCatalog.IsCollapsed("tools"));
    }

    [TestMethod]
    public void UnknownOrCorruptStoredIds_AreIgnored()
    {
        File.WriteAllText(_file, "{\"Hidden\":[\"nope\"],\"Collapsed\":[\"recent\"],\"Order\":{\"Left\":[\"bogus\",\"known\"]}}");
        HomeSectionCatalog.SetStorageLocationForTests(_file);
        CollectionAssert.AreEqual(new[] { "open", "known", "recent" }, Ids(HomeColumn.Left), "known ids honoured, bogus dropped, missing appended");
        Assert.IsTrue(HomeSectionCatalog.IsCollapsed("recent"));
        File.WriteAllText(_file, "not json");
        CollectionAssert.AreEqual(new[] { "open", "recent", "known" }, Ids(HomeColumn.Left), "corrupt file → defaults, no throw");
    }
}
