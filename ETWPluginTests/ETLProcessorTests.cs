using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle;
using findneedle.Implementations.FileExtensions;
using findneedle.WDK;


namespace ETWPluginTests;

[TestClass]
public sealed class ETWProcessorTests
{

    [TestInitialize]
    public void Init()
    {
        WDKFinder.ResetTestFlags();
    }

    [TestMethod]
    public void RegisterCorrectly()
    {
        ETLProcessor x = new ETLProcessor();
        var reg = x.RegisterForExtensions();
        Assert.IsTrue(reg.Count() == 1);
        Assert.IsTrue(reg.First().Equals(".etl"));
        x.Dispose();
    }

    [TestMethod]
    public void ParseSimple()
    {
        ETWTestUtils.UseTestTraceFmt();
        try
        {
            ETLProcessor x = new ETLProcessor();
            x.OpenFile(ETWTestUtils.GetSampleETLFile());
            x.DoPreProcessing();
            //x.LoadInMemory();
            List<ISearchResult> blah = x.GetResults();
            Assert.IsTrue(blah.Count() > 100);
            x.Dispose();
            Assert.IsTrue(true);
        }
        catch (Exception e)
        {
            if (e.ToString().Contains("Cant find tracefmt"))
            {
                Assert.Fail("Cant find tracefmt :("); //We have a testhook for it now
            }
            else
            {
                Assert.Fail("Should not throw" + e);
            }
        }
    }



    }