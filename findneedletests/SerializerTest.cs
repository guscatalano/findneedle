using findneedle;
using findneedle.Implementations;

namespace findneedletests;
[TestClass]
public class SerializerTest
{

    [TestMethod]
    public void BasicSave()
    {
        SearchQuery q = new();
        q.Name = "test";
        SerializableSearchQuery r = SearchQueryJsonReader.GetSerializableSearchQuery(q);
        var json = r.GetQueryJson();
        SerializableSearchQuery q2 = SearchQueryJsonReader.LoadSearchQuery(json);
        Assert.AreEqual(r.Name, q2.Name);
    }

    [TestMethod]
    public void BasicFilter()
    {
        SearchQuery q = new();
        SimpleKeywordFilter keyword = new SimpleKeywordFilter("word");
        q.filters.Add(keyword);
        SerializableSearchQuery r = SearchQueryJsonReader.GetSerializableSearchQuery(q);
        var json = r.GetQueryJson();
        
        //Does the basic json match
        SerializableSearchQuery q2 = SearchQueryJsonReader.LoadSearchQuery(json);
#pragma warning disable CS8602 // Dereference of a possibly null reference.
        Assert.IsTrue(q2.FilterJson.Count == 1);

        Assert.AreEqual(r.FilterJson.Count, q2.FilterJson.Count);
        Assert.AreEqual(r.FilterJson[0], q2.FilterJson[0]);
#pragma warning restore CS8602 // Dereference of a possibly null reference.

        //Does the ultimate deserialization pass
        SearchQuery output = SearchQueryJsonReader.GetSearchQueryObject(q2);
        Assert.IsTrue(output.filters.Count == 1);
        Assert.IsTrue(output.filters[0].GetType() == typeof(SimpleKeywordFilter));
        Assert.IsTrue(((SimpleKeywordFilter)output.filters[0]).term.Equals(keyword.term));

    }



    [TestMethod]
    public void BasicMultipleSameFilters()
    {
        SearchQuery q = new();
        SimpleKeywordFilter keyword = new SimpleKeywordFilter("word");
        SimpleKeywordFilter keyword2 = new SimpleKeywordFilter("word2");
        q.filters.Add(keyword);
        q.filters.Add(keyword2);
        SerializableSearchQuery r = SearchQueryJsonReader.GetSerializableSearchQuery(q);
        var json = r.GetQueryJson();

        //Does the basic json match
        SerializableSearchQuery q2 = SearchQueryJsonReader.LoadSearchQuery(json);
#pragma warning disable CS8602 // Dereference of a possibly null reference.
        Assert.IsTrue(q2.FilterJson.Count == 2);

        Assert.AreEqual(r.FilterJson.Count, q2.FilterJson.Count);
#pragma warning restore CS8602 // Dereference of a possibly null reference.
        Assert.AreEqual(r.FilterJson[0], q2.FilterJson[0]);


        //Does the ultimate deserialization pass
        SearchQuery output = SearchQueryJsonReader.GetSearchQueryObject(q2);
        Assert.IsTrue(output.filters.Count == 2);
        Assert.IsTrue(output.filters[0].GetType() == typeof(SimpleKeywordFilter));
        Assert.IsTrue(((SimpleKeywordFilter)output.filters[0]).term.Equals(keyword.term));
        Assert.IsTrue(((SimpleKeywordFilter)output.filters[1]).term.Equals(keyword2.term));

    }

}
