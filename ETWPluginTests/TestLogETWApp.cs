using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ETWPluginTests;

[TestClass]
public class TestLogETWApp
{
    [TestMethod]
    public void BasicTest()
    {
        LogETWApp.LogSomeStuff.LogFor10Seconds();
        Assert.IsTrue(true); //We did not crash so yay
    }
}
