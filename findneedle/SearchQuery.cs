using findneedle.Implementations;

namespace findneedle
{


    public class SearchQuery
    {


        private SearchLocationDepth Depth;

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

        private List<SearchFilter> _filters;
        public List<SearchFilter> filters
        {
            get
            {
                _filters ??= new List<SearchFilter>();
                return _filters;
            }
            set => _filters = value;
        }

        private List<SearchLocation> _locations;
        public List<SearchLocation> locations
        {
            get
            {
                _locations ??= new List<SearchLocation>();
                return _locations;
            }
            set => _locations = value;
        }




        public void SetLocations(List<SearchLocation> loc)
        {

            this.locations = loc;
        }



        public List<SearchLocation> GetLocations()
        {
            return locations;
        }

        public List<SearchFilter> GetFilters()
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
                string ix = ReplaceInvalidChars(i);
                if (string.IsNullOrEmpty(ix))
                {
                    continue;
                }
                ret.Add(ix);
            }
            return ret;
        }


        public SearchQuery()
        {
            stats = new SearchStatistics(this);
        }



        public SearchQuery(Dictionary<string, string> arguments) : base()
        {
            stats = new SearchStatistics(this);
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
                        string par = pair.Value.Substring(4);
                        List<string> x = SplitApart(par);
                        DateTime start = DateTime.Parse(x[0]);
                        DateTime end = DateTime.Parse(x[1]);
                        filters.Add(new TimeRangeFilter(start, end));
                    }
                    if (pair.Value.StartsWith("ago"))
                    {
                        string par = pair.Value.Substring(3);
                        List<string> x = SplitApart(par);
                        filters.Add(new TimeAgoFilter(x[0]));
                    }
                    continue;
                }

                if (pair.Key.StartsWith("depth", StringComparison.OrdinalIgnoreCase))
                {
                    SearchLocationDepth depth = SearchLocationDepth.Intermediate;
                    bool ret = Enum.TryParse<SearchLocationDepth>(pair.Value, out depth);
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
                    if (pair.Value.Equals("localeventlog", StringComparison.OrdinalIgnoreCase))
                    {
                        locations.Add(new LocalEventLogLocation());
                        continue;
                    }
                    if (pair.Value.Equals("localeventlogquery", StringComparison.OrdinalIgnoreCase))
                    {
                        locations.Add(new LocalEventLogQueryLocation());
                        continue;
                    }
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
            SetDepthForAllLocations(Depth);
            foreach (var loc in locations)
            {
                loc.LoadInMemory(false, this);
            }
            stats.LoadedAll();
        }

        public List<SearchResult> GetFilteredResults()
        {
            List<SearchResult> results = new List<SearchResult>();
            foreach (var loc in locations)
            {
                results.AddRange(loc.Search(this));
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

        public string? Name
        {
            get; set;
        }
    }
}
