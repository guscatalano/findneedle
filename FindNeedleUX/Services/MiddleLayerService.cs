using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using findneedle;
using findneedle.Implementations;
using FindNeedleUX.Services.WizardDef;
using FindNeedleUX.ViewObjects;
using Microsoft.UI.Xaml.Controls;

namespace FindNeedleUX.Services;
public class MiddleLayerService
{
    public static List<SearchLocation> Locations = new();
    public static List<SearchFilter> Filters = new();
    public static SearchQuery Query = new SearchQuery();

    public static void AddFolderLocation(string location)
    {
        Locations.Add(new FolderLocation(location));
    }

    public static void AddTimeAgoFilter(TimeAgoUnit unit, int count)
    {
        Filters.Add(new TimeAgoFilter(unit, count));
    }

    public static void AddTimeRangeFilter(DateTime start, DateTime end)
    {
        Filters.Add(new TimeRangeFilter(start, end));
    }
    public static void AddEventLog(string eventlogname, bool useQueryAPI)
    {
        // Locations.Add(new LocalEventLogLocation(location));
        if (useQueryAPI)
        {
            Locations.Add(new LocalEventLogQueryLocation(eventlogname));
        }
        else
        {
            Locations.Add(new LocalEventLogLocation(eventlogname));
        }
    }

    public static void AddKeywordFilter(string keyword)
    {
        Filters.Add(new SimpleKeywordFilter(keyword));
    }

    public static void PageChanged(IWizard wizard, Page current)
    {

    }

    public static List<SearchResult> GetSearchResults()
    {
        return SearchResults;
    }

    private static List<SearchResult> SearchResults = new();

    public static void UpdateSearchQuery()
    {
        Query = new SearchQuery();
        Query.filters = Filters;
        Query.locations = Locations;
    }

    public async static Task<string> RunSearch(bool surfacescan = false)
    {

        UpdateSearchQuery();
        if (surfacescan)
        {
            Query.SetDepthForAllLocations(SearchLocationDepth.Shallow);
        } else
        {
            Query.SetDepthForAllLocations(SearchLocationDepth.Intermediate);
        }
        Query.LoadAllLocationsInMemory();
       
        SearchResults = Query.GetFilteredResults();
        
        SearchStatistics x = Query.GetSearchStatistics();
        return x.GetSummaryReport();


    }



    public static void OpenWorkspace(string filename)
    {
        var o = SearchQueryJsonReader.LoadSearchQuery(File.ReadAllText(filename));
        SearchQuery r = SearchQueryJsonReader.GetSearchQueryObject(o);
        Query = r;
        Filters = Query.filters;
        Locations = Query.locations;
    }


    public static void SaveWorkspace(string filename)
    {
        UpdateSearchQuery();
        SerializableSearchQuery r = SearchQueryJsonReader.GetSerializableSearchQuery(Query);
        string json = r.GetQueryJson();
        File.WriteAllText(filename, json);
    }

    public static ObservableCollection<LocationListItem> GetLocationListItems()
    {
        ObservableCollection<LocationListItem> test = new ObservableCollection<LocationListItem>();
        foreach (SearchLocation loc in Locations)
        {
            test.Add(new LocationListItem() { Name = loc.GetName(), Description = loc.GetDescription() });
        }
        return test;
    }

    public static ObservableCollection<FilterListItem> GetFilterListItems()
    {
        ObservableCollection<FilterListItem> test = new ObservableCollection<FilterListItem>();
        foreach (SearchFilter fil in Filters)
        {
            test.Add(new FilterListItem() { Name = fil.GetName(), Description = fil.GetDescription() });
        }
        return test;
    }
}
