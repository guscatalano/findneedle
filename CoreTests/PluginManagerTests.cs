using findneedle.Interfaces;
using findneedle.PluginSubsystem;
using FindNeedlePluginLib.Interfaces;
using FindPluginCore.PluginSubsystem;

namespace CoreTests;

[TestClass]
public sealed class PluginManagerTests
{

    [TestInitialize]
    public void InitializePluginTests()
    {
        if (File.Exists(PluginManager.LOADER_CONFIG))
        {
            File.Delete(PluginManager.LOADER_CONFIG);
        }
    }

    [TestMethod]
    public void TestDefaultSaveAndRead()
    {
       
        PluginManager x = new PluginManager();
        x.config = new PluginConfig();
        x.config.PathToFakeLoadPlugin = "somepath";
        x.config.entries.Add(new PluginConfigEntry() { name = "test", path = "testval" });
        x.SaveToFile();

        x = new PluginManager();
        Assert.IsNotNull(x.config);
        Assert.AreEqual(1, actual: x.config.entries.Count);
        Assert.AreEqual("test", actual: x.config.entries[0].name);
        Assert.AreEqual("testval", actual: x.config.entries[0].path);
        Assert.AreEqual("somepath", actual: x.config.PathToFakeLoadPlugin);
    }

    [TestMethod]
    public void TestLoadFakePlugin()
    {
        var TEST_PLUGIN_NAME = TestGlobals.TEST_DEP_PLUGIN;


        PluginManager pluginManager = new PluginManager();
        pluginManager.config = new PluginConfig();
        pluginManager.config.PathToFakeLoadPlugin = TestGlobals.FAKE_LOAD_PLUGIN_REL_PATH;
        pluginManager.config.entries.Add(new PluginConfigEntry() { name = "TestPlugin", path = TEST_PLUGIN_NAME });
        pluginManager.LoadAllPlugins();



        
    }


}
