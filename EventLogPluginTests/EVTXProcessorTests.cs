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
}
