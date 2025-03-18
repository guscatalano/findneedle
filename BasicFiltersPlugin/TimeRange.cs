using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace findneedle.Implementations;

public class TimeRangeFilter : ISearchFilter
{
    private readonly DateTime start;
    private readonly DateTime end;
    public TimeRangeFilter(DateTime start, DateTime end)
    {
        this.start = start;
        this.end = end;
    }

    public string SearchFilterType => throw new NotImplementedException();

    public bool Filter(ISearchResult entry)
    {
        return true;
    }

    public string GetDescription()
    {
        return "TimeRange";
    }
    public string GetName()
    {
        return "Start: " + start.ToString() + " and End: " + end.ToString();
    }
    /* For commandline parser
     * 
     * if (pair.Value.StartsWith("time"))
            {
                var par = pair.Value.Substring(4);
                List<string> x = TextManipulation.SplitApart(par);
                DateTime start = DateTime.Parse(x[0]);
                DateTime end = DateTime.Parse(x[1]);
                //filters.Add(new TimeRangeFilter(start, end));
            }*/
   
}
