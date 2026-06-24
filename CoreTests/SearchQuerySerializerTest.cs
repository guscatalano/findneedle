using System.Collections.Generic;
using findneedle;
using findneedle.Implementations;
using FindNeedlePluginLib;
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

    // The UX saves the workspace from the raw pieces (not a SearchQuery cast, since it runs NuSearchQuery).
    [TestMethod]
    public void ComponentOverload_SerializesNameAndFilter()
    {
        var keyword = new FakeCmdLineParser
        {
            somevalue = "x",
            reg = new CommandLineRegistration { key = "keyword", handlerType = CommandLineHandlerType.Filter }
        };
        var r = SearchQueryJsonReader.GetSerializableSearchQuery(
            new List<ISearchLocation>(), new List<ISearchFilter> { keyword }, null, "ws");

        Assert.AreEqual("ws", r.Name);
        Assert.IsNotNull(r.FilterJson);
        Assert.AreEqual(1, r.FilterJson.Count);

        var output = SearchQueryJsonReader.GetSearchQueryObject(
            SearchQueryJsonReader.LoadSearchQuery(r.GetQueryJson()));
        Assert.AreEqual(1, output.Filters.Count);
        Assert.IsTrue(output.Filters[0] is FakeCmdLineParser);
    }

    // A saved workspace keeps its RuleDSL rule paths.
    [TestMethod]
    public void RulesConfigPaths_RoundTrip()
    {
        var rules = new List<string> { @"C:\rules\a.rules.json", @"C:\rules\b.rules.json" };
        var r = SearchQueryJsonReader.GetSerializableSearchQuery(
            new List<ISearchLocation>(), new List<ISearchFilter>(), rules, "ws");

        var q2 = SearchQueryJsonReader.LoadSearchQuery(r.GetQueryJson());
        CollectionAssert.AreEqual(rules, q2.RulesConfigPaths);

        var output = SearchQueryJsonReader.GetSearchQueryObject(q2);
        CollectionAssert.AreEqual(rules, output.RulesConfigPaths);
    }

}
