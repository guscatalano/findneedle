using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle;
using findneedle.Interfaces;
using FindNeedlePluginLib.Interfaces;

namespace FindNeedlePluginLib.TestClasses;

[ExcludeFromCodeCoverage]
public class FakeCmdLineParser : ISearchLocation, ICommandLineParser, ISearchFilter, IResultProcessor
{
    //When you need to store and reference something, use somevalue
    public string? somevalue
    {
        get; set;
    }

    public void Clone(ICommandLineParser parser)
    {
        if (parser is FakeCmdLineParser p)
        {
            somevalue = p.somevalue;
            wasParseCalled = p.wasParseCalled;
            callbackForParse = p.callbackForParse;
        }
    }

    public bool wasParseCalled = false;
    public Action<string>? callbackForParse;
    public void ParseCommandParameterIntoQuery(string parameter) 
    {
        wasParseCalled = true;
        if (callbackForParse != null)
        {
            callbackForParse(parameter);
        } else
        {
            somevalue = parameter;
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
    public override string GetDescription() => throw new NotImplementedException();
    public override string GetName() => throw new NotImplementedException();
    public void ProcessResults(List<ISearchResult> results) => throw new NotImplementedException();
    public string GetOutputFile(string optionalOutputFolder = "") => throw new NotImplementedException();
    public string GetOutputText() => throw new NotImplementedException();
    public string GetTextDescription() => throw new NotImplementedException();
    public string GetFriendlyName() => throw new NotImplementedException();
    public string GetClassName() => throw new NotImplementedException();
    public override void ClearStatistics() => throw new NotImplementedException();
    public override List<ReportFromComponent> ReportStatistics() => throw new NotImplementedException();
}
