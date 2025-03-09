using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle;
using findneedle.Implementations.FileExtensions;


namespace ETWPluginTests;

[TestClass]
public sealed class ETWProcessorTests
{


    [TestMethod]
    public void RegisterCorrectly()
    {
        ETLProcessor x = new ETLProcessor();
        var reg = x.RegisterForExtensions();
        Assert.IsTrue(reg.Count() == 1);
        Assert.IsTrue(reg.First().Equals(".etl"));
        x.CleanUp();
    }

    [TestMethod]
    public void ParseSimple()
    {
        try
        {
            ETLProcessor x = new ETLProcessor();
            x.OpenFile(Path.GetFullPath("SampleFiles\\test.etl"));
            x.DoPreProcessing();
            //x.LoadInMemory();
            List<SearchResult> blah = x.GetResults();
            Assert.IsTrue(blah.Count() > 100);
            x.CleanUp();
            Assert.IsTrue(true);
        }
        catch (Exception e)
        {
            if (e.ToString().Contains("Cant find tracefmt"))
            {
                Assert.Inconclusive("Cant find tracefmt :(");
            }
            else
            {
                Assert.Fail("Should not throw" + e);
            }
        }
    }



    }