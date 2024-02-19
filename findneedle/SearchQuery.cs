using findneedle.Implementations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace findneedle
{

    



    public class SearchQuery
    {

        private List<SearchFilter> filters = new List<SearchFilter>();
        private List<SearchLocation> locations = new List<SearchLocation>();




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
            text =  text.Replace(")", "");
            text = text.Trim();
            return text;
        }

        private List<string> SplitApart(string text)
        {
            List<string> ret = new List<string>();
            string[] results = text.Split(",");
            foreach(string i in results)
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

        private SearchStatistics stats;
        private SearchLocationDepth depth = SearchLocationDepth.Intermediate;

        public SearchQuery(Dictionary<string, string> arguments)
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
                    this.depth = depth;

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
            SetDepthForAllLocations(depth);
            foreach (var loc in locations)
            {
                loc.LoadInMemory(false);
            }
            stats.LoadedAll();
        }

        public List<SearchResult> GetFilteredResults()
        {
            List <SearchResult> results = new List<SearchResult>();
            foreach (var loc in locations)
            {
                results.AddRange(loc.Search(this));
            }
            stats.Searched();
            return results;
        }

        public void GetSearchStatsOutput()
        {
            stats.ReportToConsole();
        }

        public string GetQueryJSON()
        {
            return JsonSerializer.Serialize(this);
            
        }
    }
}
