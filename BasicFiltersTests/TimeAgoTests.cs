using findneedle.Implementations;
using FindNeedleCoreUtils;

namespace BasicFiltersTest;

[TestClass]
public sealed class TimeAgoTests
{
    [TestMethod]
    public void TestMethod1()
    {
        var timeAgoFilter = new TimeAgoFilter(TimeAgoUnit.Hour, 1);
        var result = timeAgoFilter.start;
        Assert.AreEqual(DateTime.Now, result);
    }
}
