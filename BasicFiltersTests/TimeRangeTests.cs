using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle.Implementations;
using FindNeedlePluginLib.Interfaces;
using FindNeedlePluginLib.TestClasses;

namespace BasicFiltersTests;

[TestClass]
public class TimeRangeTests
{

   [TestMethod]
   public void TestTimeRangeFilter()
   {
        const string TEST_STRING = "2022-01-01 05:00:00Z, 2022-01-01 07:00:00Z";
        TimeRangeFilter filter = new();
        var reg = filter.RegisterCommandHandler();
        Assert.AreEqual(reg.handlerType, CommandLineHandlerType.Filter);
        Assert.IsTrue(reg.key.Equals("time"));
        try
        {
            filter.ParseCommandParameterIntoQuery(TEST_STRING);
        }
        catch (Exception e)
        {
            Assert.Fail(e.ToString());
        }
        Assert.IsTrue(filter.start == DateTime.Parse("2022-01-01 05:00:00Z"));
        Assert.IsTrue(filter.end == DateTime.Parse("2022-01-01 07:00:00Z"));

        DateTime testDate = DateTime.Parse("2022-01-01 06:00:00Z");
        FakeSearchResult result = new();
        result.logTime = testDate;
        Assert.IsTrue(filter.Filter(result));

        DateTime badtestDate = DateTime.Parse("2022-01-01 07:05:00Z");
        result.logTime = badtestDate;
        Assert.IsFalse(filter.Filter(result));
    }
}
