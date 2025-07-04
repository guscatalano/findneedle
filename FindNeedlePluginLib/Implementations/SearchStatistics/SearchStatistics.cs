using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using FindNeedleCoreUtils;


namespace FindNeedlePluginLib;



public class ReportFromComponent
{
    public string component = string.Empty;
    public SearchStep step = SearchStep.Total;
    public string summary = string.Empty;
    public Dictionary<string, dynamic> metric = new();
}


public class SearchStatistics
{
    public SearchStatistics()
    {
        atLoad = new MemorySnapshot();
        atSearch = new MemorySnapshot();
        atLaunch = new MemorySnapshot();
        atLaunch.Snap();
    }

    private int totalRecordsSearch = 0;
    private int totalRecordsLoaded = 0;
    private readonly MemorySnapshot atLaunch;
    private readonly MemorySnapshot atLoad;
    private readonly MemorySnapshot atSearch;
    private ISearchQuery? searchQuery;

    public void StepNotificationHandler(SearchStep step)
    {
        if(searchQuery == null)
        {
            throw new Exception("Search query is null");
        }
        switch (step)
        {
            case SearchStep.AtLoad:
                LoadedAll(searchQuery);
                atLoad.Snap();
                break;
            case SearchStep.AtSearch:
                Searched(searchQuery);
                atSearch.Snap();
                break;
            case SearchStep.AtLaunch:
                atLaunch.Snap();
                break;
            case SearchStep.AtProcessor:
            case SearchStep.AtOutput:
            case SearchStep.Total:
                // do nothing
                break;
            default:
                throw new Exception("not valid step");
        }
    }

    public Dictionary<SearchStep, List<ReportFromComponent>> componentReports = [];
    public void RegisterForNotifications(SearchStepNotificationSink sink, ISearchQuery query)
    {
        sink.RegisterForStepNotification(StepNotificationHandler);
        searchQuery = query;
    }

    public void ReportFromComponent(ReportFromComponent data)
    {
        if (!componentReports.TryGetValue(data.step, out var value))
        {
            value = new List<ReportFromComponent>();
            componentReports.Add(data.step, value);
        }

        value.Add(data);
        
    }

    public void LoadedAll(ISearchQuery q)
    {
        totalRecordsLoaded = 0;
        foreach (var loc in q.Locations)
        {
            loc.ReportStatistics();
            totalRecordsLoaded += loc.numRecordsInMemory;
        }

        atLoad.Snap();
    }

    public void Searched(ISearchQuery q)
    {
        totalRecordsSearch = 0;
        foreach (var loc in q.GetLocations())
        {
            loc.ReportStatistics();
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
                return atLaunch.GetMemoryUsageFriendly();
            case SearchStep.AtLoad:
                return atLoad.GetMemoryUsageFriendly();
            case SearchStep.AtSearch:
                return atSearch.GetMemoryUsageFriendly();
            default:
                throw new Exception("invalid param");
        }
    }

    public string GetSummaryReport()
    {
        var summary = string.Empty;
        // Memory at launch
        try
        {
            summary += ("Memory at launch: " + GetMemoryUsage(SearchStep.AtLaunch) + Environment.NewLine);
        }
        catch (Exception ex)
        {
            summary += ("Memory at launch: ERROR - " + ex.Message + Environment.NewLine);
        }
        // Records loaded
        try
        {
            summary += ("Total records when loaded (" + GetRecordsAtStep(SearchStep.AtLoad) + ") with" + GetMemoryUsage(SearchStep.AtLoad) + Environment.NewLine);
        }
        catch (Exception ex)
        {
            summary += ("Total records when loaded: ERROR - " + ex.Message + Environment.NewLine);
        }
        // Records after search
        try
        {
            summary += ("Total records after search (" + GetRecordsAtStep(SearchStep.AtSearch) + ") with" + GetMemoryUsage(SearchStep.AtSearch) + Environment.NewLine);
        }
        catch (Exception ex)
        {
            summary += ("Total records after search: ERROR - " + ex.Message + Environment.NewLine);
        }
        // Time taken to load
        try
        {
            summary += ("Took " + GetTimeTaken(SearchStep.AtLoad).TotalSeconds + " second(s) to load." + Environment.NewLine);
        }
        catch (Exception ex)
        {
            summary += ("Took to load: ERROR - " + ex.Message + Environment.NewLine);
        }
        // Time taken to search
        try
        {
            summary += ("Took " + GetTimeTaken(SearchStep.AtSearch).TotalSeconds + " second(s) to search." + Environment.NewLine);
        }
        catch (Exception ex)
        {
            summary += ("Took to search: ERROR - " + ex.Message + Environment.NewLine);
        }
        // Total time
        try
        {
            summary += ("Took " + GetTimeTaken(SearchStep.Total).TotalSeconds + " second(s) total." + Environment.NewLine);
        }
        catch (Exception ex)
        {
            summary += ("Took total: ERROR - " + ex.Message + Environment.NewLine);
        }
        return summary;
    }

    [ExcludeFromCodeCoverage]
    public void ReportToConsole()
    {
        Console.WriteLine(GetSummaryReport());
    }
}
