using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle.Implementations;
using FindNeedleCoreUtils;
using FindNeedlePluginLib.Interfaces;
using FindNeedlePluginLib.TestClasses;
using Windows.ApplicationModel.Search;

namespace BasicFiltersTests;


[TestClass]
public sealed class KeywordTests
{


    [TestMethod]
    public void KeywordCommandLineTest()
    {
        const string TEST_STRING = "13275498735fdsfsf";
        SimpleKeywordFilter filter = new();
        var reg = filter.RegisterCommandHandler();
        Assert.AreEqual(reg.handlerType, CommandLineHandlerType.Filter);
        Assert.IsTrue(reg.key.Equals("keyword"));
        try
        {
            filter.ParseCommandParameterIntoQuery(TEST_STRING);
        }
        catch (Exception e)
        {
            Assert.Fail(e.ToString());
        }
        Assert.IsTrue(filter.term.Equals(TEST_STRING));
       
    }

    [TestMethod]
    public void KeywordFilterTest()
    {
        const string TEST_STRING = "F1ndme";
        SimpleKeywordFilter filter = new();
        filter.ParseCommandParameterIntoQuery(TEST_STRING);
        FakeSearchResult passOne = new();
        passOne.searchableDataString = "fdfsfs" + TEST_STRING + "fdsu8945u32";
        Assert.IsTrue(filter.Filter(passOne));

        FakeSearchResult failOne = new();
        passOne.searchableDataString = "ffdsfsfsf32432";
        Assert.IsFalse(filter.Filter(failOne));
    }
}
