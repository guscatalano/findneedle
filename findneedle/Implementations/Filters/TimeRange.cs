using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace findneedle.Implementations
{
    public class TimeRangeFilter : SearchFilter
    {
        private DateTime start;
        private DateTime end;
        public TimeRangeFilter(DateTime start, DateTime end)
        {
            this.start = start;
            this.end = end;
        }

        public bool Filter(SearchResult entry)
        {
            return true;
        }

        public string GetDescription()
        {
            return "TimeRange";
        }
        public string GetName()
        {
            return ":(";
        }
    }
}
