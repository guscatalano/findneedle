using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle;
using FindNeedlePluginLib.Interfaces;

namespace FindNeedlePluginLib.TestClasses;


[ExcludeFromCodeCoverage]
public class FakeSearchQuery : ISearchQuery
{
    public void AddFilter(ISearchFilter filter) { }
    public List<ISearchFilter> GetFilters() {
        return []; 
    }
    public List<ISearchLocation> GetLocations() {
        return []; 
    }
    public SearchStatistics GetSearchStatistics() => throw new NotImplementedException();
}
