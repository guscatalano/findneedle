using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle;
using findneedle.Implementations;
using FindNeedlePluginLib;

namespace EventLogPluginTests;

[TestClass]
public class LocalEventLogTests
{
    [TestMethod]
    public void LocalEventLog_Query_ForApplication()
    {
        var query = new LocalEventLogQueryLocation("Application");
        query.LoadInMemory();
        List<ISearchResult> ret = query.Search();
        Assert.IsTrue(ret.Count() > 0);
        Assert.AreEqual(ret.First().GetResultSource(), "LocalEventLogRecord-Application");
    }

    [TestMethod]
    public void LocalEventLogForApplication()
    {
        var query = new LocalEventLogLocation("Application");
        query.LoadInMemory();
        List<ISearchResult> ret = query.Search();
        Assert.IsTrue(ret.Count() > 0);
        Assert.AreEqual(ret.First().GetResultSource(), "LocalEventLog-Application");
    }

    [TestMethod]
    public void LocalEventLogForEverything()
    {
        var query = new LocalEventLogLocation("everything");
        query.LoadInMemory();
        var ret = query.Search();
        Assert.IsTrue(ret.Count() > 0);
        Assert.AreEqual(ret.First().GetResultSource(), "LocalEventLog-everything");
    }

    /// <summary>The "Add Live Event Log → everything" flow the UI uses (LocalEventLogQueryLocation):
    /// load across every channel on this machine and confirm records come back. The load is bounded by
    /// a cancellation timeout — unbounded "everything" reads every channel's entire history into memory
    /// and can OOM (it crashed the test host). Live + machine/permission-dependent, so excluded from CI
    /// (TestCategory=SkipCI) — runs locally on demand.</summary>
    [TestMethod]
    [TestCategory("SkipCI")]
    public void LocalEventLogQuery_Everything_LoadsResults()
    {
        var query = new LocalEventLogQueryLocation("everything");
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
        query.LoadInMemory(cts.Token);  // honors the token per record/channel, so it stops bounded
        List<ISearchResult> ret = query.Search();
        Assert.IsTrue(ret.Count > 0, "the live 'everything' event log query should return records");
        Assert.IsFalse(string.IsNullOrEmpty(ret.First().GetResultSource()));
    }

    /// <summary>Same bounded "everything" load, drained through the batched callback path the search
    /// engine actually uses, to prove the results stream out. SkipCI for the same reasons.</summary>
    [TestMethod]
    [TestCategory("SkipCI")]
    public async Task LocalEventLogQuery_Everything_LoadsViaCallback()
    {
        var query = new LocalEventLogQueryLocation("everything");
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
        query.LoadInMemory(cts.Token);
        int total = 0;
        await query.SearchWithCallback(batch => total += batch.Count);
        Assert.IsTrue(total > 0, "batched callback should yield records for 'everything'");
    }

    [TestMethod]
    public void LocalEventLogCmdLineTest()
    {
        
        LocalEventLogLocation loc = new();
        var reg = loc.RegisterCommandHandler();
        Assert.AreEqual(reg.handlerType, CommandLineHandlerType.Location);
        Assert.IsTrue(reg.key.Equals("eventlogentry"));
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
        Assert.IsTrue(reg.key.Equals("eventlogentry"));
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
        Assert.IsTrue(reg.key.Equals("eventlogentry"));
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
