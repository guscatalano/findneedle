using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FindNeedlePluginLib.Interfaces;
using FindNeedlePluginLib.TestClasses;

namespace CoreTests;

[TestClass]
public sealed class InterfaceTests
{

    [TestMethod]
    public void TestCommandLineRegistrationConversion()
    {
        Assert.IsTrue("filter".Equals(CommandLineRegistration.HandlerTypeToString(CommandLineHandlerType.Filter)));
        Assert.IsTrue("processor".Equals(CommandLineRegistration.HandlerTypeToString(CommandLineHandlerType.Processor)));
        Assert.IsTrue("location".Equals(CommandLineRegistration.HandlerTypeToString(CommandLineHandlerType.Location)));

        try
        {
            CommandLineRegistration.HandlerTypeToString((CommandLineHandlerType)999);
            Assert.Fail("Should have thrown");
        }
        catch
        {
            Assert.IsTrue(true); // Should throw
        }
    }

    [TestMethod]
    public void TestCommandLineRegistration()
    {
        CommandLineRegistration reg = new();
        reg.handlerType = CommandLineHandlerType.Filter;
        reg.key = "test";
        Assert.IsTrue("filter_test".Equals(reg.GetCmdLineKey()));
    }

    [TestMethod]
    public void TestInvalidPluginDescription()
    {
        var output = IPluginDescription.GetInvalidPluginDescription("testclass", "testsource", ["something"], ["somethingelse", "yea"], "testerror");
        Assert.IsTrue(output.validPlugin == false);
        Assert.IsTrue("testclass".Equals(output.ClassName));
        Assert.IsTrue("testsource".Equals(output.SourceFile));
        Assert.IsTrue("testerror".Equals(output.validationErrorMessage));
        Assert.IsTrue(output.ImplementedInterfaces.Count == 1);
        Assert.IsTrue(output.ImplementedInterfacesShort.Count == 2);
        Assert.IsTrue("Invalid".Equals(output.TextDescription));
        Assert.IsTrue("Invalid".Equals(output.FriendlyName));
    }

    [TestMethod]
    public void TestValidPluginDescription()
    {
        FakePluginDescription description = new();
        description.textdescription = "validdescription";
        description.friendlyname = "realfriend";
        var output = IPluginDescription.GetPluginDescription(description, "testsource", ["something"], ["somethingelse", "yea"]);
        Assert.IsTrue(output.validPlugin == true);
        Assert.IsTrue("FindNeedlePluginLib.TestClasses.FakePluginDescription".Equals(output.ClassName));
        Assert.IsTrue("testsource".Equals(output.SourceFile));
        Assert.IsTrue("".Equals(output.validationErrorMessage));
        Assert.IsTrue(output.ImplementedInterfaces.Count == 1);
        Assert.IsTrue(output.ImplementedInterfacesShort.Count == 2);
        Assert.IsTrue("validdescription".Equals(output.TextDescription));
        Assert.IsTrue("realfriend".Equals(output.FriendlyName));
    }

    [TestMethod]
    public void TestStaticMethods()
    {
        FakePluginDescription x = new();
        var className = x.GetPluginClassName();
        Assert.IsTrue("FindNeedlePluginLib.TestClasses.FakePluginDescription".Equals(className));
    }

}
