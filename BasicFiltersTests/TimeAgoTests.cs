using findneedle;
using findneedle.Implementations;
using FindNeedleCoreUtils;
using FindNeedlePluginLib.Interfaces;
using FindNeedlePluginLib.TestClasses;

namespace BasicFiltersTest;

[TestClass]
public sealed class TimeAgoTests
{
    [TestMethod]
    public void TimeAgoFilterTestSimpleHour()
    {
        var timeAgoFilter = new TimeAgoFilter(TimeAgoUnit.Hour, 1);
        FakeSearchResult result = new();
        result.logTime = DateTime.Now.AddMinutes(-30);
        Assert.IsTrue(timeAgoFilter.Filter(result));

        result.logTime = DateTime.Now.AddMinutes(-90);
        Assert.IsFalse(timeAgoFilter.Filter(result));
    }

    [TestMethod]
    public void TimeAgoFilterTestSimpleDays()
    {
        var timeAgoFilter = new TimeAgoFilter(TimeAgoUnit.Day, 1);
        FakeSearchResult result = new();
        result.logTime = DateTime.Now.AddHours(-10);
        Assert.IsTrue(timeAgoFilter.Filter(result));

        result.logTime = DateTime.Now.AddHours(-90);
        Assert.IsFalse(timeAgoFilter.Filter(result));
    }

    [TestMethod]
    public void TimeAgoFilterTestSimpleMinutes()
    {
        var timeAgoFilter = new TimeAgoFilter(TimeAgoUnit.Minute, 1);
        FakeSearchResult result = new();
        result.logTime = DateTime.Now.AddMinutes(10);
        Assert.IsTrue(timeAgoFilter.Filter(result));

        result.logTime = DateTime.Now.AddMinutes(-90);
        Assert.IsFalse(timeAgoFilter.Filter(result));
    }

    [TestMethod]
    public void TimeAgoFilterTestSimpleSeconds()
    {
        var timeAgoFilter = new TimeAgoFilter(TimeAgoUnit.Second, 1);
        FakeSearchResult result = new();
        result.logTime = DateTime.Now.AddSeconds(10);
        Assert.IsTrue(timeAgoFilter.Filter(result));

        result.logTime = DateTime.Now.AddSeconds(-90);
        Assert.IsFalse(timeAgoFilter.Filter(result));
    }

    


   [TestMethod]
   public void TestTimeAgoFilter()
   {
        const string TEST_STRING = "2h";
        TimeAgoFilter filter = new();
        var reg = filter.RegisterCommandHandler();
        Assert.AreEqual(reg.handlerType, CommandLineHandlerType.Filter);
        Assert.IsTrue(reg.key.Equals("timeago"));
        try
        {
            filter.ParseCommandParameterIntoQuery(TEST_STRING);
        }
        catch (Exception e)
        {
            Assert.Fail(e.ToString());
        }

        DateTime correctFilter = DateTime.Now.AddHours(-2);
        TimeSpan diff = filter.filterbegin.Subtract(correctFilter);
        Assert.AreEqual(diff.Hours, 0);
        Assert.AreEqual(diff.Days, 0);
        var toleratedTimeDiffInSeconds = 30;
        Assert.IsTrue(diff.Seconds > (-1 * toleratedTimeDiffInSeconds) || diff.Seconds < toleratedTimeDiffInSeconds); //buffer around 

    }


}
