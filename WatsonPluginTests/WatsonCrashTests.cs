using findneedle;
using findneedle.Implementations.ResultProcessors;
using FindNeedlePluginLib.TestClasses;

namespace WatsonPluginTests;

[TestClass]
public sealed class WatonCrashTests
{
   
    [TestMethod]
    public void BasicTest()
    {
        FakeSearchResult result = new();
        result.searchableDataString = "oh no A .NET application failed. oh no";
       
        List<ISearchResult> searchResults = new();
        searchResults.Add(result);
        WatsonCrashProcessor processor = new();
        processor.ProcessResults(searchResults);
        Assert.AreEqual(processor.GetOutputText(), "Found 1 crashes or hangs.");

    }
}
