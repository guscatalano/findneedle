using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle;
using findneedle.Implementations;
using findneedle.PluginSubsystem;
using FindNeedleCoreUtils;
using FindNeedlePluginLib.Interfaces;
using FindNeedlePluginLib.TestClasses;
using FindPluginCore.Searching;

namespace CoreTests;

[TestClass]
public class SearchQueryCmdLineParserTests
{
    private const string SOME_PARAM = "someparam";
    private const string SOME_KEY = "keyword1";

    public static Dictionary<CommandLineRegistration, ICommandLineParser> SetupSimpleFakeParser(CommandLineRegistration registration)
    {
        Dictionary<CommandLineRegistration, ICommandLineParser> parsers = new();
        var fakeParser = new FakeCmdLineParser()
        {
            reg = registration
        };
        parsers.Add(registration, fakeParser);
        return parsers;
    }

    [TestInitialize]
    public void TestSetup()
    {
        PluginManager.ResetSingleton();
    }

    [TestMethod]
    public void TestParseCmdIntoDictionaryBadInput()
    {
        var input = new List<CommandLineArgument>();
        input.Add(new CommandLineArgument() { key = "", value= ""});
        var q = SearchQueryCmdLine.ParseFromCommandLine(input, new PluginManager());
        Assert.AreEqual(0, q.GetLocations().Count);
    }

    [TestMethod]
    public void TestFilterKeywordToPlugin()
    {
        var input = new List<CommandLineArgument>();
        input.Add(new CommandLineArgument() { key = "filter_" + SOME_KEY, value = SOME_PARAM });
        
        var registration = new CommandLineRegistration() { handlerType = CommandLineHandlerType.Filter, key = SOME_KEY };
        var parsers = SetupSimpleFakeParser(registration);
        ISearchQuery q = SearchQueryCmdLine.ParseFromCommandLine(input, new PluginManager(), parsers);
        Assert.AreEqual(q.Locations.Count(), 0);
        Assert.AreEqual(q.Filters.Count(), 1);
        Assert.AreEqual(q.Processors.Count(), 0);
        Assert.IsTrue(((FakeCmdLineParser)q.Filters.First()).wasParseCalled);

    }

    [TestMethod]
    public void TestLocationKeywordToPlugin()
    {
        var input = new List<CommandLineArgument>();
        input.Add(new CommandLineArgument() { key = "location_" + SOME_KEY, value = SOME_PARAM });

        var registration = new CommandLineRegistration() { handlerType = CommandLineHandlerType.Location, key = SOME_KEY };
        var parsers = SetupSimpleFakeParser(registration);
        ISearchQuery q = SearchQueryCmdLine.ParseFromCommandLine(input, new PluginManager(), parsers);
        Assert.AreEqual(q.Locations.Count(), 1);
        Assert.AreEqual(q.Filters.Count(), 0);
        Assert.AreEqual(q.Processors.Count(), 0);
        Assert.IsTrue(((FakeCmdLineParser)q.Locations.First()).wasParseCalled);
    }

    [TestMethod]
    public void TestProcessorKeywordToPlugin()
    {
        var input = new List<CommandLineArgument>();
        input.Add(new CommandLineArgument() { key = "processor_" + SOME_KEY, value = SOME_PARAM });

        var registration = new CommandLineRegistration() { handlerType = CommandLineHandlerType.Processor, key = SOME_KEY };
        var parsers = SetupSimpleFakeParser(registration);
        ISearchQuery q = SearchQueryCmdLine.ParseFromCommandLine(input, new PluginManager(), parsers);
        
        Assert.AreEqual(q.Locations.Count(), 0);
        Assert.AreEqual(q.Filters.Count(), 0);
        Assert.AreEqual(q.Processors.Count(), 1);
        Assert.IsTrue(((FakeCmdLineParser)q.Processors.First()).wasParseCalled);
    }

