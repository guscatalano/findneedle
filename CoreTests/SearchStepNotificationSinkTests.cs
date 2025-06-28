using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle;
using FindNeedlePluginLib;

namespace CoreTests;

[TestClass]
public sealed class SearchStepNotificationSinkTests
{
    [TestMethod]
    public void TestBasicSinkNotifications()
    {
        SearchStepNotificationSink sink = new();
        sink.RegisterForStepNotification((SearchStep x) => Assert.AreEqual(x, SearchStep.AtSearch));
        sink.NotifyStep(SearchStep.AtSearch);
    }

    [TestMethod]
    public void TestMultipleSinkNotifications()
    {

        var ncount = 0;
        SearchStepNotificationSink sink = new();
        sink.RegisterForStepNotification((SearchStep x) => ncount++);
        sink.RegisterForStepNotification((SearchStep x) => ncount++);

        sink.NotifyStep(SearchStep.AtLaunch);

        //We subscribed twice, so we should up by 2
        Assert.AreEqual(ncount, 2);
        sink.NotifyStep(SearchStep.AtLaunch);
        Assert.AreEqual(ncount, 4);
    }
}
