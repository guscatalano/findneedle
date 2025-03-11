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
}
