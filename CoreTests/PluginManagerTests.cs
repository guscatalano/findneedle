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
        var TEST_PLUGIN_SEARCH_TYPE = "SearchOutput";
        var TEST_PLUGIN_OUTPUT_NULL = "null";
        var TEST_PLUGIN_OUTPUT_NULL_NAME = "SampleNullOutput";

        PluginManager pluginManager = new PluginManager();
        pluginManager.config = new PluginConfig();
        pluginManager.config.PathToFakeLoadPlugin = TestGlobals.FAKE_LOAD_PLUGIN_REL_PATH;
        pluginManager.config.entries.Add(new PluginConfigEntry() { name = "TestPlugin", path = TEST_PLUGIN_NAME });
        pluginManager.LoadAllPlugins();

        //Test that we identified everything in the DLL correctly
        Assert.IsTrue(pluginManager.pluginsLoadedByPath.Keys.First().EndsWith(TEST_PLUGIN_NAME));
        Assert.IsTrue(pluginManager.pluginsLoadedByPath.First().Value.Count() == TestGlobals.TEST_DEP_PLUGIN_COUNT); //There are 3 plugins
        Assert.IsTrue(pluginManager.pluginsLoadedByType.Keys.Count == 3); //There are 3 types (including the descriptor)

        //Check that it actually got loaded
        var nullPluginKey = pluginManager.pluginsLoadedByType.Keys.FirstOrDefault(x => x.Contains(TEST_PLUGIN_SEARCH_TYPE));
        Assert.IsTrue(nullPluginKey != null);
        Assert.IsTrue(pluginManager.pluginsLoadedByType[nullPluginKey].Count == 1);
        Assert.IsTrue(pluginManager.pluginsLoadedByType[nullPluginKey].First().LoadedSuccessfully);


        //Check that we can find it by type easily
        var nullPlugin = pluginManager.GetAllPluginsOfAType(TEST_PLUGIN_SEARCH_TYPE).First();
        PluginDescription? nullPluginDescription = nullPlugin.description.FirstOrDefault(x => x.ImplementedInterfacesShort.Contains(nullPluginKey));
        Assert.IsTrue(nullPluginDescription != null);
        var obj = nullPlugin.CreateInstance((PluginDescription)nullPluginDescription);
        Assert.IsTrue(obj != null);
        Assert.AreEqual(obj.GetType().Name, TEST_PLUGIN_OUTPUT_NULL_NAME);
        Assert.AreEqual(((ISearchOutput)obj).GetOutputFileName(), TEST_PLUGIN_OUTPUT_NULL);


        //Check taht we can instantiate it easily
        var y = pluginManager.GetAllPluginObjectsOfAType(TEST_PLUGIN_SEARCH_TYPE).First().CreateInstance();
        Assert.IsTrue(y != null);
        Assert.AreEqual(TEST_PLUGIN_OUTPUT_NULL, ((ISearchOutput)y).GetOutputFileName());
        
    }


}
