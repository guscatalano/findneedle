using findneedle.Implementations;
using findneedle.PluginSubsystem;
using FindNeedlePluginLib.Interfaces;

namespace findneedle;



public class SearchQuery : ISearchQuery
{


    private readonly SearchLocationDepth Depth;

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



    private string ReplaceInvalidChars(string text)
    {
        text = text.Replace(",", "");
        text = text.Replace("(", "");
        text = text.Replace(")", "");
        text = text.Trim();
        return text;
    }

    private List<string> SplitApart(string text)
    {
        List<string> ret = new List<string>();
        var results = text.Split(",");
        foreach (var i in results)
        {
            var ix = ReplaceInvalidChars(i);
            if (string.IsNullOrEmpty(ix))
            {
                continue;
            }
            ret.Add(ix);
        }
        return ret;
    }


#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public SearchQuery()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    {
        Initialize();
    }


    public void Initialize()
    {
        PluginManager.DiscoverPlugins();
        stats = new SearchStatistics(this);
        _progressSink = new SearchProgressSink();
        _stats = stats;
        _filters = new List<ISearchFilter>();
        _locations = new List<ISearchLocation>();
    }


#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public SearchQuery(Dictionary<string, string> arguments)
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    {

        Initialize();

        foreach (KeyValuePair<string, string> pair in arguments)
        {
            if (pair.Key.StartsWith("keyword", StringComparison.OrdinalIgnoreCase))
            {
                filters.Add(new SimpleKeywordFilter(pair.Value));
                continue;
            }

            //searchfilter=time(start,end)
            //searchfilter=ago(2h)
            if (pair.Key.StartsWith("searchfilter", StringComparison.OrdinalIgnoreCase))
            {
                if (pair.Value.StartsWith("time"))
                {
                    var par = pair.Value.Substring(4);
                    List<string> x = SplitApart(par);
                    DateTime start = DateTime.Parse(x[0]);
                    DateTime end = DateTime.Parse(x[1]);
                    filters.Add(new TimeRangeFilter(start, end));
                }
                if (pair.Value.StartsWith("ago"))
                {
                    var par = pair.Value.Substring(3);
                    List<string> x = SplitApart(par);
                    filters.Add(new TimeAgoFilter(x[0]));
                }
                continue;
            }

            if (pair.Key.StartsWith("depth", StringComparison.OrdinalIgnoreCase))
            {
                SearchLocationDepth depth = SearchLocationDepth.Intermediate;
                var ret = Enum.TryParse<SearchLocationDepth>(pair.Value, out depth);
                if (!ret)
                {
                    throw new Exception("Failed to parse depth");
                }
                this.Depth = depth;

            }


            const string PATH_PREPEND = "path#";
            //location=localmachine
            //location=C:\
            if (pair.Key.StartsWith("location", StringComparison.OrdinalIgnoreCase))
            {
                /*
                if (pair.Value.Equals("localeventlog", StringComparison.OrdinalIgnoreCase))
                {
                    locations.Add(new LocalEventLogLocation());
                    continue;
                }
                if (pair.Value.Equals("localeventlogquery", StringComparison.OrdinalIgnoreCase))
                {
                    locations.Add(new LocalEventLogQueryLocation());
                    continue;
                }*/
                if (pair.Value.StartsWith(PATH_PREPEND, StringComparison.OrdinalIgnoreCase))
                {
                    var path = pair.Value.Substring(PATH_PREPEND.Length);
                    if (!Path.Exists(path) && !File.Exists(path))
                    {
                        throw new Exception("Path: " + path + " does not exist");
                    }
                    locations.Add(new FolderLocation(path));
                    continue;
                }
            }
        }

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
            progressSink.NotifyProgress(50*(count/ locations.Count()), "loading location: " + loc.GetName());
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
            progressSink.NotifyProgress(50+(50 * (count / locations.Count())), "loading results: " + loc.GetName());
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
