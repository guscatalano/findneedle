using findneedle;
using FindNeedlePluginLib.TestClasses;

namespace WatsonPluginTests;

[TestClass]
public sealed class Test1
{
   
    [TestMethod]
    public void BasicTest()
    {
        FakeSearchResult result = new();
        result.messageString = "BasicOutputTest";
       
        List<ISearchResult> searchResults = new();
        searchResults.Add(result);
           
       
    }
}
