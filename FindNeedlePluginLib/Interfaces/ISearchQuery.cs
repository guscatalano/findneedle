using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle;

namespace FindNeedlePluginLib.Interfaces;
public interface ISearchQuery
{
    public List<SearchFilter> GetFilters();
    public SearchProgressSink GetSearchProgressSink();

    public SearchStatistics GetSearchStatistics();

    public List<SearchLocation> GetLocations();
}
