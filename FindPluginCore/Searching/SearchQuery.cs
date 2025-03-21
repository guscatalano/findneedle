using findneedle.Implementations;
using findneedle.Interfaces;
using findneedle.PluginSubsystem;
using FindNeedleCoreUtils;
using FindNeedlePluginLib.Interfaces;

namespace findneedle;



public class SearchQuery : ISearchQuery
{

    

    public void AddFilter(ISearchFilter filter)
    {
        filters.Add(filter);
    }
    private SearchLocationDepth Depth;

    private SearchProgressSink _progressSink;

    public SearchProgressSink progressSink
    {
        get
        {
            _progressSink ??= new SearchProgressSink();
            return _progressSink;
        }
        set => _progressSink = value;
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
        PluginManager.DiscoverPlugins();
        stats = new SearchStatistics(this);
        _progressSink = new SearchProgressSink();
        _stats = stats;
        _filters = new List<ISearchFilter>();
        _locations = new List<ISearchLocation>();
        _processors = new List<IResultProcessor>();
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
        progressSink.NotifyProgress(0, "starting");
        SetDepthForAllLocations(Depth);
        var count = 1;
        foreach (var loc in locations)
        {
            progressSink.NotifyProgress(50 * (count / locations.Count()), "loading location: " + loc.GetName());
            loc.SetNotificationCallback(progressSink);
            loc.SetSearchStatistics(stats);
            loc.LoadInMemory();
            count++;
        }
        stats.LoadedAll();
    }

    public List<ISearchResult> GetFilteredResults()
    {
        List<ISearchResult> results = new List<ISearchResult>();
        var count = 1;
        foreach (var loc in locations)
        {
            progressSink.NotifyProgress(50 + (50 * (count / locations.Count())), "loading results: " + loc.GetName());
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

    public SearchProgressSink GetSearchProgressSink() { return progressSink; }

    public string? Name
    {
        get; set;
    }
}
