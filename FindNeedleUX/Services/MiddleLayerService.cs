using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using findneedle;
using findneedle.Implementations;
using FindNeedlePluginLib;
using findneedle.PluginSubsystem;
using FindNeedleCoreUtils;
using FindNeedleUX.ViewObjects;
using FindPluginCore.Searching.Serializers;
using Microsoft.UI.Xaml.Controls;

namespace FindNeedleUX.Services;
public class MiddleLayerService
{
    public static List<ISearchLocation> Locations = new();
    public static List<ISearchFilter> Filters = new();
    public static SearchQueryUX SearchQueryUX = new();

    public static event Action StateChanged;

    public static void NotifyStateChanged() => StateChanged?.Invoke();

    public static void AddFolderLocation(string location)
    {
        var folderloc = new FolderLocation() { path = location };
        //Setup file extension processors
        var extensions = PluginManager.GetSingleton().GetAllPluginsInstancesOfAType<IFileExtensionProcessor>();

        folderloc.SetExtensionProcessorList(extensions);
        Locations.Add(folderloc);
        NotifyStateChanged();
    }

    public static List<ISearchResult> GetSearchResults()
    {
        return SearchResults;
    }

    private static List<ISearchResult> SearchResults = new();

    public static void UpdateSearchQuery()
    {
        // Update the processor list in the query based on the current plugin config
        var pluginManager = PluginManager.GetSingleton();
        var config = pluginManager.config;
        var enabledProcessors = new List<IResultProcessor>();
        if (config != null)
        {
            foreach (var entry in config.entries)
            {
                if (entry.enabled)
                {
                    // Find the processor instance by name (FriendlyName or ClassName)
                    var processor = pluginManager.GetAllPluginsInstancesOfAType<IResultProcessor>()
                        .FirstOrDefault(p =>
                            p.GetType().Name == entry.name ||
                            (p.GetType().FullName != null && p.GetType().FullName.EndsWith(entry.name))
                        );
                    if (processor != null && !enabledProcessors.Contains(processor))
                        enabledProcessors.Add(processor);
                }
            }
        }
        var query = SearchQueryUX.CurrentQuery;
        if (query != null)
        {
            // Add RuleDSL processors for each rules config file
            if (query.RulesConfigPaths != null && query.RulesConfigPaths.Count > 0)
            {
                foreach (var rulesPath in query.RulesConfigPaths)
                {
                    if (System.IO.File.Exists(rulesPath))
                    {
                        var ruleDslProcessor = new FindNeedleRuleDSL.FindNeedleRuleDSLPlugin("*", rulesPath);
                        enabledProcessors.Add(ruleDslProcessor);
                        System.Diagnostics.Debug.WriteLine($"Added RuleDSL processor for: {rulesPath}");
                    }
                }
            }

            query.Processors = enabledProcessors;

            SearchQueryUX.UpdateSearchQuery();
            // Try to get SearchStepNotificationSink if possible
            SearchStepNotificationSink? sink = query?.SearchStepNotificationSink;
            SearchQueryUX.UpdateAllParameters(SearchLocationDepth.Intermediate, Locations, Filters, 
                query.Processors, query.Outputs, sink, query.stats);
        }
    }

    public static List<LogLine> GetLogLines()
    {
        List<LogLine> lines = new List<LogLine>();
        var index = 0;
        foreach(ISearchResult r in GetSearchResults())
        {
            lines.Add(new LogLine(r, index));
            index++;
        }
        return lines;
    }

    public static SearchProgressSink GetProgressEventSink()
    {
        var query = SearchQueryUX.CurrentQuery;
        return query != null ? query.SearchStepNotificationSink.progressSink : null;
    }

    public static Task<string> RunSearch(bool surfacescan = false, CancellationToken cancellationToken = default)
    {

        UpdateSearchQuery();
        var query = SearchQueryUX.CurrentQuery;
        if (query != null)
        {
            if (surfacescan)
            {
                query.SetDepthForAllLocations(SearchLocationDepth.Shallow);
            } else
            {
                query.SetDepthForAllLocations(SearchLocationDepth.Intermediate);
            }
        }
        if (cancellationToken != default)
        {
            SearchResults = SearchQueryUX.GetSearchResults(cancellationToken);
        }
        else
        {
            SearchResults = SearchQueryUX.GetSearchResults();
        }
        SearchStatistics x = SearchQueryUX.GetSearchStatistics();
        NotifyStateChanged();
        return Task.FromResult(x.GetSummaryReport());


    }

    public static SearchStatistics GetStats()
    {
        var query = SearchQueryUX.CurrentQuery;
        var searchQueryConcrete = query as SearchQuery;
        return searchQueryConcrete != null ? searchQueryConcrete.GetSearchStatistics() : null;
    }



    public static void OpenWorkspace(string filename)
    {
        var o = SearchQueryJsonReader.LoadSearchQuery(File.ReadAllText(filename));
        SearchQuery r = SearchQueryJsonReader.GetSearchQueryObject(o);
        Filters = r.Filters;
        Locations = r.Locations;

        foreach(ISearchLocation loc in Locations)
        {
            //Fix up the extension list
            if (loc is FolderLocation)
            {
                ((FolderLocation)loc).SetExtensionProcessorList(PluginManager.GetSingleton().GetAllPluginsInstancesOfAType<IFileExtensionProcessor>());
            }
        }
        UpdateSearchQuery();
        NotifyStateChanged();
    }

    public static void NewWorkspace()
    {
        Filters = new List<ISearchFilter>();
        Locations = new List<ISearchLocation>();

        UpdateSearchQuery();
        NotifyStateChanged();
    }

    public static void SaveWorkspace(string filename)
    {
        UpdateSearchQuery();
        var query = SearchQueryUX.CurrentQuery;
        var searchQueryConcrete = query as SearchQuery;
        if (searchQueryConcrete != null)
        {
            SerializableSearchQuery r = SearchQueryJsonReader.GetSerializableSearchQuery(searchQueryConcrete);
            var json = r.GetQueryJson();
            File.WriteAllText(filename, json);
        }
    }

    public static void RemoveLocationByName(string name)
    {
        var loc = Locations.FirstOrDefault(l => l.GetName() == name);
        if (loc != null)
        {
            Locations.Remove(loc);
            UpdateSearchQuery();
            NotifyStateChanged();
        }
    }

    public static void RemoveFilterByName(string name)
    {
        var f = Filters.FirstOrDefault(x => x.GetName() == name);
        if (f != null)
        {
            Filters.Remove(f);
            UpdateSearchQuery();
            NotifyStateChanged();
        }
    }

    public static ObservableCollection<LocationListItem> GetLocationListItems()
    {
        ObservableCollection<LocationListItem> test = new ObservableCollection<LocationListItem>();
        foreach (ISearchLocation loc in Locations)
        {
            test.Add(new LocationListItem() { Name = loc.GetName(), Description = loc.GetDescription() });
        }
        return test;
    }

    public static ObservableCollection<FilterListItem> GetFilterListItems()
    {
        ObservableCollection<FilterListItem> test = new ObservableCollection<FilterListItem>();
        foreach (ISearchFilter fil in Filters)
        {
            test.Add(new FilterListItem() { Name = fil.GetName(), Description = fil.GetDescription() });
        }
        return test;
    }

    /// <summary>
    /// Gets the current query (NuSearchQuery) being used in the UI.
    /// </summary>
    public static ISearchQuery? GetCurrentQuery()
    {
        return SearchQueryUX?.CurrentQuery;
    }
}
