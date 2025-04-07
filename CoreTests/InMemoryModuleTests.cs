using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle.PluginSubsystem;
using FindPluginCore.PluginSubsystem;

namespace CoreTests;

[TestClass]
public class InMemoryModuleTests
{
    [TestMethod]
    public void TestModuleBasic()
    {
        PluginManager man = new();
        man.config = new PluginConfig(); // Ensure config is not null
        man.config.PathToFakeLoadPlugin = TestGlobals.FAKE_LOAD_PLUGIN_REL_PATH;
        InMemoryPluginModule x = new InMemoryPluginModule(TestGlobals.TEST_DEP_PLUGIN_REL_PATH, man, false);
        Assert.AreEqual(x.description.Count, TestGlobals.TEST_DEP_PLUGIN_COUNT);
    }
}
