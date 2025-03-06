using findneedle.PluginSubsystem;
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
        PluginManager x = new PluginManager();
        x.config = new PluginConfig();
        x.config.PathToFakeLoadPlugin = "TestDependencies\\FakeLoadPlugin.exe";
        x.config.entries.Add(new PluginConfigEntry() { name = "TestPlugin", path = "TestProcessorPlugin.dll" });
        x.LoadAllPlugins();

        Assert.IsTrue(x.pluginsLoadedByPath.Keys.First().EndsWith("TestProcessorPlugin.dll"));
        Assert.IsTrue(x.pluginsLoadedByPath.First().Value.Count() == 3); //There are 3 plugins
        Assert.IsTrue(x.pluginsLoadedByType.Keys.Count == 3); //There are 3 types (including the descriptor)
    }
}
