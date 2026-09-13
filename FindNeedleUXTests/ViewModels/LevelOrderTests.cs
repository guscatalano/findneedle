using System.Collections.Generic;
using System.Linq;
using FindNeedleUX.Pages.NativeResultViewer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// The level chips are a SEVERITY filter, so they must read worst-first. They were sorted alphabetically,
/// which put Info above Warning and produced "Catastrophic, Error, Info, Warning". Rank now comes from the
/// Level enum itself (declared worst-first) rather than a hand-kept table — the old table listed "Critical",
/// a name this app never emits, so its most severe level sorted last.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
public class LevelOrderTests
{
    private static List<string> Sorted(params string[] levels)
        => levels.OrderBy(NativeResultsPageViewModel.LevelRank)
                 .ThenBy(s => s, System.StringComparer.OrdinalIgnoreCase)
                 .ToList();

    [TestMethod]
    public void Levels_SortWorstFirst_NotAlphabetically()
    {
        // Deliberately supplied alphabetically — the order that used to survive.
        CollectionAssert.AreEqual(
            new[] { "Catastrophic", "Error", "Warning", "Info", "Verbose" },
            Sorted("Catastrophic", "Error", "Info", "Verbose", "Warning"));
    }

    [TestMethod]
    public void Catastrophic_IsTheMostSevere_NotLast()
    {
        Assert.IsTrue(NativeResultsPageViewModel.LevelRank("Catastrophic")
                    < NativeResultsPageViewModel.LevelRank("Error"),
            "Catastrophic is this app's most severe level; the old table said 'Critical' so it ranked last");
        Assert.AreEqual("Catastrophic", Sorted("Info", "Catastrophic", "Error").First());
    }

    [TestMethod]
    public void Warning_OutranksInfo()
    {
        Assert.IsTrue(NativeResultsPageViewModel.LevelRank("Warning")
                    < NativeResultsPageViewModel.LevelRank("Info"));
    }

    [TestMethod]
    public void UnknownNames_SortLast_AndDoNotPretendToBeCritical()
    {
        Assert.AreEqual(int.MaxValue, NativeResultsPageViewModel.LevelRank("Bananas"));
        CollectionAssert.AreEqual(
            new[] { "Error", "Info", "Bananas" },
            Sorted("Bananas", "Info", "Error"));
    }

    [TestMethod]
    public void Rank_IsCaseInsensitive()
    {
        Assert.AreEqual(NativeResultsPageViewModel.LevelRank("Warning"),
                        NativeResultsPageViewModel.LevelRank("warning"));
    }
}
