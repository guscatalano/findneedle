using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle;
using findneedle.Implementations;
using FindNeedlePluginLib.Interfaces;

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
        var ret = query.Search(null);
        Assert.IsTrue(ret.Count() > 0);
        Assert.AreEqual(ret.First().GetResultSource(), "LocalEventLog-everything");
    }

    [TestMethod]
    public void LocalEventLogCmdLineTest()
    {
        
        LocalEventLogLocation loc = new();
        var reg = loc.RegisterCommandHandler();
        Assert.AreEqual(reg.handlerType, CommandLineHandlerType.Location);
        Assert.IsTrue(reg.key.Equals("eventlog"));
        try
        {
            loc.ParseCommandParameterIntoQuery("everything");
        }
        catch (Exception e)
        {
            Assert.Fail(e.ToString());
        }
        Assert.IsTrue(loc.eventLogName.Equals("everything"));

    }

    [TestMethod]
    public void LocalEventLogCmdLineEmptyArgTest()
    {

        LocalEventLogLocation loc = new();
        var reg = loc.RegisterCommandHandler();
        Assert.AreEqual(reg.handlerType, CommandLineHandlerType.Location);
        Assert.IsTrue(reg.key.Equals("eventlog"));
        try
        {
            loc.ParseCommandParameterIntoQuery("");
        }
        catch (Exception e)
        {
            Assert.Fail(e.ToString());
        }
        Assert.IsTrue(loc.eventLogName.Equals("everything"));

    }

    [TestMethod]
    public void LocalEventLogCmdLineSomethingElseArgTest()
    {

        LocalEventLogLocation loc = new();
        var reg = loc.RegisterCommandHandler();
        Assert.AreEqual(reg.handlerType, CommandLineHandlerType.Location);
        Assert.IsTrue(reg.key.Equals("eventlog"));
        try
        {
            loc.ParseCommandParameterIntoQuery("blah");
        }
        catch (Exception e)
        {
            Assert.Fail(e.ToString());
        }
        Assert.IsTrue(loc.eventLogName.Equals("blah"));

    }
}
