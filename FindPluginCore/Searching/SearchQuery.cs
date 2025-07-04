using findneedle.Implementations;
using findneedle.PluginSubsystem;
using FindNeedleCoreUtils;
using FindNeedlePluginLib;

namespace findneedle;



public class SearchQuery : ISearchQuery
{

    public void RunThrough()
    {
        //Old implementation

        LoadAllLocationsInMemory();
        GetFilteredResults();
        ProcessAllResultsToOutput();
        PrintOutputFilesToConsole();
        GetSearchStatsOutput();
        //IResultProcessor p = new WatsonCrashProcessor();
        //p.ProcessResults(y);
        //p.GetOutputFile();
    }

    public void AddFilter(ISearchFilter filter)
    {
        Filters.Add(filter);
    }

    private List<ISearchOutput> _output;
    public List<ISearchOutput> Outputs
    {
        get
        {
            _output ??= new List<ISearchOutput>();
            return _output;
        }
        set => _output = value;
    }

    private SearchLocationDepth _depth;

  

    public SearchLocationDepth Depth
    {
        get => _depth;
        set => _depth = value;
    }

    private SearchStepNotificationSink _stepnotifysink;

    public SearchStepNotificationSink SearchStepNotificationSink
    {
        get
        {
            _stepnotifysink ??= new SearchStepNotificationSink();
            return _stepnotifysink;
        }
        set
        {
            _stepnotifysink = value;
        }
    }
    

    private List<IResultProcessor> _processors;
    public List<IResultProcessor> Processors
    {
        get
        {
            _processors ??= new List<IResultProcessor>();
            return _processors;
        }
        set => _processors = value;
    }


    private SearchStatistics _stats;
    public SearchStatistics stats
    {
        get
        {
            _stats ??= new SearchStatistics();
            return _stats;
        }
        set => _stats = value;
    }

    private List<ISearchFilter> _filters;
    public List<ISearchFilter> Filters
    {
        get
        {
            _filters ??= new List<ISearchFilter>();
            return _filters;
        }
        set => _filters = value;
    }

    private List<ISearchLocation> _locations;
    public List<ISearchLocation> Locations
    {
        get
        {
            _locations ??= new List<ISearchLocation>();
            return _locations;
        }
        set => _locations = value;
    }

    public void SetLocations(List<ISearchLocation> loc)
    {
        this.Locations = loc;
    }

    public List<ISearchLocation> GetLocations()
    {
        return Locations;
    }

    public List<ISearchFilter> GetFilters()
    {
        return Filters;
    }

    public SearchQuery()
    {
        stats = new();
        _stats = stats;
        _filters = [];
        _locations = [];
        _processors = [];
        _output = [];
        _stepnotifysink = new();
    }


    public void SetDepth(SearchLocationDepth depth)
    {
        this.Depth = depth;
    }

    public void SetDepthForAllLocations(SearchLocationDepth depthForAllLocations)
    {
        foreach (var loc in Locations)
        {
            loc.SetSearchDepth(depthForAllLocations);
        }
    }

    public void LoadAllLocationsInMemory(System.Threading.CancellationToken cancellationToken = default)
    {
        stats = new SearchStatistics(); //reset the stats
        SearchStepNotificationSink.NotifyStep(SearchStep.AtLoad);
        SetDepthForAllLocations(Depth);
        var count = 1;
        int total = Locations.Count();
        foreach (var loc in Locations)
        {
            int percent = total > 0 ? (int)(50.0 * count / total) : 0;
            SearchStepNotificationSink.progressSink.NotifyProgress(percent, "loading location: " + loc.GetName());
            loc.LoadInMemory(cancellationToken);
            count++;
        }
        stats.LoadedAll(this);
    }

    public List<ISearchResult> GetFilteredResults(System.Threading.CancellationToken cancellationToken = default)
    {
        SearchStepNotificationSink.NotifyStep(SearchStep.AtSearch);
        List<ISearchResult> results = new List<ISearchResult>();
        var count = 1;
        int total = Locations.Count();
        foreach (var loc in Locations)
        {
            int percent = total > 0 ? 50 + (int)(50.0 * count / total) : 50;
            SearchStepNotificationSink.progressSink.NotifyProgress(percent, "loading results: " + loc.GetName());
            results.AddRange(loc.Search(cancellationToken));
            count++;
        }
        stats.Searched(this);
        return results;
    }

    public SearchStatistics GetSearchStatistics()
    {
        return stats;
    }

    public void GetSearchStatsOutput()
    {
        stats.ReportToConsole();
    }

    public void ProcessAllResultsToOutput()
    {
        //Remember to provide one that does it one y one at some point 
        foreach(var output in Outputs)
        {
            output.WriteAllOutput(GetFilteredResults());
        }
    }

    public void PrintOutputFilesToConsole()
    {
        foreach (var output in Outputs)
        {
            Console.WriteLine(output.GetPluginFriendlyName() + ": " + output.GetOutputFileName());
        }
    }

    public void AddOutput(ISearchOutput output)
    {
        Outputs.Add(output);
    }

    public void Step1_LoadAllLocationsInMemory()
    {
        LoadAllLocationsInMemory();
    }
    public List<ISearchResult> Step2_GetFilteredResults()
    {
        return GetFilteredResults();
    }
    public void Step3_ResultsToProcessors()
    {
        return; 
    }
    public void Step4_ProcessAllResultsToOutput()
    {
        ProcessAllResultsToOutput();
    }
    public void Step5_Done()
    {
        return; 
    }

    public string Name
    {
        get => _name ?? string.Empty;
        set => _name = value;
    }

    private string? _name;
}
