using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle;
using FindNeedleCoreUtils;
using FindPluginCore.Searching;

namespace CoreTests;

[TestClass]
public class TextManipulationTests
{

    [TestMethod]
    public void TestParseCmdIntoDictionary()
    {
        var ret = TextManipulation.ParseCommandLineIntoDictionary(["a=1", "b=2"]);
        Assert.AreEqual(2, ret.Count);
        Assert.AreEqual("1", ret["a0"]);
        Assert.AreEqual("2", ret["b0"]);
    }

   

    [TestMethod]
    public void SplitApartTests()
    {
        
    }


    [TestMethod]
    public void ReplaceInvalidCharsTests()
    {
    }
}
