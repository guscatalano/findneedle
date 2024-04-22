using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace findneedle.Implementations
{

    public class TimeAgoFilter : SearchFilter
    {
        DateTime start;
        DateTime filterbegin;
        TimeSpan ts = TimeSpan.Zero;
        public TimeAgoFilter(string timespanstr)
        {
            start = DateTime.Now;
            //cast to timespan here maybe?
            timespanstr = timespanstr.ToLower().Trim();
            string num = timespanstr.Replace("h", "").Replace("m", "").Replace("d", "").Replace("s", "");
            int time = Int32.Parse(num);
            int hour = timespanstr.IndexOf("h");
            if (hour > 0)
            {
                ts = new TimeSpan(time, 0, 0);
            }
            int minute = timespanstr.IndexOf("m");
            if (minute > 0)
            {
                ts = new TimeSpan(0, time, 0);
            }

            int second = timespanstr.IndexOf("s");
            if (second > 0)
            {
                ts = new TimeSpan(0, 0, time);
            }

            int day = timespanstr.IndexOf("d");
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

        public bool Filter(SearchResult entry)
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

        public string GetSerializedJson() => throw new NotImplementedException();
    }
}
