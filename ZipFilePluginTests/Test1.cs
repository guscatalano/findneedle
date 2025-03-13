using findneedle.Implementations.FileExtensions;

namespace ZipFilePluginTests;

[TestClass]
public sealed class Test1
{
    [TestMethod]
    public void TestMethod1()
    {
        ZipProcessor x = new ZipProcessor();
        var ret = x.RegisterForExtensions();
        Assert.IsTrue(ret.Count() == 1);
        Assert.IsTrue(ret.First().Equals(".zip"));

    }
}
