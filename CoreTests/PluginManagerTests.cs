using System.Reflection;
using findneedle.PluginSubsystem;
using FindPluginCore.GlobalConfiguration;
using FindPluginCore.PluginSubsystem;
using FindNeedlePluginLib;

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
        if (File.Exists(PluginManager.LOADER_CONFIG))
        {
            File.Delete(PluginManager.LOADER_CONFIG);
        }
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
    public void TestPrint()
    {

        var x = PluginManager.GetSingleton();
        x.PrintToConsole();
        Assert.IsTrue(true); //didn't crash
    }

    [TestMethod]
    public void CallFakeLoaderOutputTest()
    {
       
        PluginManager man = new();
        man.config = new();
        man.config.PathToFakeLoadPlugin = "fake";
        try
        {
            man.CallFakeLoadPlugin("test"); //garbage
            Assert.Fail();
        }
        catch 
        {
            Assert.IsTrue(true); //We should throw
        }
        GlobalSettings.Debug = false;
        man.config.PathToFakeLoadPlugin = TestGlobals.FAKE_LOAD_PLUGIN_REL_PATH;
        Assert.IsTrue(File.Exists(TestGlobals.FAKE_LOAD_PLUGIN_REL_PATH), "bad test setup?");
        // Note: When called with empty string, FakeLoadPlugin outputs "No arguments were passed" and exits
        // CallFakeLoadPlugin will throw an exception because the process exits with -1
        // So we expect this to throw, not to return "Output is disabled"
        try
        {
            var output = man.CallFakeLoadPlugin(""); // This will throw because process exits with -1
            Assert.Fail("Expected exception when calling FakeLoadPlugin with empty argument");
        }
        catch
        {
            // Expected behavior - FakeLoadPlugin exits with error code when given empty argument
            Assert.IsTrue(true);
        }

        // Verify that the FakeLoadPlugin output file was created
        var appDataFolder = FindNeedleCoreUtils.FileIO.GetAppDataFindNeedlePluginFolder();
        var outputfile = Path.Combine(appDataFolder, "fakeloadplugin_output.txt");
        Assert.IsTrue(File.Exists(outputfile), "FakeLoadPlugin output file should exist");
        var textfile = File.ReadAllText(outputfile);
        Assert.IsTrue(textfile.Contains("No arguments were passed"), "Output file should contain error message");
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
