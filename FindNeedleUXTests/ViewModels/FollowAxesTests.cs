using System.Linq;
using FindNeedleUX.Pages.NativeResultViewer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Follow ▾ in the in-row detail: which axes are offered for a row, and the query each one runs.
/// An axis the row has no value for must NOT be offered — an item that silently does nothing is worse
/// than no item. Thread is paired with its process because thread ids only mean something within one.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
public class FollowAxesTests
{
    private static string[] Captions(params string[] a)
        => FollowCatalog.AxesFor(a[0], a[1], a[2], a[3]).Select(x => x.Caption).ToArray();

    [TestMethod]
    public void AllFour_WhenTheRowHasEverything()
    {
        var axes = FollowCatalog.AxesFor("A1", "1234", "8", "Kernel-Power");
        CollectionAssert.AreEqual(
            new[] { "Follow this activity", "Follow this thread (1234:8)", "Follow this process (1234)", "Follow this provider (Kernel-Power)" },
            axes.Select(x => x.Caption).ToArray());
    }

    [TestMethod]
    public void NoActivity_NoActivityItem()
    {
        CollectionAssert.DoesNotContain(Captions("", "1234", "8", "P"), "Follow this activity");
        CollectionAssert.DoesNotContain(Captions(null, "1234", "8", "P"), "Follow this activity");
    }

    [TestMethod]
    public void ThreadWithoutProcess_IsNotOffered()
    {
        // A bare thread id is ambiguous across processes; there is nothing honest to follow.
        var c = Captions("", "", "8", "");
        Assert.AreEqual(0, c.Length, string.Join(", ", c));
    }

    [TestMethod]
    public void ProcessWithoutThread_OffersProcessOnly()
    {
        CollectionAssert.AreEqual(new[] { "Follow this process (1234)" }, Captions("", "1234", "", ""));
    }

    [TestMethod]
    public void EmptyRow_OffersNothing()
    {
        Assert.AreEqual(0, Captions("", "", "", "").Length);
    }

    [TestMethod]
    public void Queries_TargetTheRightFields()
    {
        var axes = FollowCatalog.AxesFor("A1", "1234", "8", "Kernel-Power").ToDictionary(x => x.Caption, x => x.Query);
        Assert.AreEqual("activityid == \"A1\" OR relatedactivityid == \"A1\"", axes["Follow this activity"]);
        Assert.AreEqual("processid == \"1234\" AND threadid == \"8\"", axes["Follow this thread (1234:8)"]);
        Assert.AreEqual("processid == \"1234\"", axes["Follow this process (1234)"]);
        Assert.AreEqual("provider == \"Kernel-Power\"", axes["Follow this provider (Kernel-Power)"]);
    }
}
