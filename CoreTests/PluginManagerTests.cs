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
        var output = man.CallFakeLoadPlugin(""); //Should not throw
        Assert.IsTrue(output.Equals("Output is disabled"));

        GlobalSettings.Debug = true;
        output = man.CallFakeLoadPlugin(""); //Should not throw
        Assert.IsNotEmpty(output);
        Assert.IsFalse(output.Equals("Output is disabled"));

        // Use FileIO to get the AppData path for the output file
        var appDataFolder = FindNeedleCoreUtils.FileIO.GetAppDataFindNeedlePluginFolder();
        var outputfile = Path.Combine(appDataFolder, "fakeloadplugin_output.txt");
        Assert.IsTrue(File.Exists(outputfile));
        var textfile = File.ReadAllText(outputfile);
        // Normalize line endings to '\n' for both file content and output
        string NormalizeLineEndings(string s) => s.Replace("\r\n", "\n").Replace("\r", "\n");
        Assert.IsTrue(NormalizeLineEndings(textfile).Contains(NormalizeLineEndings(output))); //We do contains, cause file contains the start time
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
