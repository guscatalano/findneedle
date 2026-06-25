using System;
using System.IO;
using FindNeedleUX;
using FindNeedleUX.Pages.NativeResultViewer;
using FindNeedleUX.Services;
using FindNeedleUX.Services.PagedLogSource;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Tests for the result viewer's sort state: <see cref="NativeResultsPageViewModel.ApplySort"/> and
/// <see cref="NativeResultsPageViewModel.ApplyDefaultSortFromSettings"/> (mapping the persisted
/// DefaultSort preference to the initial sort column/direction).
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
[DoNotParallelize] // ApplyDefaultSortFromSettings reads the static ResultsViewerSettings singleton
public class ViewModelSortTests
{
    private static NativeResultsPageViewModel NewVm()
    {
        var vm = new NativeResultsPageViewModel();
        vm.SetSourceForTests(new InMemoryPagedSource(Array.Empty<LogLine>()));
        return vm;
    }

    [TestMethod]
    public void ApplySort_SetsColumnAndDirection()
    {
        var vm = NewVm();
        vm.ApplySort("Time", descending: true);
        Assert.AreEqual("Time", vm.SortColumn);
        Assert.IsTrue(vm.SortDescending);

        vm.ApplySort("Provider", descending: false);
        Assert.AreEqual("Provider", vm.SortColumn);
        Assert.IsFalse(vm.SortDescending);
    }

    [TestMethod]
    public void ApplySort_EmptyColumn_ClearsToLoadOrder()
    {
        var vm = NewVm();
        vm.ApplySort("", descending: false);
        Assert.IsNull(vm.SortColumn, "an empty column means load-order (no sort column)");
    }

    [TestMethod]
    public void ApplyDefaultSortFromSettings_MapsEachMode()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"viewer-settings-sort-{Guid.NewGuid():N}.json");
        try
        {
            ResultsViewerSettings.SetStorageLocationForTests(tmp);
            var vm = NewVm();

            ResultsViewerSettings.DefaultSort = DefaultSortMode.TimeDescending;
            vm.ApplyDefaultSortFromSettings();
            Assert.AreEqual("Time", vm.SortColumn);
            Assert.IsTrue(vm.SortDescending);

            ResultsViewerSettings.DefaultSort = DefaultSortMode.TimeAscending;
            vm.ApplyDefaultSortFromSettings();
            Assert.AreEqual("Time", vm.SortColumn);
            Assert.IsFalse(vm.SortDescending);

            ResultsViewerSettings.DefaultSort = DefaultSortMode.LoadOrder;
            vm.ApplyDefaultSortFromSettings();
            Assert.IsNull(vm.SortColumn, "LoadOrder means no sort column");
            Assert.IsFalse(vm.SortDescending);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            ResultsViewerSettings.ResetStorageForTests();
        }
    }
}
