using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle;
using findneedle.Implementations;

namespace EventLogPluginTests;

[TestClass]
public class LocalEventLogTests
{
    [TestMethod]
    public void LocalEventLog_Query_ForApplication()
    {
        var query = new LocalEventLogQueryLocation("Application");
        query.LoadInMemory();
        List<ISearchResult> ret = query.Search(null);
        Assert.IsTrue(ret.Count() > 0);
        Assert.AreEqual(ret.First().GetResultSource(), "LocalEventLogRecord-Application");
    }

    [TestMethod]
    public void LocalEventLogForApplication()
    {
        var query = new LocalEventLogLocation("Application");
        query.LoadInMemory();
        List<ISearchResult> ret = query.Search(null);
        Assert.IsTrue(ret.Count() > 0);
        Assert.AreEqual(ret.First().GetResultSource(), "LocalEventLog-Application");
    }

    [TestMethod]
    public void LocalEventLogForEverything()
    {
        var query = new LocalEventLogLocation("everything");
        query.LoadInMemory();
        List<ISearchResult> ret = query.Search(null);
        Assert.IsTrue(ret.Count() > 0);
        Assert.AreEqual(ret.First().GetResultSource(), "LocalEventLog-everything");
    }
}
