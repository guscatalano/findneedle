using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle;
using findneedle.PluginSubsystem;
using FindNeedlePluginLib.Interfaces;

namespace FindPluginCore.Searching.Serializers;
public class SearchQueryFactory
{

    public static ISearchQuery CreateSearchQuery(PluginManager pluginManager)
    {

        ISearchQuery q;
        switch (pluginManager.GetSearchQueryClass())
        {
            case "SearchQuery":
                q = new SearchQuery();
                break;
            case "NuSearchQuery":
                q = new NuSearchQuery();
                break;
            default:
                throw new Exception("unknown search query class");
        }
        return q;
    }
}
