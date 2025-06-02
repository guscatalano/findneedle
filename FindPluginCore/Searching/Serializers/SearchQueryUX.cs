using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle;
using findneedle.Interfaces;
using findneedle.PluginSubsystem;
using FindNeedlePluginLib.Implementations.SearchNotifications;
using FindNeedlePluginLib.Interfaces;

namespace FindPluginCore.Searching.Serializers;
public class SearchQueryUX
{
    private ISearchQuery? q = null;
    private PluginManager? pluginManager;
    private bool initalized = false;

    public List<string> GetLoadedPlugins()
    {
        Initialize();

        return pluginManager.GetAllPluginsInstancesOfAType<IPluginDescription>()
            .Select(plugin => plugin.GetPluginTextDescription())
            .ToList();

    }

    public SearchQueryUX()
    {
        Initialize();
    }

    public void Initialize()
    {
        if (!initalized)
        {
            pluginManager = new PluginManager(); // Initialize plugin manager if not already done
            pluginManager.LoadAllPlugins(true);
            q = SearchQueryFactory.CreateSearchQuery(pluginManager); // Initialize 'q' to ensure it is non-null
            initalized = true;
        }
    }

    public void UpdateSearchQuery()
    {
        Initialize();
        q = SearchQueryFactory.CreateSearchQuery(pluginManager);
    }

    public void UpdateAllParameters(SearchLocationDepth depth, List<ISearchFilter> filters, List<ISearchOutput> outputs, SearchStepNotificationSink stepnotifysink)
    {
        q.Depth = depth;
        //q.Filters = filters;
        //q.Outputs = outputs;
        //q.SearchStepNotificationSink = stepnotifysink;
    }
}
