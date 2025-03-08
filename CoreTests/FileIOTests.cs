using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle.Utils;

namespace CoreTests;

[TestClass]
public sealed class FileIOTests
{


    [TestMethod]
    public void TestReadDirectory()
    {
        var TEST_DEP_FOLDER = "TestDependencies";

        IEnumerable<string> list = FileIO.GetAllFiles(TEST_DEP_FOLDER, (path) => { Assert.Fail(); });
        Assert.AreEqual(list.Count(), 5); //There are 5 files in the directory (Test Dependencies)
        Assert.IsTrue(list.First().StartsWith(TEST_DEP_FOLDER)); //It starts with folder name
        Assert.IsTrue(list.FirstOrDefault(x => x.Contains("TestProcessorPlugin.dll")) != null); //It returned at least one of the files
    }
}