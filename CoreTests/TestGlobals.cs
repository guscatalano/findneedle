using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoreTests;

[TestClass]
public class TestGlobals
{
    public const string TEST_DEP_FOLDER = "TestDependencies";
    public const string TEST_DEP_PLUGIN = "TestProcessorPlugin.dll";
    public const string FAKE_LOAD_PLUGIN = "FakeLoadPlugin.exe";
    public const string TEST_DEP_PLUGIN_REL_PATH = TEST_DEP_FOLDER + "\\" + TEST_DEP_PLUGIN;
    public const string FAKE_LOAD_PLUGIN_REL_PATH = TEST_DEP_FOLDER + "\\" + FAKE_LOAD_PLUGIN;

    public const int TEST_DEP_PLUGIN_COUNT = 3;

    public static string PickRightParent(string basepath, string searchpath)
    {
        int maxTries = 10;
        while(maxTries > 0)
        {
            if (System.IO.Directory.Exists(System.IO.Path.Combine(basepath, searchpath)))
            {
                return basepath;
            }
            basepath = System.IO.Path.Combine(basepath, "..");
            maxTries--;
        }
        throw new Exception("Can't find " + searchpath);
    }


    [AssemblyInitialize]
    public static void CopyDependencies(TestContext testContext)
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
        var sourcePath = Path.Combine(basePath, "\\FakeLoadPlugin\\bin\\x64\\Debug\\net8.0-windows10.0.26100.0\\win-x64\\");

        if (!Directory.Exists(sourcePath))
        {
            throw new Exception("Can't find " + sourcePath + ". I am running in " + path);
        }

        foreach (var fileToCopy in Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories))
        {
            var file = Path.GetFileName(fileToCopy);
            var fileDest = Path.Combine(dest, file);
            var fileToCopyNormal = Path.GetFullPath(fileToCopy);
            if (!File.Exists(fileToCopyNormal))
            {
                throw new Exception("failed copy setup :(" + fileToCopyNormal);
            }
            File.Copy(fileToCopy, fileDest, true);
        }

        basePath = PickRightParent(path, "TestProcessorPlugin");
        sourcePath = Path.Combine(basePath, "\\TestProcessorPlugin\\bin\\Debug\\net8.0-windows10.0.26100.0\\");
        if (!Directory.Exists(sourcePath))
        {
            throw new Exception("Can't find " + sourcePath + ". I am running in " + path);
        }

        foreach (var fileToCopy in Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories))
        {
            var file = Path.GetFileName(fileToCopy);
            var fileDest = Path.Combine(dest, file);
            var fileToCopyNormal = Path.GetFullPath(fileToCopy);
            if (!File.Exists(fileToCopyNormal))
            {
                throw new Exception("failed copy setup :(" + fileToCopyNormal);
            }
            File.Copy(fileToCopy, fileDest, true);
        }

    }

    [AssemblyCleanup]
    public static void TearDown()
    {
        int maxTries = 10;
        while (maxTries > 0)
        {
            try
            {
                var path = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (path == null)
                {
                    throw new Exception("failed to cleanup");
                }
                var dest = System.IO.Path.Combine(path, TEST_DEP_FOLDER);
                if (System.IO.Directory.Exists(dest))
                {
                    System.IO.Directory.Delete(dest, true);
                }
            } catch
            {
                Thread.Sleep(1000);
                maxTries--;
                continue;
            }
        }
    }
}

