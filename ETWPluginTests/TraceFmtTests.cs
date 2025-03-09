using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle.WDK;

namespace ETWPluginTests;

[TestClass]
public class TraceFmtTests
{

    [TestMethod]
    public void TestBasicLoadPath()
    {
        WDKFinder.TEST_MODE = true;
        WDKFinder.TEST_MODE_SUCCESS = true;
        Assert.IsTrue(WDKFinder.GetTraceFmtPath().Contains("tracefmt.exe"));
        Assert.IsTrue(WDKFinder.GetTraceFmtPath().Contains("x64"));
    }

    [TestMethod]
    public void TestFailure()
    {
        WDKFinder.TEST_MODE = true;
        WDKFinder.TEST_MODE_SUCCESS = false;
        Assert.IsTrue(WDKFinder.GetTraceFmtPath().Equals(WDKFinder.NOT_FOUND_STRING));
    }

    [TestMethod]
    public void TestWDK()
    {
       
    }
}
