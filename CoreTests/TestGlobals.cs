using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FindNeedleCoreUtils;
using Windows.ApplicationModel.Search;

namespace CoreTests;

[TestClass]
public class TestGlobals
{
    public const string TEST_DEP_FOLDER = "TestDependencies";
    public const string TEST_DEP_PLUGIN = "TestProcessorPlugin.dll";
    public const string FAKE_LOAD_PLUGIN = "FakeLoadPlugin.exe";
    public static string FAKE_LOAD_PLUGIN_PATH = "";
    public static string TEST_DEP_PLUGIN_REL_PATH = TEST_DEP_PLUGIN;
    public static string FAKE_LOAD_PLUGIN_REL_PATH = FAKE_LOAD_PLUGIN;

    public const int TEST_DEP_PLUGIN_COUNT = 3;
    
    public static string PickRightParent(string basepath, string searchpath)
    {
        int maxTries = 10;
        while(maxTries > 0)
        {
            if (System.IO.Directory.Exists(System.IO.Path.Combine(basepath, searchpath)))
            {
                return Path.GetFullPath(basepath);
            }
            basepath = System.IO.Path.Combine(basepath, "..");
            maxTries--;
        }
        throw new Exception("Can't find " + searchpath);
    }

    public static string PickRightChild(string basepath, string searchExe)
    {

        List<string> files = FileIO.GetAllFiles(basepath).ToList();
        foreach (var file in files)
        {
            if (file.Contains(searchExe) && file.Contains("bin"))
            {
                return Path.GetFullPath(file).Replace(searchExe, "");
            }
        }
        throw new Exception("Can't find " + searchExe);
    }


    [AssemblyInitialize]
    public static void FigureOutDependencies (TestContext testContext)
    {

        var path = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        if(path == null)
        {
            throw new Exception("failed setup");
        }
        var dest = System.IO.Path.Combine(path, TEST_DEP_FOLDER);
        if (System.IO.Directory.Exists(dest))
        {
            System.IO.Directory.Delete(dest, true);
        }
        System.IO.Directory.CreateDirectory(dest);
        var basePath = PickRightParent(path, "FakeLoadPlugin");
        var childPath = PickRightChild(Path.Combine(basePath, "FakeLoadPlugin"), "FakeLoadPlugin.exe");
        var sourcePath = Path.Combine(basePath, childPath);

        if (!Directory.Exists(sourcePath))
        {
            throw new Exception("Can't find " + sourcePath + ". I am running in " + path + " my basepath was: " + basePath);
        }
        TestGlobals.FAKE_LOAD_PLUGIN_REL_PATH = Path.Combine(sourcePath, FAKE_LOAD_PLUGIN);
        TestGlobals.FAKE_LOAD_PLUGIN_PATH = sourcePath;

        
        basePath = PickRightParent(path, "TestProcessorPlugin");
        childPath = PickRightChild(Path.Combine(basePath, "TestProcessorPlugin"), "TestProcessorPlugin.dll");
        sourcePath = Path.Combine(basePath, childPath);
        if (!Directory.Exists(sourcePath))
        {
            throw new Exception("Can't find " + sourcePath + ". I am running in " + path);
        }

        TestGlobals.TEST_DEP_PLUGIN_REL_PATH = Path.Combine(sourcePath, TEST_DEP_PLUGIN);

    }


    [AssemblyCleanup]
    public static void TearDown()
    {
        
    }
}

