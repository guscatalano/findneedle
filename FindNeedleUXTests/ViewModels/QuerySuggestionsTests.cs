using System.Linq;
using FindNeedleUX.Pages.NativeResultViewer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUX.UnitTests.ViewModels;

/// <summary>The search box's completion: what is offered at each point of typing a query.</summary>
[TestClass]
public class QuerySuggestionsTests
{
    private static string[] Full(string text) => QuerySuggestions.For(text).Select(s => s.FullText).ToArray();
    private static string[] Shown(string text) => QuerySuggestions.For(text).Select(s => s.Display).ToArray();

    [TestMethod]
    public void EmptyOrPlainText_OffersNothing()
    {
        Assert.AreEqual(0, QuerySuggestions.For("").Count);
        Assert.AreEqual(0, QuerySuggestions.For("timeout").Count, "'timeout' is not a field prefix");
    }

    [TestMethod]
    public void PartialFieldName_CompletesToTheField_WithASpace()
    {
        var full = Full("lev");
        CollectionAssert.Contains(full, "level ");
        Assert.IsTrue(Shown("lev")[0].StartsWith("level"), "the field name leads the display text");
        CollectionAssert.Contains(Full("p"), "pid ");
        CollectionAssert.Contains(Full("da"), "data.", "data. completes without a space: the key follows");
        CollectionAssert.Contains(Full("t"), "tag ");
    }

    [TestMethod]
    public void AfterAField_OffersTheOperators()
    {
        var full = Full("level ");
        CollectionAssert.Contains(full, "level == ");
        CollectionAssert.Contains(full, "level =~ ");
        CollectionAssert.Contains(full, "level !~ ");
        Assert.IsTrue(Shown("level ").Any(d => d.Contains("regex")), "operators carry their meaning");
    }

    [TestMethod]
    public void TypingAnOperator_NarrowsToMatchingOperators()
    {
        var full = Full("msg !");
        CollectionAssert.Contains(full, "msg != ");
        CollectionAssert.Contains(full, "msg !~ ");
        CollectionAssert.DoesNotContain(full, "msg == ");
    }

    [TestMethod]
    public void AfterLevelOperator_OffersLevelNames_AndTagOffersTagNames()
    {
        CollectionAssert.Contains(Full("level == "), "level == Error ");
        CollectionAssert.Contains(Full("level == e"), "level == Error ");
        CollectionAssert.DoesNotContain(Full("level == e"), "level == Info ");
        CollectionAssert.Contains(Full("tag == "), "tag == Important ");
        Assert.AreEqual(0, QuerySuggestions.For("msg == ").Count, "free-text fields have no value list");
    }

    [TestMethod]
    public void AfterAFinishedPredicate_OffersTheConnectives()
    {
        var full = Full("level == Error ");
        CollectionAssert.Contains(full, "level == Error AND ");
        CollectionAssert.Contains(full, "level == Error OR ");
        CollectionAssert.Contains(full, "level == Error NOT ");
    }

    [TestMethod]
    public void InsideAnOpenString_OffersNothing()
    {
        Assert.AreEqual(0, QuerySuggestions.For("msg == \"hello wor").Count);
    }

    [TestMethod]
    public void SecondPredicate_CompletesFieldsAfterAConnective()
    {
        CollectionAssert.Contains(Full("level == Error AND pi"), "level == Error AND pid ");
    }
}
