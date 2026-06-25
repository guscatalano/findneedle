using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using FindNeedleUX.Pages.NativeResultViewer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Tests for <see cref="RangeObservableCollection{T}.ReplaceAll"/> — the bulk page-swap the viewer uses
/// so binding a new page raises a single Reset rather than one event per row.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
public class RangeObservableCollectionTests
{
    [TestMethod]
    public void ReplaceAll_SwapsContents()
    {
        var c = new RangeObservableCollection<int> { 1, 2, 3 };
        c.ReplaceAll(new List<int> { 7, 8 });
        CollectionAssert.AreEqual(new[] { 7, 8 }, c.ToList());
    }

    [TestMethod]
    public void ReplaceAll_Empty_ClearsCollection()
    {
        var c = new RangeObservableCollection<int> { 1, 2, 3 };
        c.ReplaceAll(new List<int>());
        Assert.AreEqual(0, c.Count);
    }

    [TestMethod]
    public void ReplaceAll_RaisesASingleResetNotification()
    {
        var c = new RangeObservableCollection<int> { 1 };
        int events = 0;
        NotifyCollectionChangedAction last = NotifyCollectionChangedAction.Add;
        c.CollectionChanged += (_, e) => { events++; last = e.Action; };

        c.ReplaceAll(new List<int> { 4, 5, 6 });

        Assert.AreEqual(1, events, "a bulk replace should raise exactly one change notification");
        Assert.AreEqual(NotifyCollectionChangedAction.Reset, last);
    }
}
