using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle.PluginSubsystem;
using FindNeedleCoreUtils;

namespace CoreTests;

[TestClass]
public sealed class FakeLoadPluginTest
{


    [TestMethod]
    public void TestBasic()
    {
        PluginManager.ResetSingleton();
        //This is tested more in PluginManagerTests 
        var list = FakeLoadPlugin.Program.LoadPluginModule(Path.GetFullPath(TestGlobals.TEST_DEP_PLUGIN_REL_PATH));
        Assert.IsTrue(list != null);
        Assert.AreEqual(list.Count, TestGlobals.TEST_DEP_PLUGIN_COUNT);
    }

    [TestMethod]
    public void TestFailBasic()
    {
        try
        {
            var list = FakeLoadPlugin.Program.LoadPluginModule("fakepath123");
            Assert.Fail("Should throw");
        }
        catch (Exception)
        {
            Assert.IsTrue(true);
        }
           
    }


}
