using findneedle.Interfaces;
using findneedle.PluginSubsystem;
using findneedle.Utils;
using FindNeedlePluginLib.Interfaces;
using FindPluginCore.PluginSubsystem;

namespace CoreTests;

[TestClass]
public sealed class TempStorageTests
{
    [TestMethod]
    public void TestBasicTempStorage()
    {
        var path = TempStorage.GetMainTempPath();
        Assert.IsTrue(Path.Exists(path));
        var newPath = TempStorage.GetMainTempPath();
        Assert.AreEqual(path, newPath);
    }

    [TestMethod]
    public void TestPathGeneration()
    {
        var ROOT_PATH = "C:\\";
        var RANDOM_HINT = "OhNoTemp";
        TempStorage x = new();

        var path = x.GenerateNewPath(ROOT_PATH, RANDOM_HINT);
        Assert.IsTrue(path.StartsWith(ROOT_PATH));
        Assert.IsTrue(path.Contains(RANDOM_HINT));
        Assert.IsFalse(path.EndsWith(RANDOM_HINT));
        Assert.IsTrue(path.Count() > ROOT_PATH.Count() + RANDOM_HINT.Count()); //There is entropy

        for (var i = 0; i < 100; i++)
        {
            var newPath = x.GenerateNewPath(ROOT_PATH, RANDOM_HINT);
            Assert.AreNotEqual(path, newPath);
        }

    }

    [TestMethod]
    public void TestTempCleanup()
    {
        string? path;
        using (TempStorage x = new()) {
            
            path = x.GetExistingMainTempPath();
            Assert.IsTrue(Directory.Exists(path));
        }
        var maxTries = 10;
        while (Directory.Exists(path))
        {
            Thread.Sleep(100);
            maxTries--;
            if(maxTries <= 0)
            {
                break;
            }
        }
        
        Assert.IsFalse(Directory.Exists(path));
    }

    [TestMethod]
    public void TestNewTempDoesntCleanup()
    {
        string? path;
        using (TempStorage x = new())
        {

            path = x.GetNewTempPathWithHint("hint");
            Assert.IsTrue(Directory.Exists(path));
        }
        var maxTries = 10;
        while (Directory.Exists(path))
        {
            Thread.Sleep(100);
            maxTries--;
            if (maxTries <= 0)
            {
                break;
            }
        }

        Assert.IsTrue(Directory.Exists(path));
    }

    public void TestGetSingleton()
    {
        Assert.IsTrue(TempStorage.GetSingleton() != null);
        var temp1 = TempStorage.GetMainTempPath();
        Assert.IsTrue(temp1 != null);
        var temp2 = TempStorage.GetNewTempPath("");
        Assert.IsTrue(temp2 != null);
        Assert.AreNotEqual(temp1, temp2);

        var temp3 = TempStorage.GetNewTempPath("");
        Assert.IsTrue(temp3 != null);
        Assert.AreNotEqual(temp2, temp3);
    }
}
    
