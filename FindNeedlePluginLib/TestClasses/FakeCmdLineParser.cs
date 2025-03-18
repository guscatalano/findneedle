using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle;
using findneedle.Interfaces;
using FindNeedlePluginLib.Interfaces;

namespace FindNeedlePluginLib.TestClasses;

public class FakeCmdLineParser : ISearchLocation, ICommandLineParser, ISearchFilter, IResultProcessor
{
    public bool wasParseCalled = false;
    public Action<string>? callbackForParse;
    public void ParseCommandParameterIntoQuery(string parameter) 
    {
        wasParseCalled = true;
        if (callbackForParse != null)
        {
            callbackForParse(parameter);
        }
    }

    public CommandLineRegistration reg = new()
    {
        handlerType = CommandLineHandlerType.Filter,
        key = "something"
    };
    public CommandLineRegistration RegisterCommandHandler()
    {
        return reg;
    }

    //Do not implement any below, this is just for the casting to work
    public bool Filter(ISearchResult entry) => throw new NotImplementedException();
    public override void LoadInMemory() => throw new NotImplementedException();
    public override List<ISearchResult> Search(ISearchQuery? searchQuery) => throw new NotImplementedException();
    public override void SetNotificationCallback(SearchProgressSink sink) => throw new NotImplementedException();
    public override void SetSearchStatistics(SearchStatistics stats) => throw new NotImplementedException();
    public override string GetDescription() => throw new NotImplementedException();
    public override string GetName() => throw new NotImplementedException();
    public void ProcessResults(List<ISearchResult> results) => throw new NotImplementedException();
    public string GetOutputFile(string optionalOutputFolder = "") => throw new NotImplementedException();
    public string GetOutputText() => throw new NotImplementedException();
    public string GetTextDescription() => throw new NotImplementedException();
    public string GetFriendlyName() => throw new NotImplementedException();
    public string GetClassName() => throw new NotImplementedException();
}
