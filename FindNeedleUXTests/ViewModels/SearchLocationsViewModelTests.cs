using System.Collections.Generic;
using System.Linq;
using FindNeedleUX.ViewModels;
using FindNeedleUX.ViewObjects;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Unit tests for <see cref="SearchLocationsViewModel"/>. Covers TESTING_PLAN.md U-B5:
/// Add / Remove persist through the location-state service and the VM's bound
/// collection stays in sync. View-model has no WinUI dependencies (the file/folder
/// picker stays in the page) so these run without a window or dispatcher.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
public class SearchLocationsViewModelTests
{
    /// <summary>
    /// Test double standing in for MiddleLayerService's static location surface.
    /// Keeps an in-memory list and counts calls so tests can assert persistence
    /// without touching statics or the plugin manager.
    /// </summary>
    private sealed class FakeLocationState : ILocationStateService
    {
        public List<LocationListItem> Items { get; } = new();
        public int AddCount { get; private set; }
        public int RemoveCount { get; private set; }

        public void AddFolderLocation(string path)
        {
            AddCount++;
            Items.Add(new LocationListItem { Name = path, Description = path });
        }

        public void RemoveLocationByName(string name)
        {
            RemoveCount++;
            var found = Items.FirstOrDefault(i => i.Name == name);
            if (found != null)
                Items.Remove(found);
        }

        public IReadOnlyList<LocationListItem> GetLocationListItems() => Items;
    }

    [TestMethod]
    public void Ctor_SeedsLocationsFromExistingState()
    {
        var fake = new FakeLocationState();
        fake.AddFolderLocation(@"C:\pre-existing");

        var vm = new SearchLocationsViewModel(fake);

        Assert.AreEqual(1, vm.Locations.Count, "constructor should refresh from current state");
        Assert.AreEqual(@"C:\pre-existing", vm.Locations[0].Name);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // U-B5: Add persists to the state service and shows up in the bound list.
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void AddLocation_PersistsToStateService_AndRefreshesList()
    {
        var fake = new FakeLocationState();
        var vm = new SearchLocationsViewModel(fake);

        vm.AddLocation(@"C:\Logs");

        Assert.AreEqual(1, fake.AddCount, "add should be delegated to the state service");
        Assert.AreEqual(1, fake.Items.Count, "state service should hold the new location");
        Assert.AreEqual(1, vm.Locations.Count, "bound collection should reflect the add");
        Assert.AreEqual(@"C:\Logs", vm.Locations[0].Name);
    }

    [TestMethod]
    public void AddLocation_Multiple_AllAppearInOrder()
    {
        var fake = new FakeLocationState();
        var vm = new SearchLocationsViewModel(fake);

        vm.AddLocation(@"C:\A");
        vm.AddLocation(@"C:\B");

        Assert.AreEqual(2, vm.Locations.Count);
        CollectionAssert.AreEqual(
            new[] { @"C:\A", @"C:\B" },
            vm.Locations.Select(l => l.Name).ToArray());
    }

    [TestMethod]
    public void AddLocation_RefreshesInPlace_SameCollectionInstance()
    {
        var fake = new FakeLocationState();
        var vm = new SearchLocationsViewModel(fake);
        var instance = vm.Locations;

        vm.AddLocation(@"C:\Logs");

        // The page binds ItemsSource to this instance once — it must not be swapped out.
        Assert.AreSame(instance, vm.Locations);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // U-B5: Remove persists to the state service and drops from the bound list.
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void RemoveLocation_PersistsToStateService_AndRefreshesList()
    {
        var fake = new FakeLocationState();
        var vm = new SearchLocationsViewModel(fake);
        vm.AddLocation(@"C:\A");
        vm.AddLocation(@"C:\B");

        vm.RemoveLocation(@"C:\A");

        Assert.AreEqual(1, fake.RemoveCount);
        Assert.AreEqual(1, vm.Locations.Count, "removed location should be gone from the bound list");
        Assert.AreEqual(@"C:\B", vm.Locations[0].Name);
    }

    [TestMethod]
    public void RemoveLocation_UnknownName_IsNoOp()
    {
        var fake = new FakeLocationState();
        var vm = new SearchLocationsViewModel(fake);
        vm.AddLocation(@"C:\A");

        vm.RemoveLocation(@"C:\does-not-exist");

        Assert.AreEqual(1, vm.Locations.Count, "removing an unknown name leaves the list intact");
        Assert.AreEqual(@"C:\A", vm.Locations[0].Name);
    }
}
