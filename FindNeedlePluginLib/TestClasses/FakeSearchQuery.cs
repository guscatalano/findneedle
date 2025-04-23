using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle;
using findneedle.Interfaces;
using FindNeedlePluginLib.Interfaces;

namespace FindNeedlePluginLib.TestClasses;


[ExcludeFromCodeCoverage]
public class FakeSearchQuery : ISearchQuery
{
    public List<ISearchFilter> Filters => [];
    public List<ISearchLocation> Locations => [];
    public List<IResultProcessor> Processors => [];
    public List<ISearchOutput> Outputs => [];
    public SearchLocationDepth Depth {
        get;
        set;
    }

    public void AddFilter(ISearchFilter filter) { }
    public List<ISearchFilter> GetFilters() {
        return []; 
    }
    public List<ISearchLocation> GetLocations() {
        return []; 
    }
    public SearchStatistics GetSearchStatistics() => throw new NotImplementedException();

    public void RunThrough()
    {
        throw new NotImplementedException();
    }
}
