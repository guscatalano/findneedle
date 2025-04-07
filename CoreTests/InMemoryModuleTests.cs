using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle.PluginSubsystem;
using FindNeedlePluginLib.Interfaces;
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

    [TestMethod]
    public void TestModuleBasicLoader()
    {
        PluginManager man = new();
        man.config = new PluginConfig(); // Ensure config is not null
        man.config.PathToFakeLoadPlugin = TestGlobals.FAKE_LOAD_PLUGIN_REL_PATH;
        InMemoryPluginModule TestModule = new InMemoryPluginModule(TestGlobals.TEST_DEP_PLUGIN_REL_PATH, man, false);

        var InMemoryObjectGeneric = TestModule.GetObjectForTypeGeneric(TestModule.description[0]);
        Assert.AreEqual(TestModule.description[0], InMemoryObjectGeneric.description);
        Assert.AreEqual(TestModule.description, InMemoryObjectGeneric.plugin.description);

        var InMemoryObjectType = TestModule.GetObjectForType<IPluginDescription>(TestModule.description[0]);
        Type t = InMemoryObjectType.GetType();
        Assert.IsTrue(t.GenericTypeArguments.Length > 0);
        Assert.IsTrue(t.GenericTypeArguments.FirstOrDefault(x => x.ToString().Contains("IPluginDescription")) != null);
        Assert.AreEqual(InMemoryObjectType.description, TestModule.description[0]);
    }
}
