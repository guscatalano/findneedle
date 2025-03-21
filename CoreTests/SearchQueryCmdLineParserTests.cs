using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle;
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


    [TestMethod]
    public void TestParseCmdIntoDictionaryBadInput()
    {
        var input = new List<CommandLineArgument>();
        input.Add(new CommandLineArgument() { key = "", value= ""});
        var q = SearchQueryCmdLine.ParseFromCommandLine(input);
        Assert.AreEqual(0, q.GetLocations().Count);
    }

    [TestMethod]
    public void TestFilterKeywordToPlugin()
    {
        var input = new List<CommandLineArgument>();
        input.Add(new CommandLineArgument() { key = "filter_" + SOME_KEY, value = SOME_PARAM });
        
        var registration = new CommandLineRegistration() { handlerType = CommandLineHandlerType.Filter, key = SOME_KEY };
        var parsers = SetupSimpleFakeParser(registration);
        SearchQuery q = SearchQueryCmdLine.ParseFromCommandLine(input, parsers);
        Assert.IsTrue(((FakeCmdLineParser)parsers.First().Value).wasParseCalled);
        Assert.AreEqual(q.locations.Count(), 0);
        Assert.AreEqual(q.filters.Count(), 1);
        Assert.AreEqual(q.processors.Count(), 0);
    }

    [TestMethod]
    public void TestLocationKeywordToPlugin()
    {
        var input = new List<CommandLineArgument>();
        input.Add(new CommandLineArgument() { key = "location_" + SOME_KEY, value = SOME_PARAM });

        var registration = new CommandLineRegistration() { handlerType = CommandLineHandlerType.Location, key = SOME_KEY };
        var parsers = SetupSimpleFakeParser(registration);
        SearchQuery q = SearchQueryCmdLine.ParseFromCommandLine(input, parsers);
        Assert.IsTrue(((FakeCmdLineParser)parsers.First().Value).wasParseCalled);
        Assert.AreEqual(q.locations.Count(), 1);
        Assert.AreEqual(q.filters.Count(), 0);
        Assert.AreEqual(q.processors.Count(), 0);
    }

    [TestMethod]
    public void TestProcessorKeywordToPlugin()
    {
        var input = new List<CommandLineArgument>();
        input.Add(new CommandLineArgument() { key = "processor_" + SOME_KEY, value = SOME_PARAM });

        var registration = new CommandLineRegistration() { handlerType = CommandLineHandlerType.Processor, key = SOME_KEY };
        var parsers = SetupSimpleFakeParser(registration);
        SearchQuery q = SearchQueryCmdLine.ParseFromCommandLine(input, parsers);
        Assert.IsTrue(((FakeCmdLineParser)parsers.First().Value).wasParseCalled);
        Assert.AreEqual(q.locations.Count(), 0);
        Assert.AreEqual(q.filters.Count(), 0);
        Assert.AreEqual(q.processors.Count(), 1);
    }

    [TestMethod]
    public void TestParameterPassThrough()
    {
        var doubleCheckcallback = false;
        var input = new List<CommandLineArgument>();
        input.Add(new CommandLineArgument() { key = "filter_" + SOME_KEY, value = SOME_PARAM });

        var registration = new CommandLineRegistration() { handlerType = CommandLineHandlerType.Filter, key = SOME_KEY };
        var parsers = SetupSimpleFakeParser(registration);
        var FakeParser = (FakeCmdLineParser)parsers.First().Value;
        FakeParser.callbackForParse = (string parameter) =>
        {
            Assert.AreEqual(SOME_PARAM, parameter);
            doubleCheckcallback = true;
        };
        SearchQuery q = SearchQueryCmdLine.ParseFromCommandLine(input, parsers);
        Assert.IsTrue(FakeParser.wasParseCalled);
        Assert.IsTrue(doubleCheckcallback);
    }

    [TestMethod]
    public void TestEmptyParameterPassThrough()
    {
        var doubleCheckcallback = false;
        var input = new List<CommandLineArgument>();
        input.Add(new CommandLineArgument() { key = "filter_" + SOME_KEY, value = "" });

        var registration = new CommandLineRegistration() { handlerType = CommandLineHandlerType.Filter, key = SOME_KEY };
        var parsers = SetupSimpleFakeParser(registration);
        var FakeParser = (FakeCmdLineParser)parsers.First().Value;
        FakeParser.callbackForParse = (string parameter) =>
        {
            Assert.IsTrue(string.IsNullOrEmpty(parameter));
            doubleCheckcallback = true;
        };
        SearchQuery q = SearchQueryCmdLine.ParseFromCommandLine(input, parsers);
        Assert.IsTrue(FakeParser.wasParseCalled);
        Assert.IsTrue(doubleCheckcallback);
    }

    [TestMethod]
    public void TestComplexParameterPassThrough()
    {
        var input = new List<CommandLineArgument>();
        input.Add(new CommandLineArgument() { key = "filter_" + SOME_KEY, value = "(something,something)" });

        var registration = new CommandLineRegistration() { handlerType = CommandLineHandlerType.Filter, key = SOME_KEY };
        var parsers = SetupSimpleFakeParser(registration);
        var FakeParser = (FakeCmdLineParser)parsers.First().Value;
        FakeParser.callbackForParse = (string parameter) =>
        {
            Assert.AreEqual("something,something", parameter); //We expect it to remove the brackets
        };
        SearchQuery q = SearchQueryCmdLine.ParseFromCommandLine(input, parsers);
        Assert.IsTrue(FakeParser.wasParseCalled);
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
        SearchQuery q = SearchQueryCmdLine.ParseFromCommandLine(input, parsers);
        Assert.AreEqual(3, q.GetLocations().Count);
    }

}
