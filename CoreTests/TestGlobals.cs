using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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

    public const int TEST_DEP_PLUGIN_COUNT = 4;

    public static bool DoesInfoFromPathMatch(string path1, string path2)
    {
        var info = GetInfoFromPath(path1);
        var info2 = GetInfoFromPath(path2);
        return info["Configuration"] == info2["Configuration"] &&
               info["Platform"] == info2["Platform"] &&
               info["TargetRuntime"] == info2["TargetRuntime"] &&
               info["TargetFramework"] == info2["TargetFramework"];
    }

    public static Dictionary<string, string> GetInfoFromPath(string path)
    {
        Dictionary<string, string> ret = new();
        if (string.IsNullOrEmpty(path) || !System.IO.Directory.Exists(path))
        {
            throw new ArgumentException("Path is null or does not exist: " + path);
        }
        var parts = path.Split(System.IO.Path.DirectorySeparatorChar);

        ret["Configuration"] = "unknown"; //Debug/Release
        ret["Platform"] = "unknown"; //AnyCPU, x64, x86
        ret["TargetRuntime"] = "unknown"; //win-x64, win-x86, etc.
        ret["TargetFramework"] = "unknown"; //net8.0-windows10.0.26100.0
        foreach (var part in parts)
        {
            var clean = part.Replace("/", "").Replace("\\", "").Trim().ToLower();
            switch (clean)
            {
                case "release":
                case "debug":
                    if (ret["Configuration"] == "unknown")
                    {
                        ret["Configuration"] = clean;
                    }
                    else
                    {
                        throw new Exception("can't tell");
                    }
                    break;
                case "win-x64":
                case "win-x86":
                    if (ret["TargetRuntime"] == "unknown")
                    {
                        ret["TargetRuntime"] = clean;
                    }
                    else
                    {
                        throw new Exception("can't tell");
                    }
                    break;
                default:
                    if (clean.StartsWith("net") && clean.Contains("-windows"))
                    {
                        if (ret["TargetFramework"] == "unknown")
                        {
                            ret["TargetFramework"] = clean;
                        }
                        else
                        {
                            throw new Exception("can't tell");
                        }
                    }
                    else if (clean.StartsWith("x64") || clean.StartsWith("x86") || clean.StartsWith("anycpu"))
                    {
                        if (ret["Platform"] == "unknown")
                        {
                            ret["Platform"] = clean;
                        }
                        else
                        {
                            throw new Exception("can't tell");
                        }
                    }
                    break;
            }
        }

        return ret;
    }

    public static string PickRightParent(string basepath, string searchpath)
    {
        var maxTries = 10;
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

    public static string PickRightChild(string basepath, string searchExe, string originalPath = "null")
    {
        Exception? lastException = null;
        var tries = 3;
        List<string> discardedFinds = new();
        while (tries > 0)
        {
            try
            {
                List<string> files = FileIO.GetAllFiles(basepath).ToList();
                var rightFile = "";
                var found = false;
                foreach (var file in files)
                {
                    if (file.EndsWith(searchExe) && file.Contains("bin"))  
                    {
                        var temp = Path.GetFullPath(file).Replace(searchExe, "");
                        if (found)
                        {
                            
                            if (temp.Equals(rightFile))
                            {
                                continue;// We already found this one, so skip it  
                            } 

                            


                            throw new Exception("There are multiple " + searchExe + " in " + basepath + ". Found: " + rightFile + " and " + file);
                        }
                        if (!originalPath.Equals("null"))
                        {
                            if (DoesInfoFromPathMatch(temp, originalPath) == false)
                            {
                                discardedFinds.Add(temp);
                                continue; // We found a file, but it does not match the base info
                            }
                        }
                        found = true;
                        rightFile = Path.GetFullPath(file).Replace(searchExe, "");
                    }
                }
                if (found)
                {
                    return rightFile;
                }
                else
                {
                    if(discardedFinds.Count() > 0)
                    {
                        return discardedFinds[0]; // pick one
                    }
                    throw new Exception("Can't find " + searchExe + " in " + basepath);
                }
            }
            catch (Exception e)
            {
                lastException = e;
                tries--;
                Thread.Sleep(1000); // Try again incase compile shit is happening
            }
        }
        if (lastException != null)
        {
            throw lastException;
        } else
        {
            throw new Exception("Can't find bin after 3 tries, dont know why");
        }
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
        var childPath = PickRightChild(Path.Combine(basePath, "FakeLoadPlugin"), "FakeLoadPlugin.exe", path);
        var sourcePath = Path.Combine(basePath, childPath);
        if (!Directory.Exists(sourcePath))
        {
            throw new Exception("Can't find " + sourcePath + ". I am running in " + path + " my basepath was: " + basePath);
        }
        TestGlobals.FAKE_LOAD_PLUGIN_REL_PATH = Path.Combine(sourcePath, FAKE_LOAD_PLUGIN);
        TestGlobals.FAKE_LOAD_PLUGIN_PATH = sourcePath;

        
        basePath = PickRightParent(path, "TestProcessorPlugin");
        childPath = PickRightChild(Path.Combine(basePath, "TestProcessorPlugin"), "TestProcessorPlugin.dll", path);

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

