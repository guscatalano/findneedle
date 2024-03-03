using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
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

    public static void AddFolderLocation(string location)
    {
        Locations.Add(new FolderLocation(location));
    }
    public static void AddEventLog(string eventlogname, bool useQueryAPI)
    {
        // Locations.Add(new LocalEventLogLocation(location));
        if (useQueryAPI)
        {
            Locations.Add(new LocalEventLogQueryLocation(eventlogname));
        } else
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

    public static string RunSearch()
    {
        SearchQuery q = new SearchQuery();
        q.SetFilters(Filters);
        q.SetLocations(Locations);
        q.LoadAllLocationsInMemory();
        SearchResults = q.GetFilteredResults();
        SearchStatistics x = q.GetSearchStatistics();
        return x.GetSummaryReport();

        
    }

    public static ObservableCollection<LocationListItem> GetLocationListItems()
    {
        ObservableCollection<LocationListItem> test = new ObservableCollection<LocationListItem>();
        foreach (SearchLocation loc in Locations)
        {
             test.Add(new LocationListItem() { Name = loc.GetName(), Description= loc.GetDescription() });
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
