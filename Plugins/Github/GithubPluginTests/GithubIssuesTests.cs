using System.Linq;
using GithubPlugin.Location;

namespace GithubPluginTests;

[TestClass]
public sealed class GithubIssuesTests
{
    [TestMethod]
    public void ParseRepo_HandlesUrlsAndOwnerRepo()
    {
        Assert.AreEqual(("guscatalano", "findneedle"),
            GithubIssuesLocation.ParseRepo("https://github.com/guscatalano/findneedle/issues/3"));
        Assert.AreEqual(("guscatalano", "findneedle"),
            GithubIssuesLocation.ParseRepo("https://github.com/guscatalano/findneedle"));
        Assert.AreEqual(("guscatalano", "findneedle"),
            GithubIssuesLocation.ParseRepo("guscatalano/findneedle"));
    }

    /// <summary>
    /// Live end-to-end load against a stable public repo: guscatalano/findneedle has issue #3
    /// ("Test issue") plus PRs #1/#2 (the issues API returns PRs too — they must be filtered out).
    /// Network-dependent (anonymous GitHub rate limit), so categorized Network and tolerant of
    /// rate-limit/connectivity failures via Inconclusive.
    /// </summary>
    [TestMethod]
    [TestCategory("Network")]
    public void LoadsRealIssues_FiltersPullRequests()
    {
        var loc = new GithubIssuesLocation("https://github.com/guscatalano/findneedle/issues", state: "all");
        var results = loc.Search();

        if (loc.LastError != null)
            Assert.Inconclusive($"GitHub not reachable / rate-limited: {loc.LastError}");

        // Issue #3 is present...
        var three = results.FirstOrDefault(r => r.GetEventId() == "3");
        Assert.IsNotNull(three, "issue #3 should be returned");
        StringAssert.Contains(three!.GetMessage(), "Test issue");

        // ...and the PRs (#1, #2) are NOT (the issues endpoint returns them but we filter them out).
        Assert.IsFalse(results.Any(r => r.GetEventId() == "1" || r.GetEventId() == "2"),
            "pull requests #1/#2 must be filtered out");
    }
}
