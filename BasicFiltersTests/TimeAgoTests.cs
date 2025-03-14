using findneedle;
using findneedle.Implementations;
using FindNeedleCoreUtils;

namespace BasicFiltersTest;

public class FakeSearchResult : ISearchResult
{
    public DateTime logTime = DateTime.Now;

    public Level GetLevel() => throw new NotImplementedException();
    public DateTime GetLogTime()
    {
        return logTime;
    }
    public string GetMachineName() => throw new NotImplementedException();
    public string GetMessage() => throw new NotImplementedException();
    public string GetOpCode() => throw new NotImplementedException();
    public string GetResultSource() => throw new NotImplementedException();
    public string GetSearchableData() => throw new NotImplementedException();
    public string GetSource() => throw new NotImplementedException();
    public string GetTaskName() => throw new NotImplementedException();
    public string GetUsername() => throw new NotImplementedException();
    public void WriteToConsole() => throw new NotImplementedException();
}

[TestClass]
public sealed class TimeAgoTests
{
    [TestMethod]
    public void TimeAgoFilterTestSimple()
    {
        var timeAgoFilter = new TimeAgoFilter(TimeAgoUnit.Hour, 1);
        FakeSearchResult result = new();
        result.logTime = DateTime.Now.AddMinutes(-30);
        Assert.IsTrue(timeAgoFilter.Filter(result));

        result.logTime = DateTime.Now.AddMinutes(-90);
        Assert.IsFalse(timeAgoFilter.Filter(result));
    }
}
