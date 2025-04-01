using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle;

namespace CoreTests;

[TestClass]
public sealed class SearchProgressSinkTests
{
    [TestMethod]
    public void TestBasicSinkNotifications()
    {
        SearchProgressSink sink = new();
        sink.RegisterForTextProgress((string text) => Assert.AreEqual("Test", text));
        sink.RegisterForNumericProgress((int percent) => Assert.AreEqual(50, percent));
        sink.NotifyProgress(50, "Test");
    }

    [TestMethod]
    public void TestOtherBasicSinkNotifications()
    {
        SearchProgressSink sink = new();
        sink.RegisterForTextProgress((string text) => Assert.AreEqual("winning", text));
        sink.NotifyProgress("winning");
    }

    [TestMethod]
    public void TestMultipleSinkNotifications()
    {
        var tcount = 0;
        var ncount = 0;
        SearchProgressSink sink = new();
        sink.RegisterForTextProgress((string text) => tcount++);
        sink.RegisterForTextProgress((string text) => tcount++);
        sink.RegisterForNumericProgress((int percent) => ncount++);
        sink.RegisterForNumericProgress((int percent) => ncount++);

        sink.NotifyProgress(50, "Test");

        //We subscribed twice, so we should up by 2
        Assert.AreEqual(tcount, 2);
        Assert.AreEqual(ncount, 2);
        sink.NotifyProgress(100, "done");
        Assert.AreEqual(tcount, 4);
        Assert.AreEqual(ncount, 4);
    }
}
