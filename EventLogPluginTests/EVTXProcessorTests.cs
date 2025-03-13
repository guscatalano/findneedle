using findneedle.Implementations.FileExtensions;

namespace EventLogPluginTests;

[TestClass]
public sealed class EVTXProcessorTests
{
    [TestMethod]
    public void TestRegister()
    {
        EVTXProcessor x = new EVTXProcessor();
        Assert.IsTrue(x.RegisterForExtensions().Count() == 1);
        Assert.IsTrue(x.RegisterForExtensions().First().Equals(".evtx"));
    }

    [TestMethod]
    public void TestGetFileName()
    {
        EVTXProcessor x = new EVTXProcessor();
        var testFileName = "test.evtx";
        x.OpenFile(testFileName);
        Assert.AreEqual(testFileName, x.GetFileName());
    }

    [TestMethod]
    public void TestGetProviderCountBasic()
    {
        EVTXProcessor x = new EVTXProcessor();
        var providerCount = x.GetProviderCount();
        Assert.IsNotNull(providerCount);
        Assert.AreEqual(0, providerCount.Count);
    }

    [TestMethod]
    public void TestGetResultsFromSampleFile()
    {
        EVTXProcessor x = new EVTXProcessor();
        
        x.OpenFile("SampleFiles\\susp_explorer_exec.evtx");
        x.LoadInMemory();
        var results = x.GetResults();
        Assert.IsNotNull(results);
        Assert.IsTrue(results.Count == 3);
        
        var firstOne = results.First();
        Assert.AreEqual(firstOne.GetSource(), "Microsoft-Windows-Sysmon");
        Assert.AreEqual(firstOne.GetMachineName(), "MSEDGEWIN10");
        Assert.AreEqual(firstOne.GetLevel(), findneedle.Level.Verbose);
        Assert.AreEqual(firstOne.GetResultSource(), "LocalEventLogRecord-SampleFiles\\susp_explorer_exec.evtx");
        Assert.AreEqual(firstOne.GetUsername(), "NT AUTHORITY\\SYSTEM");
        //removed check for time cause its timezone dependent
        Assert.IsTrue(firstOne.GetMessage().Contains("<Data Name='CommandLine'>\"C:\\windows\\explorer.exe\" shell:::")); //shows up in message and searchable data
        Assert.IsTrue(firstOne.GetSearchableData().Contains("<Provider Name='Microsoft-Windows-Sysmon'")); //only shows up in searchable data

    }
}
