using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FindNeedleCoreUtils;
using FindNeedlePluginLib.Interfaces;

namespace findneedle.Implementations;

public class TimeRangeFilter : ISearchFilter, IPluginDescription, ICommandLineParser
{
    public DateTime start;
    public DateTime end;

    public TimeRangeFilter()
    {
        this.start = DateTime.Now;
        this.end = DateTime.Now;
    }


    public bool Filter(ISearchResult entry)
    {
        if (start < entry.GetLogTime() && end > entry.GetLogTime())
        {
            return true;
        }
        return false;
    }

    public string GetDescription()
    {
        return "TimeRange";
    }
    public string GetName()
    {
        return "Start: " + start.ToString() + " and End: " + end.ToString();
    }

    public string GetPluginTextDescription()
    {
        return "Filters the search results by a time range";
    }
    public string GetPluginFriendlyName()
    {
        return "Time Range Filter";
    }
    public string GetPluginClassName()
    {
        return GetType().FullName ?? string.Empty;
    }

    public CommandLineRegistration RegisterCommandHandler()
    {
        return new CommandLineRegistration()
        {
            handlerType = CommandLineHandlerType.Filter,
            key = "time"
        };
    }
    public void ParseCommandParameterIntoQuery(string parameter)
    {
        var splitted = TextManipulation.SplitApart(parameter);
        var firstdate = splitted[0];
        var seconddate = splitted[1];
        start = DateTime.Parse(firstdate);
        end = DateTime.Parse(seconddate);
    }
}
