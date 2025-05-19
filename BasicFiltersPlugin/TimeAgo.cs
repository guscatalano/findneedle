using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FindNeedleCoreUtils;
using FindNeedlePluginLib.Interfaces;

namespace findneedle.Implementations;




public class TimeAgoFilter : ISearchFilter, ICommandLineParser, IPluginDescription
{
    public void Clone(ICommandLineParser parser)
    {
        //Keep nothing
    }
    public DateTime start
    {
        get; set;
    }
    public DateTime filterbegin
    {
        get; set;
    }
    public TimeSpan ts
    {
        get; set;
    }

    public TimeAgoFilter(TimeAgoUnit unit, int time)
    {

        ts = TimeSpan.Zero;
        start = DateTime.Now;
        
        if ( unit == TimeAgoUnit.Hour)
        {
            ts = new TimeSpan(time, 0, 0);
        }
        if (unit == TimeAgoUnit.Minute)
        {
            ts = new TimeSpan(0, time, 0);
        }

        if (unit == TimeAgoUnit.Second)
        {
            ts = new TimeSpan(0, 0, time);
        }

        if (unit == TimeAgoUnit.Day)
        {
            ts = new TimeSpan(time, 0, 0, 0);
        }
        if (ts == TimeSpan.Zero)
        {
            throw new Exception("Failed to parse timespan");
        }
        filterbegin = start - ts;
    }

    public TimeAgoFilter()
    {
        //Dont initialize on purpose
    }

    public TimeAgoFilter(string timespanstr)
    {
        ParseFromString(timespanstr);
    }

    public void ParseFromString(string timespanstr)
    {
        ts = TimeSpan.Zero;
        start = DateTime.Now;
        //cast to timespan here maybe?
        timespanstr = timespanstr.ToLower().Trim();
        var num = timespanstr.Replace("h", "").Replace("m", "").Replace("d", "").Replace("s", "");
        var time = Int32.Parse(num);
        var hour = timespanstr.IndexOf('h');
        if (hour > 0)
        {
            ts = new TimeSpan(time, 0, 0);
        }
        var minute = timespanstr.IndexOf('m');
        if (minute > 0)
        {
            ts = new TimeSpan(0, time, 0);
        }

        var second = timespanstr.IndexOf('s');
        if (second > 0)
        {
            ts = new TimeSpan(0, 0, time);
        }

        var day = timespanstr.IndexOf('d');
        if (day > 0)
        {
            ts = new TimeSpan(time, 0, 0, 0);
        }
        if (ts == TimeSpan.Zero)
        {
            throw new Exception("Failed to parse timespan");
        }
        filterbegin = start - ts;
    }


    public bool Filter(ISearchResult entry)
    {
        if (filterbegin < entry.GetLogTime())
        {
            return true;
        }
        return false;
    }

    public string GetDescription()
    {
        return "TimeAgo";
    }
    public string GetName()
    {
        return ":(";
    }

    public CommandLineRegistration RegisterCommandHandler() 
    {
        var reg = new CommandLineRegistration()
        {
            handlerType = CommandLineHandlerType.Filter,
            key = "timeago"
        };
        return reg;
    }
    public void ParseCommandParameterIntoQuery(string parameter) 
    {
        ParseFromString(parameter);   
    }

    public string GetPluginTextDescription()
    {
        return "Filters the search results by a time range from now to the past";
    }
    public string GetPluginFriendlyName()
    {
        return "Time Ago Filter";
    }
    public string GetPluginClassName()
    {
        return IPluginDescription.GetPluginClassNameBase(this);
    }
}
