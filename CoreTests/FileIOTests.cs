﻿using System;
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
        
        IEnumerable<string> list = FileIO.GetAllFiles("FakeFolder", (path) => { Assert.Fail(); });
        Assert.AreEqual(2, list.Count()); //There are 5 files in the directory (Test Dependencies)
        Assert.IsTrue(list.First().StartsWith("FakeFolder")); //It starts with folder name

        Assert.IsTrue(list.FirstOrDefault(x => x.Contains("fakefile.txt")) != null); //It returned at least one of the files
        
    }
}