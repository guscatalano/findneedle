﻿using System.Diagnostics;
using FindNeedlePluginLib.Interfaces;

namespace findneedle;


public class MemorySnapshot
{
    static readonly string[] SizeSuffixes =
               { "bytes", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };

    static string SizeSuffix(long value, int decimalPlaces = 1)
    {
        if (decimalPlaces < 0) { throw new ArgumentOutOfRangeException("decimalPlaces"); }
        if (value < 0) { return "-" + SizeSuffix(-value, decimalPlaces); }
        if (value == 0) { return string.Format("{0:n" + decimalPlaces + "} bytes", 0); }

        // mag is 0 for bytes, 1 for KB, 2, for MB, etc.
        var mag = (int)Math.Log(value, 1024);

        // 1L << (mag * 10) == 2 ^ (10 * mag) 
        // [i.e. the number of bytes in the unit corresponding to mag]
        var adjustedSize = (decimal)value / (1L << (mag * 10));

        // make adjustment when the value is large enough that
        // it would round up to 1000 or more
        if (Math.Round(adjustedSize, decimalPlaces) >= 1000)
        {
            mag += 1;
            adjustedSize /= 1024;
        }

        return string.Format("{0:n" + decimalPlaces + "} {1}",
            adjustedSize,
            SizeSuffixes[mag]);
    }

    long privatememory = 0;
    long gcmemory = 0;
    DateTime when;
    readonly Process p;
    public MemorySnapshot(Process p)
    {
        this.p = p;
    }

    public void Snap()
    {
        when = DateTime.Now;
        p.Refresh();
        privatememory = p.PrivateMemorySize64;
        gcmemory = GC.GetTotalMemory(false);
    }

    public string GetMemoryUsage()
    {
        return " PrivateMemory (" + SizeSuffix(privatememory) + ") / GC Memory (" + SizeSuffix(gcmemory) + ").";
    }

    public DateTime GetSnapTime()
    {
        return when;
    }

}

public enum SearchStatisticStep
{
    AtLoad,
    AtSearch,
    AtLaunch,
    Total
}

public class ReportFromComponent
{
    public string component = string.Empty;
    public SearchStatisticStep step = SearchStatisticStep.Total;
    public string summary = string.Empty;
    public Dictionary<string, dynamic> metric = new();
}

public class SearchStatistics
{
    readonly ISearchQuery q;
    readonly Process proc;
    public SearchStatistics(ISearchQuery query)
    {
        q = query;
        proc = Process.GetCurrentProcess();
        atLoad = new MemorySnapshot(proc);
        atSearch = new MemorySnapshot(proc);
        atLaunch = new MemorySnapshot(proc);
        atLaunch.Snap();
    }

    int totalRecordsSearch = 0;
    int totalRecordsLoaded = 0;
    readonly MemorySnapshot atLaunch;
    readonly MemorySnapshot atLoad;
    readonly MemorySnapshot atSearch;



    public Dictionary<SearchStatisticStep, List<ReportFromComponent>> componentReports = new();
    

    public void ReportFromComponent(ReportFromComponent data)
    {
        if (!componentReports.ContainsKey(data.step))
        {
            componentReports.Add(data.step, new List<ReportFromComponent>());
        }
        componentReports[data.step].Add(data);
        
    }

    public void LoadedAll()
    {
        totalRecordsLoaded = 0;
        foreach (ISearchLocation loc in q.GetLocations())
        {
            totalRecordsLoaded += loc.numRecordsInMemory;
        }

        atLoad.Snap();
    }

    public void Searched()
    {
        totalRecordsSearch = 0;
        foreach (ISearchLocation loc in q.GetLocations())
        {
            totalRecordsSearch += loc.numRecordsInLastResult;
        }
        atSearch.Snap();
    }

    public int GetRecordsAtStep(SearchStatisticStep step)
    {
        switch (step)
        {
            case SearchStatisticStep.AtLoad:
                return totalRecordsLoaded;
            case SearchStatisticStep.AtSearch:
                return totalRecordsSearch;
            default:
                throw new Exception("bad input");
        }
    }

    public TimeSpan GetTimeTaken(SearchStatisticStep step)
    {
        switch (step)
        {
            case SearchStatisticStep.AtLoad:
                return atLoad.GetSnapTime() - atLaunch.GetSnapTime();
            case SearchStatisticStep.AtSearch:
                return atSearch.GetSnapTime() - atLoad.GetSnapTime();
            case SearchStatisticStep.Total:
                return atSearch.GetSnapTime() - atLaunch.GetSnapTime();
            default:
                throw new Exception("not valid step for time");
        }

    }


    public string GetMemoryUsage(SearchStatisticStep step)
    {
        switch (step)
        {
            case SearchStatisticStep.AtLaunch:
                return atLaunch.GetMemoryUsage();
            case SearchStatisticStep.AtLoad:
                return atLoad.GetMemoryUsage();
            case SearchStatisticStep.AtSearch:
                return atSearch.GetMemoryUsage();
            default:
                throw new Exception("invalid param");
        }
    }

    public string GetSummaryReport()
    {
        var summary = string.Empty;
        summary += ("Memory at launch: " + GetMemoryUsage(SearchStatisticStep.AtLaunch) + Environment.NewLine);
        summary += ("Total records when loaded (" + GetRecordsAtStep(SearchStatisticStep.AtLoad) + ") with" + GetMemoryUsage(SearchStatisticStep.AtLoad) + Environment.NewLine);
        summary += ("Total records after search (" + GetRecordsAtStep(SearchStatisticStep.AtSearch) + ") with" + GetMemoryUsage(SearchStatisticStep.AtSearch) + Environment.NewLine);
        summary += ("Took " + GetTimeTaken(SearchStatisticStep.AtLoad).TotalSeconds + " second(s) to load." + Environment.NewLine);
        summary += ("Took " + GetTimeTaken(SearchStatisticStep.AtSearch).TotalSeconds + " second(s) to search." + Environment.NewLine);
        summary += ("Took " + GetTimeTaken(SearchStatisticStep.Total).TotalSeconds + " second(s) total." + Environment.NewLine);
        return summary;
    }

    public void ReportToConsole()
    {
        Console.WriteLine(GetSummaryReport());

    }
}
