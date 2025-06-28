using findneedle;
using findneedle.Implementations;
using FindNeedlePluginLib.TestClasses;
using FindNeedlePluginLib;

namespace BasicOutputsTests;

[TestClass]
public sealed class TestSimpleOutputToFile
{
    [TestMethod]
    public void BasicTest()
    {
        FakeSearchResult result = new();
        result.messageString = "BasicOutputTest";
        var file = "";
        using (OutputToPlainFile output = new OutputToPlainFile())
        {
            List<ISearchResult> searchResults = new();
            searchResults.Add(result);
            output.WriteAllOutput(searchResults);
            file = output.GetOutputFileName();
        }
        Assert.IsTrue(File.Exists(file));
        Assert.IsTrue(File.ReadAllText(file).Contains("BasicOutputTest"));
    }

    [TestMethod]
    public void BasicTestMultiResult()
    {
        FakeSearchResult result = new();
        result.messageString = "One";
        FakeSearchResult result2 = new();
        result2.messageString = "Two";
        var file = "";
        using (OutputToPlainFile output = new OutputToPlainFile())
        {
            List<ISearchResult> searchResults = new();
            searchResults.Add(result);
            searchResults.Add(result2);
            output.WriteAllOutput(searchResults);
            file = output.GetOutputFileName();
        }
        Assert.IsTrue(File.Exists(file));
        Assert.IsTrue(File.ReadAllText(file).Contains("One"));
        Assert.IsTrue(File.ReadAllText(file).Contains("Two"));
    }
}
