using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle.Implementations;
using findneedle.Interfaces;
using findneedle.PluginSubsystem;
using FindNeedlePluginLib.Interfaces;
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
        var TEST_FILE = "FakeFolder\\fakefile.txt";
        FolderLocation loc = new();
        loc.ParseCommandParameterIntoQuery(TEST_FILE);

        //We know its the only one
        SampleFileExtensionProcessor sampleFileExtensionProcessor = new();
        List<IFileExtensionProcessor> processors = new();
        processors.Add(sampleFileExtensionProcessor);

        Assert.IsFalse(sampleFileExtensionProcessor.hasDonePreProcessing);
        Assert.IsFalse(sampleFileExtensionProcessor.hasLoaded);


        loc.SetExtensionProcessorList(processors);
        loc.LoadInMemory();

        Assert.IsTrue(sampleFileExtensionProcessor.hasDonePreProcessing);
        Assert.IsTrue(sampleFileExtensionProcessor.hasLoaded);
        Assert.AreEqual(sampleFileExtensionProcessor.lastOpenedFile, TEST_FILE);

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
        var TEST_FILE = "FakeFolder\\fakefile.txt";
        FolderLocation loc = new();
        loc.ParseCommandParameterIntoQuery(TEST_FILE);

        //We know its the only one
        SampleFileExtensionProcessor sampleFileExtensionProcessor = new();
        List<IFileExtensionProcessor> processors = new();
        processors.Add(sampleFileExtensionProcessor);

        loc.SetExtensionProcessorList(processors);
        loc.LoadInMemory();
        var result = loc.ReportStatistics();

        Assert.AreEqual(result.Count, 2);
        Assert.IsTrue(result.FirstOrDefault( x => x.summary.Equals("ExtensionProviders")) != null);
        Assert.IsTrue(result.FirstOrDefault(x => x.summary.Equals("ProviderByFile")) != null);
        //Expand later
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
        var results = loc.Search(new FakeSearchQuery());
        Assert.AreEqual(results.Count, 2);
    }
}
