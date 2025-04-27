using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle;
using findneedle.Interfaces;
using FindNeedlePluginLib.Implementations.SearchNotifications;

namespace FindNeedlePluginLib.Interfaces;
public interface ISearchQuery
{
    void AddFilter(ISearchFilter filter);

    List<ISearchFilter> GetFilters();

    SearchStatistics GetSearchStatistics();

    List<ISearchLocation> GetLocations();

    List<ISearchFilter> Filters { get; }
    List<ISearchLocation> Locations { get; }
    List<IResultProcessor> Processors { get; set;
    }
    List<ISearchOutput> Outputs { get; }

    SearchLocationDepth Depth { get; set; }

    string Name => "test";

    //No matter the implementation, this function should run through every step
    void RunThrough();


}
