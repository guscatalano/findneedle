using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FindNeedleUX.Services;
using FindNeedlePluginLib;
using FindNeedlePluginLib.Interfaces;
using findneedle.Implementations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Services;

/// <summary>
/// Tests for <see cref="MiddleLayerService.AddFolderLocation"/> — adding a file/folder path as a
/// search location, with case-insensitive de-duplication so repeated adds don't scan the same path
/// twice. Mutates the static Locations list, so it saves/restores it and runs serial.
/// </summary>
[TestClass]
[TestCategory("Services")]
[DoNotParallelize]
public class AddFolderLocationTests
{
    private List<ISearchLocation> _saved;
    private string _recentFile;

    [TestInitialize]
    public void Init()
    {
        _saved = MiddleLayerService.Locations;
        MiddleLayerService.Locations = new List<ISearchLocation>();
        // Redirect the recent-locations store so the test doesn't touch the real %LocalAppData% file.
        _recentFile = Path.Combine(Path.GetTempPath(), $"fn_recent_{Guid.NewGuid():N}.json");
        RecentLocationsStore.SetStorageLocationForTests(_recentFile);
    }

    [TestCleanup]
    public void Cleanup()
    {
        MiddleLayerService.Locations = _saved;
        RecentLocationsStore.ResetStorageForTests();
        try { if (File.Exists(_recentFile)) File.Delete(_recentFile); } catch { }
    }

    [TestMethod]
    public void AddFolderLocation_DeduplicatesCaseInsensitively()
    {
        MiddleLayerService.AddFolderLocation(@"C:\logs\a");
        MiddleLayerService.AddFolderLocation(@"c:\LOGS\A"); // same path, different case → no duplicate
        MiddleLayerService.AddFolderLocation(@"C:\logs\b");

        Assert.AreEqual(2, MiddleLayerService.Locations.Count);
        Assert.IsTrue(MiddleLayerService.Locations.Any(l => l.GetName().Equals(@"C:\logs\a", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(MiddleLayerService.Locations.Any(l => l.GetName().Equals(@"C:\logs\b", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void AddFolderLocation_AddsAsFolderLocation()
    {
        MiddleLayerService.AddFolderLocation(@"C:\logs\only");
        Assert.AreEqual(1, MiddleLayerService.Locations.Count);
        Assert.IsInstanceOfType(MiddleLayerService.Locations[0], typeof(FolderLocation));
    }
}
