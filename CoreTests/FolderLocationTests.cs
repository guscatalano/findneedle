using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle.Implementations;
using findneedle.PluginSubsystem;
using FindNeedlePluginLib;
using FindNeedlePluginLib.TestClasses;
using FindPluginCore.PluginSubsystem;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestProcessorPlugin;

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


    [TestMethod]
    public void TestHandlingExtensions()
    {
        // FolderLocation treats the registered processors as TEMPLATES and processes each real file
        // with its own fresh clone (so multiple files never share one processor's per-file state). So
        // we verify the OUTCOME — the .txt file got processed — rather than the passed instance's flags.
        var dir = Path.Combine(Path.GetTempPath(), "FN_floc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var TEST_FILE = Path.Combine(dir, "fakefile.txt");
        File.WriteAllText(TEST_FILE, "hello");
        try
        {
            FolderLocation loc = new();
            loc.ParseCommandParameterIntoQuery(TEST_FILE);

            var sampleFileExtensionProcessor = new SampleFileExtensionProcessor();
            loc.SetExtensionProcessorList(new List<IFileExtensionProcessor> { sampleFileExtensionProcessor });
            loc.LoadInMemory();

            // The per-file clone (not the registered template) processed the file: SampleFileExtension-
            // Processor.GetResults() returns 2 results per handled .txt file.
            Assert.AreEqual(2, loc.Search().Count);
            // The registered instance is a template and is left untouched.
            Assert.IsFalse(sampleFileExtensionProcessor.hasLoaded);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [TestMethod]
    public void TestSkipHandlingExtensions()
    {
        var TEST_FILE = "FakeFolder\\somethingelse.json";
        FolderLocation loc = new();
        loc.ParseCommandParameterIntoQuery(TEST_FILE);

        //We know its the only one
        SampleFileExtensionProcessor sampleFileExtensionProcessor = new();
        List<IFileExtensionProcessor> processors = new();
        processors.Add(sampleFileExtensionProcessor);

        Assert.IsFalse(sampleFileExtensionProcessor.hasDonePreProcessing);
        Assert.IsFalse(sampleFileExtensionProcessor.hasLoaded);

        //The sample processor does not handle .json
        loc.SetExtensionProcessorList(processors);
        loc.LoadInMemory();

        Assert.IsFalse(sampleFileExtensionProcessor.hasDonePreProcessing);
        Assert.IsFalse(sampleFileExtensionProcessor.hasLoaded);

    }

    [TestMethod]
    public void TestStatistics()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FN_floc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var TEST_FILE = Path.Combine(dir, "fakefile.txt");
        File.WriteAllText(TEST_FILE, "hello");
        try
        {
            FolderLocation loc = new();
            loc.ParseCommandParameterIntoQuery(TEST_FILE);

            loc.SetExtensionProcessorList(new List<IFileExtensionProcessor> { new SampleFileExtensionProcessor() });
            loc.LoadInMemory();
            var result = loc.ReportStatistics();

            // Both expected stat reports are present (exact count isn't pinned — a per-file stats
            // report is also emitted now that a real file is processed).
            Assert.IsTrue(result.Count >= 2);
            Assert.IsTrue(result.FirstOrDefault(x => x.summary.Equals("ExtensionProviders")) != null);
            Assert.IsTrue(result.FirstOrDefault(x => x.summary.Equals("ProviderByFile")) != null);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [TestMethod]
    public void TestSearch()
    {
        var TEST_FILE = "FakeFolder\\fakefile.txt";
        FolderLocation loc = new();
        loc.ParseCommandParameterIntoQuery(TEST_FILE);

        //We know its the only one
        SampleFileExtensionProcessor sampleFileExtensionProcessor = new();
        List<IFileExtensionProcessor> processors = new();
        processors.Add(sampleFileExtensionProcessor);

        loc.SetExtensionProcessorList(processors);
        loc.LoadInMemory();
        var results = loc.Search();
        Assert.AreEqual(results.Count, 2);
    }
}
