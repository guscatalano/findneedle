using findneedle.Implementations;
using findneedle.Interfaces;
using findneedle.PluginSubsystem;
using FindNeedleCoreUtils;
using FindNeedlePluginLib.Implementations.SearchNotifications;
using FindNeedlePluginLib.Interfaces;

namespace findneedle;



public class SearchQuery : ISearchQuery
{

    

    public void AddFilter(ISearchFilter filter)
    {
        filters.Add(filter);
    }

    private List<ISearchOutput> _output;
    public List<ISearchOutput> outputs
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
    }
    

    private List<IResultProcessor> _processors;
    public List<IResultProcessor> processors
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
            _stats ??= new SearchStatistics(this);
            return _stats;
        }
        set => _stats = value;
    }

    private List<ISearchFilter> _filters;
    public List<ISearchFilter> filters
    {
        get
        {
            _filters ??= new List<ISearchFilter>();
            return _filters;
        }
        set => _filters = value;
    }

    private List<ISearchLocation> _locations;
    public List<ISearchLocation> locations
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
        this.locations = loc;
    }

    public List<ISearchLocation> GetLocations()
    {
        return locations;
    }

    public List<ISearchFilter> GetFilters()
    {
        return filters;
    }

    public SearchQuery()
    {
        stats = new(this);
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
        foreach (var loc in locations)
        {
            loc.SetSearchDepth(depthForAllLocations);
        }
    }

    public void LoadAllLocationsInMemory()
    {
        stats = new SearchStatistics(this); //reset the stats
        SearchStepNotificationSink.NotifyStep(SearchStep.AtLoad);
        SetDepthForAllLocations(Depth);
        var count = 1;
        
        foreach (var loc in locations)
        {
            SearchStepNotificationSink.progressSink.NotifyProgress(50 * (count / locations.Count()), "loading location: " + loc.GetName());
            //loc.SetNotificationCallback(progressSink);
            //loc.SetSearchStatistics(stats);
            loc.LoadInMemory();
            count++;
        }
        stats.LoadedAll();
    }

    public List<ISearchResult> GetFilteredResults()
    {
        SearchStepNotificationSink.NotifyStep(SearchStep.AtSearch);
        List<ISearchResult> results = new List<ISearchResult>();
        var count = 1;
        foreach (var loc in locations)
        {
            SearchStepNotificationSink.progressSink.NotifyProgress(50 + (50 * (count / locations.Count())), "loading results: " + loc.GetName());
            results.AddRange(loc.Search(this));
            count++;
        }
        stats.Searched();
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
        foreach(var output in outputs)
        {
            output.WriteAllOutput(GetFilteredResults());
        }
    }

    public void PrintOutputFilesToConsole()
    {
        foreach (var output in outputs)
        {
            Console.WriteLine(output.GetPluginFriendlyName() + ": " + output.GetOutputFileName());
        }
    }

    public void AddOutput(ISearchOutput output)
    {
        outputs.Add(output);
    }

   
    public string? Name
    {
        get; set;
    }
}
