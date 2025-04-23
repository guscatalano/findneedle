using findneedle;
using findneedle.Implementations;
using FindNeedlePluginLib.Interfaces;
using FindNeedlePluginLib.TestClasses;

namespace CoreTests;
[TestClass]
public class SearchQuerySerializerTest
{

    [TestMethod]
    public void BasicSave()
    {
        SearchQuery q = new()
        {
            Name = "test"
        };
        SerializableSearchQuery r = SearchQueryJsonReader.GetSerializableSearchQuery(q);
        var json = r.GetQueryJson();
        SerializableSearchQuery q2 = SearchQueryJsonReader.LoadSearchQuery(json);
        Assert.AreEqual(r.Name, q2.Name);
    }

    [TestMethod]
    public void BasicFilter()
    {
        SearchQuery q = new();
        FakeCmdLineParser keyword = new FakeCmdLineParser();
        keyword.somevalue = "thewordtofilter";
        keyword.reg = new CommandLineRegistration() { key = "keyword", handlerType= CommandLineHandlerType.Filter };
        q.Filters.Add(keyword);
        SerializableSearchQuery r = SearchQueryJsonReader.GetSerializableSearchQuery(q);
        var json = r.GetQueryJson();
        
        //Does the basic json match
        SerializableSearchQuery q2 = SearchQueryJsonReader.LoadSearchQuery(json);
        Assert.IsNotNull(q2.FilterJson);
        Assert.IsTrue(q2.FilterJson.Count == 1);

        Assert.IsNotNull(r.FilterJson);
        Assert.AreEqual(r.FilterJson.Count, q2.FilterJson.Count);
        Assert.AreEqual(r.FilterJson[0], q2.FilterJson[0]);


        //Does the ultimate deserialization pass
        SearchQuery output = SearchQueryJsonReader.GetSearchQueryObject(q2);
        Assert.IsTrue(output.Filters.Count == 1);
        Assert.IsTrue(output.Filters[0].GetType() == typeof(FakeCmdLineParser));
        var outputfilter = (FakeCmdLineParser)output.Filters[0];
        Assert.IsNotNull(outputfilter.somevalue);
        Assert.IsTrue(outputfilter.somevalue.Equals(keyword.somevalue));

    }


    
    [TestMethod]
    public void BasicMultipleSameFilters()
    {
        SearchQuery q = new();
        FakeCmdLineParser keyword1 = new FakeCmdLineParser();
        keyword1.somevalue = "someword";
        keyword1.reg = new CommandLineRegistration() { key = "keyword", handlerType = CommandLineHandlerType.Filter };

        FakeCmdLineParser keyword2 = new FakeCmdLineParser();
        keyword2.somevalue = "anotherword";
        keyword2.reg = new CommandLineRegistration() { key = "keyword", handlerType = CommandLineHandlerType.Filter };

        q.Filters.Add(keyword1);
        q.Filters.Add(keyword2);
        SerializableSearchQuery r = SearchQueryJsonReader.GetSerializableSearchQuery(q);
        var json = r.GetQueryJson();

        //Does the basic json match
        SerializableSearchQuery q2 = SearchQueryJsonReader.LoadSearchQuery(json);


        Assert.IsNotNull(q2.FilterJson);
        Assert.IsNotNull(r.FilterJson);
        Assert.IsTrue(q2.FilterJson.Count == 2);

        Assert.AreEqual(r.FilterJson.Count, q2.FilterJson.Count);

        Assert.AreEqual(r.FilterJson[0], q2.FilterJson[0]);


        //Does the ultimate deserialization pass
        SearchQuery output = SearchQueryJsonReader.GetSearchQueryObject(q2);
        Assert.IsTrue(output.Filters.Count == 2);

    
        Assert.IsTrue(output.Filters[0].GetType() == typeof(FakeCmdLineParser));
        Assert.IsTrue(output.Filters[1].GetType() == typeof(FakeCmdLineParser));

        var outputfilter1 = (FakeCmdLineParser)output.Filters[0];
        var outputfilter2 = (FakeCmdLineParser)output.Filters[1];

        Assert.IsNotNull(outputfilter1.somevalue);
        Assert.IsNotNull(outputfilter2.somevalue);
        Assert.IsTrue(outputfilter1.somevalue.Equals(keyword1.somevalue));
        Assert.IsTrue(outputfilter2.somevalue.Equals(keyword2.somevalue));

    }

}
