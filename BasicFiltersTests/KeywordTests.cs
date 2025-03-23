using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle.Implementations;
using FindNeedleCoreUtils;
using FindNeedlePluginLib.Interfaces;
using Windows.ApplicationModel.Search;

namespace BasicFiltersTests;


[TestClass]
public sealed class KeywordTests
{
    [TestMethod]
    public void SimpleKeywordTest()
    {
       // Assert.Fail(); //fail on purpose
    }

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
}
