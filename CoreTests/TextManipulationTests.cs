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
        Assert.AreEqual("1", ret.First(x => x.key.Equals("a")).value);
        Assert.AreEqual("2", ret.First(x => x.key.Equals("b")).value);
    }

    [TestMethod]
    public void TestParseCmdEmptyIntoDictionary()
    {
        //This is failing because we're doing things wrong
        var ret = TextManipulation.ParseCommandLineIntoDictionary(["a", "b=2"]);
        Assert.AreEqual(2, ret.Count);
        Assert.AreEqual("", ret.First(x => x.key.Equals("a")).value);
        Assert.AreEqual("2", ret.First(x => x.key.Equals("b")).value);
    }

    [TestMethod]
    public void TestParseCmdDuplicatesIntoDictionary()
    {
        //This is failing because we're doing things wrong
        var ret = TextManipulation.ParseCommandLineIntoDictionary(["b=3", "b=2"]);
        Assert.AreEqual(2, ret.Count);
        Assert.AreEqual("b", ret.First(x => x.value.Equals("3")).key);
        Assert.AreEqual("b", ret.First(x => x.value.Equals("2")).key);
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
