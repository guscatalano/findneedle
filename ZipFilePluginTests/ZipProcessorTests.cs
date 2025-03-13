using findneedle.Implementations.FileExtensions;

namespace ZipFilePluginTests;

[TestClass]
public sealed class ZipProcessorTests
{
    [TestMethod]
    public void RegisterExtensionTest()
    {
        ZipProcessor x = new ZipProcessor();
        var ret = x.RegisterForExtensions();
        Assert.IsTrue(ret.Count() == 1);
        Assert.IsTrue(ret.First().Equals(".zip"));

    }

    [TestMethod]
    public void BasicUnzipTest()
    {
        var called = false;
        using ZipProcessor x = new ZipProcessor();
        
        x.OpenFile(Path.GetFullPath("SampleFiles\\susp_explorer_exec.zip"));
        x.RegisterForQueueNewFolderCallback(new Action<string>((string folder) => { 
            called = true;  
            Assert.IsTrue(Directory.Exists(Path.GetFullPath(folder)));
            Assert.IsTrue(File.Exists(Path.GetFullPath(Path.Combine(folder, "susp_explorer_exec.evtx"))));  
        }));
        x.DoPreProcessing();
        Assert.IsTrue(called);
    }

    [TestMethod]
    public void BasicUnzipCleanupTest()
    {
        var called = false;
        var newPath = "";
        using (ZipProcessor x = new ZipProcessor())
        {
            x.OpenFile(Path.GetFullPath("SampleFiles\\susp_explorer_exec.zip"));
            x.RegisterForQueueNewFolderCallback(new Action<string>((string folder) =>
            {
                called = true;
                newPath = Path.GetFullPath(folder);

            }));
            x.DoPreProcessing();
            Assert.IsTrue(called);
        }
        Assert.IsFalse(Directory.Exists(Path.GetFullPath(newPath)));
    }
}
