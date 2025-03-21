using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle.Implementations;
using FindNeedlePluginLib.Interfaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

[TestClass]
public class FolderLocationTests
{
    [TestMethod]
    public void TestCmdLineParserWithFile()
    {
        const string TEST_STRING = "C:\\windows\\explorer.exe";
        FolderLocation loc = new();
        var reg = loc.RegisterCommandHandler();
        //Check it got registered correctly
        Assert.AreEqual(reg.handlerType, CommandLineHandlerType.Location);
        Assert.IsTrue(reg.key.Equals("path"));
        try
        {
            loc.ParseCommandParameterIntoQuery(TEST_STRING);
        }
        catch (Exception e)
        {
            Assert.Fail(e.ToString());
        }
        Assert.IsTrue(loc.path.Equals(TEST_STRING));
    }

    [TestMethod]
    public void TestCmdLineParserBadPath()
    {
        const string TEST_STRING = "13275498735fdsfsf";
        FolderLocation loc = new();
        loc.RegisterCommandHandler();
        try
        {
            loc.ParseCommandParameterIntoQuery(TEST_STRING);
            Assert.Fail("Should have thrown");
        }
        catch (Exception)
        {
            Assert.IsTrue(true);
        }
    }

    [TestMethod]
    public void TestCmdLineParserWithFolder()
    {
        const string TEST_STRING = "C:\\windows\\";
        FolderLocation loc = new();
        var reg = loc.RegisterCommandHandler();
        //Check it got registered correctly
        Assert.AreEqual(reg.handlerType, CommandLineHandlerType.Location);
        Assert.IsTrue(reg.key.Equals("path"));
        try
        {
            loc.ParseCommandParameterIntoQuery(TEST_STRING);
        }
        catch (Exception e)
        {
            Assert.Fail(e.ToString());
        }
        Assert.IsTrue(loc.path.Equals(TEST_STRING));
    }
}
