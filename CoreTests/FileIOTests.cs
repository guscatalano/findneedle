using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FindNeedleCoreUtils;

namespace CoreTests;

[TestClass]
public sealed class FileIOTests
{


    [TestMethod]
    public void TestReadDirectory()
    {
        IEnumerable<string> list = FileIO.GetAllFiles(TestGlobals.TEST_DEP_FOLDER, (path) => { Assert.Fail(); });
        Assert.AreEqual(196, list.Count()); //There are 5 files in the directory (Test Dependencies)
        Assert.IsTrue(list.First().StartsWith(TestGlobals.TEST_DEP_FOLDER)); //It starts with folder name

        Assert.IsTrue(list.FirstOrDefault(x => x.Contains(TestGlobals.TEST_DEP_PLUGIN)) != null); //It returned at least one of the files
    }
}