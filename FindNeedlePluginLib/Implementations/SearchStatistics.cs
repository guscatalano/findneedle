using System.Diagnostics;
using FindNeedleCoreUtils;
using FindNeedlePluginLib.Interfaces;

namespace findneedle;


public class MemorySnapshot
{
   

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
        return " PrivateMemory (" + ByteUtils.BytesToFriendlyString(privatememory) + ") / GC Memory (" + ByteUtils.BytesToFriendlyString(gcmemory) + ").";
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
