using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle;

namespace FindNeedlePluginLib.Interfaces;
public interface ISearchQuery
{
    public void AddFilter(ISearchFilter filter);

    public List<ISearchFilter> GetFilters();

    public SearchStatistics GetSearchStatistics();

    public List<ISearchLocation> GetLocations();
}
