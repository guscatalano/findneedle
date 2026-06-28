using System.Linq;
using FindNeedlePluginLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

[TestClass]
public class FlowProgressTests
{
    [TestCleanup]
    public void Cleanup() => FlowProgress.Complete(); // reset the global state between tests

    [TestMethod]
    public void Detail_ClampsPercent_To0_100()
    {
        FlowProgress.StartPlan(new[] { FlowPhase.ReadParse });
        FlowProgress.Begin(FlowPhase.ReadParse);

        // Overshooting estimate (the ETL line-count "300%" bug) must clamp to 100, not blow past it.
        FlowProgress.Detail("400,000 lines parsed", 300, estimate: true);
        Assert.AreEqual(100, FlowProgress.Steps().Single(s => s.Current).Percent, "percent > 100 must clamp to 100");

        FlowProgress.Detail("x", -5);
        Assert.AreEqual(0, FlowProgress.Steps().Single(s => s.Current).Percent, "negative percent must clamp to 0");

        FlowProgress.Detail("y", 42);
        Assert.AreEqual(42, FlowProgress.Steps().Single(s => s.Current).Percent, "in-range percent passes through");
    }
}
