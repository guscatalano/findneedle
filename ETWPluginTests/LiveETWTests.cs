using ETWPlugin.Locations;
using findneedle;

namespace ETWPluginTests;

[TestClass]
public class LiveETWTests
{
    [TestMethod]
    public void TestStartAndAutoTimeStop()
    {
        
        LiveCollector collector = new();
        collector.Setup(new List<string> { "D451642C-63A6-11D7-9720-00B0D03E0347",
            "DBE9B383-7CF3-4331-91CC-A3CB16A3B538", "E5D3F7AD-54A2-404D-B50B-BF41C40D6CAB", "4846D1C4-912A-4306-B173-24970E40621B" }, TimeSpan.FromSeconds(2), 10);
        collector.StartCollecting();
        Thread.Sleep(TimeSpan.FromSeconds(5)); //Should auto stop regardless
        Assert.IsFalse(collector.IsCollecting());
        List<ISearchResult> results = collector.GetResultsInMemory();

        Assert.IsTrue(results.Count >= 2);
    }
}
