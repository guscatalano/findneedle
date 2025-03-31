using findneedle.Interfaces;
using findneedle.PluginSubsystem;
using FindNeedlePluginLib.Interfaces;
using FindPluginCore.GlobalConfiguration;
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


        PluginManager pluginManager = new();
        pluginManager.config = new();
        pluginManager.config.PathToFakeLoadPlugin = TestGlobals.FAKE_LOAD_PLUGIN_REL_PATH;
        pluginManager.config.entries.Add(new PluginConfigEntry() { name = "TestPlugin", path = TEST_PLUGIN_NAME });
        pluginManager.LoadAllPlugins();
        foreach(var module in pluginManager.loadedPluginsModules)
        {
            Assert.IsTrue(module.LoadedSuccessfully);
        }
        

    }

    [TestMethod]
    public void TestLoadFakePluginGetAll()
    {
        var TEST_PLUGIN_NAME = TestGlobals.TEST_DEP_PLUGIN;
        GlobalSettings.Debug = true;

        PluginManager pluginManager = new();
        pluginManager.config = new();
        pluginManager.config.PathToFakeLoadPlugin = TestGlobals.FAKE_LOAD_PLUGIN_REL_PATH;
        pluginManager.config.entries.Add(new PluginConfigEntry() { name = "TestPlugin", path = TEST_PLUGIN_NAME });
        pluginManager.LoadAllPlugins();
        var ret = pluginManager.GetAllPluginsInstancesOfAType<ISearchOutput>();
        Assert.AreEqual(1, ret.Count());

    }

    [TestMethod]
    public void TestSingleton()
    {

        var x = PluginManager.GetSingleton();
        var y = PluginManager.GetSingleton();
        Assert.IsTrue(x == y);
    }

    [TestMethod]
    public void TestGetPath()
    {
        var x = PluginManager.GetSingleton();
        x.config = new PluginConfig();
        x.config.PathToFakeLoadPlugin = "somepath";
        try
        {
            x.GetFakeLoadPluginPath();
            Assert.Fail();
        }
        catch
        {
            Assert.IsTrue(true); //should throw with fake path
        }
        var realPathToMyself = Environment.ProcessPath;
        if (realPathToMyself != null)
        {
            x.config.PathToFakeLoadPlugin = realPathToMyself;
            var teststr = x.GetFakeLoadPluginPath();
            Assert.IsTrue(teststr.Equals(realPathToMyself));
        }
        else
        {
            Assert.Inconclusive();
        }
    }
}
