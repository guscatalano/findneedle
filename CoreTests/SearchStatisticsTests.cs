using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using findneedle;
using FindNeedlePluginLib;
using FindNeedlePluginLib.TestClasses;

namespace CoreTests;

[TestClass]
public class SearchStatisticsTests
{

    [TestMethod]
    public void BasicFailStatistics()
    {
        try
        {
            SearchStatistics stats = new();
            stats.ReportToConsole();
            Assert.Fail("Should have thrown because not everything was called");
        }
        catch
        {
            Assert.IsTrue(true); //Should throw, we didnt call every step
        }
    }

    [TestMethod]
    public void BasicStatistics()
    {
        try
        {
            SearchStatistics stats = new();
            stats.LoadedAll(new FakeSearchQuery());
            stats.Searched(new FakeSearchQuery());
            Assert.IsTrue(stats.GetSummaryReport().Contains("Total records"));
        }
        catch
        {
            Assert.Fail("Should not throw");
        }
    }

    [TestMethod]
    public void TestStatistics()
    {
        SearchStatistics stats = new();
        Thread.Sleep(100);
        stats.LoadedAll(new FakeSearchQuery());
        Thread.Sleep(50);
        stats.Searched(new FakeSearchQuery());
        
        var s = stats.GetTimeTaken(SearchStep.AtLoad);
        var s2 = stats.GetTimeTaken(SearchStep.AtSearch);
        Assert.IsTrue(s.TotalMilliseconds > 90); //we slept for 100ms
        Assert.IsTrue(s2.TotalMilliseconds > 40 && s2.TotalMilliseconds < 100); //we slept for 50ms
        Assert.IsTrue(stats.GetMemoryUsage(SearchStep.AtLoad).Contains("PrivateMemory"));

        Assert.AreEqual(stats.GetRecordsAtStep(SearchStep.AtLoad), 0);
    }

    [TestMethod]
    public void TestComponentReports()
    {
        SearchStatistics stats = new();
        stats.ReportFromComponent(new ReportFromComponent()
        {
            component = "Test",
            step = SearchStep.AtLoad,
            summary = "Test",
            metric = new Dictionary<string, dynamic>()
            {
                {"Test", 2}
            }
        });
        Assert.IsTrue(stats.componentReports[SearchStep.AtLoad].First().metric.ContainsKey("Test"));
        Assert.AreEqual(stats.componentReports[SearchStep.AtLoad].First().metric["Test"], 2);
    }
}