    [TestMethod]
    public void TestParameterPassThrough()
    {
        var checkCallback = false;
        var input = new List<CommandLineArgument>();
        input.Add(new CommandLineArgument() { key = "filter_" + SOME_KEY, value = SOME_PARAM });

        var registration = new CommandLineRegistration() { handlerType = CommandLineHandlerType.Filter, key = SOME_KEY };
        var parsers = SetupSimpleFakeParser(registration);
        var FakeParser = (FakeCmdLineParser)parsers.First().Value;
        FakeParser.callbackForParse = (string parameter) =>
        {

            Assert.AreEqual(SOME_PARAM, parameter);
            checkCallback = true;
        };
        SearchQueryCmdLine.ParseFromCommandLine(input, new PluginManager(), parsers);
        Assert.IsTrue(checkCallback);
    }

    [TestMethod]
    public void TestEmptyParameterPassThrough()
    {
        var checkCallback = false;
        var input = new List<CommandLineArgument>();
        input.Add(new CommandLineArgument() { key = "filter_" + SOME_KEY, value = "" });

        var registration = new CommandLineRegistration() { handlerType = CommandLineHandlerType.Filter, key = SOME_KEY };
        var parsers = SetupSimpleFakeParser(registration);
        var FakeParser = (FakeCmdLineParser)parsers.First().Value;
        FakeParser.callbackForParse = (string parameter) =>
        {
            Assert.IsTrue(string.IsNullOrEmpty(parameter));
            checkCallback = true;
        };
        SearchQueryCmdLine.ParseFromCommandLine(input, new PluginManager(), parsers);
        Assert.IsTrue(checkCallback);
    }

    [TestMethod]
    public void TestComplexParameterPassThrough()
    {
        var input = new List<CommandLineArgument>();
        input.Add(new CommandLineArgument() { key = "filter_" + SOME_KEY, value = "(something,something)" });

        var registration = new CommandLineRegistration() { handlerType = CommandLineHandlerType.Filter, key = SOME_KEY };
        var parsers = SetupSimpleFakeParser(registration);
        var FakeParser = (FakeCmdLineParser)parsers.First().Value;
        var checkCallback = false;
        FakeParser.callbackForParse = (string parameter) =>
        {
            Assert.AreEqual("something,something", parameter); //We expect it to remove the brackets
            checkCallback = true;
        };
        SearchQueryCmdLine.ParseFromCommandLine(input, new PluginManager(), parsers);
        Assert.IsTrue(checkCallback);
    }


    [TestMethod]
    public void TestAddMultipleFileLog()
    {

        var input = new List<CommandLineArgument>() {
            new() { key = "location_path", value = @"C:\\windows\\explorer.exe" },
            new() { key = "location_path", value = @"C:\\windows\\system32" },
            new() { key = "location_path", value = @"C:\\windows\\system32\\" }
        };

        var registration = new CommandLineRegistration() { handlerType = CommandLineHandlerType.Location, key = "path" };
        var parsers = SetupSimpleFakeParser(registration);
        ISearchQuery q = SearchQueryCmdLine.ParseFromCommandLine(input, new PluginManager(), parsers);
        Assert.AreEqual(3, q.GetLocations().Count);
        foreach(var loc in q.GetLocations())
        {
            Assert.IsTrue(loc is FakeCmdLineParser);
            Assert.IsNotNull(((FakeCmdLineParser)loc).somevalue);
        }
        Assert.IsTrue(q.GetLocations().FirstOrDefault(x => ((FakeCmdLineParser)x).somevalue.Equals(@"C:\\windows\\explorer.exe")) != null);
        Assert.IsTrue(q.GetLocations().FirstOrDefault(x => ((FakeCmdLineParser)x).somevalue.Equals(@"C:\\windows\\system32")) != null);
        Assert.IsTrue(q.GetLocations().FirstOrDefault(x => ((FakeCmdLineParser)x).somevalue.Equals(@"C:\\windows\\system32\\")) != null);
    }

}
