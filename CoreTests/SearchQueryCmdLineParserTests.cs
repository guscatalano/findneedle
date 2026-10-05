using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle;
using findneedle.Implementations;
using findneedle.PluginSubsystem;
using FindNeedleCoreUtils;
using FindNeedlePluginLib;
using FindNeedlePluginLib.TestClasses;
using FindPluginCore.Searching;
using TestProcessorPlugin;
using System.IO;

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

    /// <summary>
    /// A keyed `location_path=<file>` argument must produce a location that can actually PARSE the file.
    ///
    /// ParseFromCommandLine never adds the registered parser itself: it creates a fresh instance and calls
    /// Clone(prototype) on it. FolderLocation.Clone used to keep nothing, so the location that went into the
    /// query had no extension processors and matched no file - every keyed CLI search returned 0 rows and
    /// exit 2 ("no rows decoded"), which reads as "your log is empty" rather than "nothing parsed it". The
    /// positional form (a bare path) sets the list itself and worked, which is why this survived: found by
    /// running the shipped 1.0.267 CLI on a clean machine.
    /// </summary>
    [TestMethod]
    public void LocationFromKeyedArgument_CanStillParseTheFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FN_cmdline_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "sample.txt");   // SampleFileExtensionProcessor registers .txt
        File.WriteAllText(file, "hello");
        try
        {
            // The prototype is what GetCommandLineParsers builds: one location carrying the plugin
            // manager's processor list.
            var prototype = new FolderLocation();
            prototype.SetExtensionProcessorList(new List<IFileExtensionProcessor> { new SampleFileExtensionProcessor() });

            var registration = new CommandLineRegistration() { handlerType = CommandLineHandlerType.Location, key = "path" };
            var parsers = new Dictionary<CommandLineRegistration, ICommandLineParser> { { registration, prototype } };

            var input = new List<CommandLineArgument> { new() { key = "location_path", value = file } };
            var q = SearchQueryCmdLine.ParseFromCommandLine(input, new PluginManager(), parsers);

            Assert.AreEqual(1, q.Locations.Count, "the keyed argument should add exactly one location");
            var loc = (FolderLocation)q.Locations.First();
            Assert.AreNotSame(prototype, loc, "the query gets a clone, not the registered prototype");

            loc.LoadInMemory();
            // SampleFileExtensionProcessor yields 2 results per handled .txt file. Zero here means the
            // clone lost its processors.
            Assert.AreEqual(2, loc.Search().Count, "the cloned location parsed nothing - Clone dropped the extension processors");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
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
        foreach (var loc in q.GetLocations())
        {
            Assert.IsNotNull(loc); // Fix for CS8602
            Assert.IsInstanceOfType(loc, typeof(FakeCmdLineParser)); // Ensures type safety
            var fakeParser = loc as FakeCmdLineParser;
            Assert.IsNotNull(fakeParser?.somevalue); // Fix for CS8602
        }

        Assert.IsNotNull(q.GetLocations().FirstOrDefault(static x =>
        {
            if (x is FakeCmdLineParser y && y.somevalue != null)
            {
                return y.somevalue.Equals(@"C:\\windows\\explorer.exe");
            }
            return false;
        }));

        Assert.IsNotNull(q.GetLocations().FirstOrDefault(static x =>
        {
            if (x is FakeCmdLineParser y && y.somevalue != null)
            {
                return y.somevalue.Equals(@"C:\\windows\\system32");
            }
            return false;
        }));

        Assert.IsNotNull(q.GetLocations().FirstOrDefault(static x =>
        {
            if (x is FakeCmdLineParser y && y.somevalue != null)
            {
                return y.somevalue.Equals(@"C:\\windows\\system32\\");
            }
            return false;
        }));
    }

}
