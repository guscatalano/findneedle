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
        var TEST_PLUGIN_SEARCH_TYPE = "ISearchOutput";

        PluginManager pluginManager = new PluginManager();
        pluginManager.config = new PluginConfig();
        pluginManager.config.PathToFakeLoadPlugin = TestGlobals.FAKE_LOAD_PLUGIN_REL_PATH;
        pluginManager.config.entries.Add(new PluginConfigEntry() { name = "TestPlugin", path = TEST_PLUGIN_NAME });
        pluginManager.LoadAllPlugins();

        //Test that we identified everything in the DLL correctly
        Assert.IsTrue(pluginManager.pluginsLoadedByType.Keys.Count == 4); //There are 4 types (including the descriptor and IDisposable)

        //Check that it actually got loaded
        var nullPluginKey = pluginManager.pluginsLoadedByType.Keys.FirstOrDefault(x => x.Contains(TEST_PLUGIN_SEARCH_TYPE));
        Assert.IsTrue(nullPluginKey != null);
        Assert.IsTrue(pluginManager.pluginsLoadedByType[nullPluginKey].Count == 1);
        Assert.IsTrue(pluginManager.pluginsLoadedByType[nullPluginKey].First().LoadedSuccessfully);



        
    }


}
