using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle.Utils;

namespace CoreTests;

[TestClass]
public sealed class FakeLoadPluginTest
{
    [TestMethod]
    public void TestBasic()
    {
        //This is tested more in PluginManagerTests 
        var list = FakeLoadPlugin.Program.LoadPlugin(Path.GetFullPath(TestGlobals.TEST_DEP_PLUGIN_REL_PATH));
        Assert.IsTrue(list != null);
        Assert.AreEqual(list.Count, TestGlobals.TEST_DEP_PLUGIN_COUNT);
    }
}
