using System.Linq;
using FindNeedleUX.Services;
using FindPluginCore.Wpp.Symbols;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.WppSymbols;

/// <summary>
/// Scale checks for the resolution surface: the "1000 WPP providers, 75% missing" scenario that
/// backs the dev simulation. Verifies the synthetic set is classified correctly and that the
/// roll-up summary a user would read reflects the backlog. Pure logic — CI-runnable, no WDK.
/// </summary>
[TestClass]
[TestCategory("WppSymbols")]
public class WppSymbolScaleTests
{
    [TestMethod]
    public void Simulated_1000Providers_75PercentMissing_ClassifiesAndSummarizes()
    {
        var outcomes = WppSymbolResolver.GenerateSimulatedOutcomes(1000, 0.75);

        Assert.AreEqual(1000, outcomes.Count, "exactly 1000 providers");

        int resolved = outcomes.Count(o => o.Status == SymbolStatus.Resolved);
        int problems = outcomes.Count(o => o.IsProblem);
        int missing = outcomes.Count(o => o.Status == SymbolStatus.NotFound);
        int wrong = outcomes.Count(o => o.Status == SymbolStatus.WrongVersion);

        // 75% missing => 25% resolved, and every non-resolved entry is something the user must fix.
        Assert.AreEqual(250, resolved, "25% resolved");
        Assert.AreEqual(750, problems, "75% need attention");
        Assert.AreEqual(750, missing + wrong, "problems split between not-found and wrong-version");
        Assert.IsTrue(missing > 0, "some are simply missing");
        Assert.IsTrue(wrong > 0, "some are the wrong build (present but stale)");

        // Every missing/wrong row names the exact PDB identity the user needs to supply.
        var firstProblem = outcomes.First(o => o.IsProblem);
        StringAssert.Contains(firstProblem.Headline, firstProblem.PdbName, "row names the PDB");
        Assert.AreEqual(32, firstProblem.Guid.Length, "row carries the full PDB GUID");

        // The roll-up the user reads reflects the backlog.
        var summary = new BuildTmfsResult { Outcomes = outcomes }.Summary;
        StringAssert.Contains(summary, "250 resolved", $"summary: {summary}");
        StringAssert.Contains(summary, "wrong version", $"summary: {summary}");
        StringAssert.Contains(summary, "missing", $"summary: {summary}");
    }

    [TestMethod]
    public void Simulated_AllMissing_IsAllProblems()
    {
        var outcomes = WppSymbolResolver.GenerateSimulatedOutcomes(200, 1.0);
        Assert.AreEqual(200, outcomes.Count);
        Assert.IsTrue(outcomes.Count(o => o.IsProblem) >= 199, "≈all missing when fraction is 1.0");
    }
}
