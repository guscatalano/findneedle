﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FindNeedleCoreUtils;

namespace findneedle.Implementations;




public class TimeAgoFilter : ISearchFilter
{

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


    public TimeAgoFilter(string timespanstr)
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

    public string SearchFilterType => throw new NotImplementedException();

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

    
}
