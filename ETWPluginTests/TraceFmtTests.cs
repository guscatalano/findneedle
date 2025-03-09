using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle.WDK;
using FindNeedleCoreUtils;

namespace ETWPluginTests;

[TestClass]
public class TraceFmtTests
{

    [TestMethod]
    public void TestBasicLoadPath()
    {
        WDKFinder.TEST_MODE = true;
        WDKFinder.TEST_MODE_SUCCESS = true;
        Assert.IsTrue(WDKFinder.GetTraceFmtPath().Contains("tracefmt.exe"));
        Assert.IsTrue(WDKFinder.GetTraceFmtPath().Contains("x64"));
    }

    [TestMethod]
    public void TestFailure()
    {
        WDKFinder.TEST_MODE = true;
        WDKFinder.TEST_MODE_SUCCESS = false;
        Assert.IsTrue(WDKFinder.GetTraceFmtPath().Equals(WDKFinder.NOT_FOUND_STRING));
    }

    [TestMethod]
    public void TestSummaryFile()
    {
        TraceFmtResult fmtResult = new TraceFmtResult();
        fmtResult.summaryfile = "SampleFiles\\FmtSum.txt";
        fmtResult.ParseSummaryFile();
        Assert.AreEqual(fmtResult.TotalBuffersProcessed, 30);
        Assert.AreEqual(fmtResult.TotalFormatsUnknown, 184696);
        Assert.AreEqual(fmtResult.TotalEventsLost, 1);
        Assert.AreEqual(fmtResult.TotalFormatErrors, 2);
        Assert.IsTrue(fmtResult.TotalElapsedTime != null
            && fmtResult.TotalElapsedTime.Equals("9 sec", StringComparison.CurrentCultureIgnoreCase));
        Assert.IsTrue(fmtResult.ProcessedFile != null
            && fmtResult.ProcessedFile.Equals("C:\\Program Files (x86)\\Windows Kits\\10\\bin\\10.0.19041.0\\x64\\test.etl", StringComparison.CurrentCultureIgnoreCase));

    }

    [TestMethod]
    public void TestETLProcessFile()
    {
        try
        {
            using TempStorage temp = new();
            var path = temp.GetExistingMainTempPath();
            var result = TraceFmt.ParseSimpleETL(Path.GetFullPath("SampleFiles\\test.etl"), path);
            Assert.IsTrue(result.ProcessedFile != null && result.ProcessedFile.Contains("test.etl"));
            Assert.IsTrue(result.TotalBuffersProcessed > 0);
        }
        catch (Exception e)
        {
            if (e.ToString().Contains("Cant find tracefmt"))
            {
                Assert.Inconclusive("Cant find tracefmt");
            }
            else
            {
                Assert.Fail("Should not throw" + e);
            }
        }
    }

    public void TestBadETLPath()
    {
        using TempStorage temp = new();
        var path = temp.GetExistingMainTempPath();
        try
        {
            var result = TraceFmt.ParseSimpleETL("SomeRandomPath", path);
            Assert.Fail("Should throw");
        }
        catch (Exception)
        {
            Assert.IsTrue(true);
        }


    }
}
