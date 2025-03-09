using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle.Implementations.FileExtensions;


namespace CoreTests;

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
    }


}