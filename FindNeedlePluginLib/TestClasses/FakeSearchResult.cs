using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle;

namespace FindNeedlePluginLib.TestClasses;


public class FakeSearchResult : ISearchResult
{
    public DateTime logTime = DateTime.Now;
    public string messageString = "This is fake search result";
    public string searchableDataString = "<xml>fake</xml>";

    public Level GetLevel() => throw new NotImplementedException();
    public DateTime GetLogTime()
    {
        return logTime;
    }
    public string GetMachineName() => throw new NotImplementedException();
    public string GetMessage() {
        return messageString;
    }
    public string GetOpCode() => throw new NotImplementedException();
    public string GetResultSource() => throw new NotImplementedException();
    public string GetSearchableData() {
        return searchableDataString;
    }
    public string GetSource() => throw new NotImplementedException();
    public string GetTaskName() => throw new NotImplementedException();
    public string GetUsername() => throw new NotImplementedException();
    public void WriteToConsole() => throw new NotImplementedException();
}
