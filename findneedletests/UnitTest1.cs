using findneedle;
using findneedle.Implementations;
using FindPluginCore.Searching;

namespace findneedletests;

[TestClass]
public class SearchArgsTests
{



    [TestMethod]
    public void TestAddMultiplelFileLog()
    {
        Dictionary<string, string> input = new Dictionary<string, string>
        {
            { "location1", @"path#C:\\windows\\explorer.exe" },
            { "location2", @"path#C:\\windows\\system32" },
            { "location3", @"path#C:\\windows\\system32\\" }
        };

        SearchQuery q = SearchQueryCmdLine.ParseFromCommandLine(input);


        Assert.AreEqual(3, q.GetLocations().Count);

    }
   
}