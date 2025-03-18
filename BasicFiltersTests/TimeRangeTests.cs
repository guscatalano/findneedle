using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BasicFiltersTests;

[TestClass]
public class TimeRangeTests
{
    [TestMethod]
    public void TestTimeRange()
    {
        Assert.Fail(); //fail on purpose
    }
    /*   [TestMethod]
   public void TestTimeSpanFilter()
   {
       Dictionary<string, string> input = new Dictionary<string, string>();
       input.Add("searchfilter", "time(2022-01-01 05:00:00Z, 2023-01-01 05:00:00Z)");

       SearchQuery q = new SearchQuery(input);
       Assert.IsTrue(q.GetFilters().Count == 1);
   }*/
}
