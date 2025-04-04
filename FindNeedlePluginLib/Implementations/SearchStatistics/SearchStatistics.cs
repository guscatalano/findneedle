using System.Diagnostics;
using FindNeedleCoreUtils;
using FindNeedlePluginLib.Implementations.SearchNotifications;
using FindNeedlePluginLib.Implementations.SearchStatistics;
using FindNeedlePluginLib.Interfaces;
using static FindNeedlePluginLib.Implementations.SearchNotifications.SearchStepNotificationSink;

namespace findneedle;



public class ReportFromComponent
{
    public string component = string.Empty;
    public SearchStep step = SearchStep.Total;
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



    public Dictionary<SearchStep, List<ReportFromComponent>> componentReports = new();
    

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

    public int GetRecordsAtStep(SearchStep step)
    {
        switch (step)
        {
            case SearchStep.AtLoad:
                return totalRecordsLoaded;
            case SearchStep.AtSearch:
                return totalRecordsSearch;
            default:
                throw new Exception("bad input");
        }
    }

    public TimeSpan GetTimeTaken(SearchStep step)
    {
        switch (step)
        {
            case SearchStep.AtLoad:
                return atLoad.GetSnapTime() - atLaunch.GetSnapTime();
            case SearchStep.AtSearch:
                return atSearch.GetSnapTime() - atLoad.GetSnapTime();
            case SearchStep.Total:
                return atSearch.GetSnapTime() - atLaunch.GetSnapTime();
            default:
                throw new Exception("not valid step for time");
        }

    }


    public string GetMemoryUsage(SearchStep step)
    {
        switch (step)
        {
            case SearchStep.AtLaunch:
                return atLaunch.GetMemoryUsage();
            case SearchStep.AtLoad:
                return atLoad.GetMemoryUsage();
            case SearchStep.AtSearch:
                return atSearch.GetMemoryUsage();
            default:
                throw new Exception("invalid param");
        }
    }

    public string GetSummaryReport()
    {
        var summary = string.Empty;
        summary += ("Memory at launch: " + GetMemoryUsage(SearchStep.AtLaunch) + Environment.NewLine);
        summary += ("Total records when loaded (" + GetRecordsAtStep(SearchStep.AtLoad) + ") with" + GetMemoryUsage(SearchStep.AtLoad) + Environment.NewLine);
        summary += ("Total records after search (" + GetRecordsAtStep(SearchStep.AtSearch) + ") with" + GetMemoryUsage(SearchStep.AtSearch) + Environment.NewLine);
        summary += ("Took " + GetTimeTaken(SearchStep.AtLoad).TotalSeconds + " second(s) to load." + Environment.NewLine);
        summary += ("Took " + GetTimeTaken(SearchStep.AtSearch).TotalSeconds + " second(s) to search." + Environment.NewLine);
        summary += ("Took " + GetTimeTaken(SearchStep.Total).TotalSeconds + " second(s) total." + Environment.NewLine);
        return summary;
    }

    public void ReportToConsole()
    {
        Console.WriteLine(GetSummaryReport());

    }
}
