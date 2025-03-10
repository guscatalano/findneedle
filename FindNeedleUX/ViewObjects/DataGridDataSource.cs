// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using findneedle;
using FindNeedleUX.Services;
using Microsoft.UI.Xaml.Data;

namespace Microsoft.Toolkit.Uwp.SampleApp.Data;

[Bindable]
public class SearchDataSource
{
    private static ObservableCollection<SearchSourceDataItem> _items;
    private static List<string> _mountains;
    private static CollectionViewSource groupedItems;
    private string _cachedSortedColumn = string.Empty;

    // Loading data
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    public async Task<IEnumerable<SearchSourceDataItem>> GetDataAsync()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    {

        _items = new ObservableCollection<SearchSourceDataItem>();



        List<ISearchResult> ret = MiddleLayerService.GetSearchResults();
        foreach (ISearchResult result in ret)
        {
            _items.Add(
                new SearchSourceDataItem(result));
        }



        return _items;
    }

    // Load mountains into separate collection for use in combobox column
    public async Task<IEnumerable<string>> GetMountains()
    {
        if (_items == null || !_items.Any())
        {
            await GetDataAsync();
        }

        _mountains = _items?.OrderBy(x => x.Provider).Select(x => x.Provider).Distinct().ToList();

        return _mountains;
    }

    // Sorting implementation using LINQ
    public string CachedSortedColumn
    {
        get => _cachedSortedColumn;

        set => _cachedSortedColumn = value;
    }

    public ObservableCollection<SearchSourceDataItem> SortData(string sortBy, bool ascending)
    {
        _cachedSortedColumn = sortBy;
        switch (sortBy)
        {
            case "Rank":
                if (ascending)
                {
                    return new ObservableCollection<SearchSourceDataItem>(from item in _items
                                                                          orderby item.Time ascending
                                                                          select item);
                }
                else
                {
                    return new ObservableCollection<SearchSourceDataItem>(from item in _items
                                                                          orderby item.Time descending
                                                                          select item);
                }

            case "Parent_mountain":
                if (ascending)
                {
                    return new ObservableCollection<SearchSourceDataItem>(from item in _items
                                                                          orderby item.Parent_mountain ascending
                                                                          select item);
                }
                else
                {
                    return new ObservableCollection<SearchSourceDataItem>(from item in _items
                                                                          orderby item.Parent_mountain descending
                                                                          select item);
                }

            case "Mountain":
                if (ascending)
                {
                    return new ObservableCollection<SearchSourceDataItem>(from item in _items
                                                                          orderby item.Provider ascending
                                                                          select item);
                }
                else
                {
                    return new ObservableCollection<SearchSourceDataItem>(from item in _items
                                                                          orderby item.Provider descending
                                                                          select item);
                }

            case "Height_m":
                if (ascending)
                {
                    return new ObservableCollection<SearchSourceDataItem>(from item in _items
                                                                          orderby item.TaskName ascending
                                                                          select item);
                }
                else
                {
                    return new ObservableCollection<SearchSourceDataItem>(from item in _items
                                                                          orderby item.TaskName descending
                                                                          select item);
                }

            case "Range":
                if (ascending)
                {
                    return new ObservableCollection<SearchSourceDataItem>(from item in _items
                                                                          orderby item.Message ascending
                                                                          select item);
                }
                else
                {
                    return new ObservableCollection<SearchSourceDataItem>(from item in _items
                                                                          orderby item.Message descending
                                                                          select item);
                }
        }

        return _items;
    }

    // Grouping implementation using LINQ
    public CollectionViewSource GroupData(string groupBy = "Range")
    {
        ObservableCollection<GroupInfoCollection<SearchSourceDataItem>> groups = new ObservableCollection<GroupInfoCollection<SearchSourceDataItem>>();
        var query = from item in _items
                    orderby item
                    group item by item.Message into g
                    select new
                    {
                        GroupName = g.Key,
                        Items = g
                    };
        if (groupBy == "Parent_Mountain")
        {
            query = from item in _items
                    orderby item
                    group item by item.Parent_mountain into g
                    select new
                    {
                        GroupName = g.Key,
                        Items = g
                    };
        }
        foreach (var g in query)
        {
            GroupInfoCollection<SearchSourceDataItem> info = new GroupInfoCollection<SearchSourceDataItem>();
            info.Key = g.GroupName;
            foreach (var item in g.Items)
            {
                info.Add(item);
            }

            groups.Add(info);
        }

        groupedItems = new CollectionViewSource();
        groupedItems.IsSourceGrouped = true;
        groupedItems.Source = groups;

        return groupedItems;
    }

    public class GroupInfoCollection<T> : ObservableCollection<T>
    {
        public object Key
        {
            get; set;
        }

        public new IEnumerator<T> GetEnumerator()
        {
            return (IEnumerator<T>)base.GetEnumerator();
        }
    }

    // Filtering implementation using LINQ
    public enum FilterOptions
    {
        All = -1,
        Rank_Low = 0,
        Rank_High = 1,
        Height_Low = 2,
        Height_High = 3
    }

    public ObservableCollection<SearchSourceDataItem> FilterData(FilterOptions filterBy)
    {
        switch (filterBy)
        {
            case FilterOptions.All:
                return new ObservableCollection<SearchSourceDataItem>(_items);
                /*
            case FilterOptions.Rank_Low:
                return new ObservableCollection<SearchSourceDataItem>(from item in _items
                                                                      where item.Rank < 50
                                                                      select item);

            case FilterOptions.Rank_High:
                return new ObservableCollection<SearchSourceDataItem>(from item in _items
                                                                      where item.Rank > 50
                                                                      select item);
                /*
            case FilterOptions.Height_High:
                return new ObservableCollection<SearchSourceDataItem>(from item in _items
                                                                      where item.Height_m > 8000
                                                                      select item);

            case FilterOptions.Height_Low:
                return new ObservableCollection<SearchSourceDataItem>(from item in _items
                                                                      where item.Height_m < 8000
                                                                      select item);*/
        }

        return _items;
    }

    public ObservableCollection<SearchSourceDataItem> SearchData(string queryText)
    {
        return new ObservableCollection<SearchSourceDataItem>(from item in _items
                                                              where item.Provider.Contains(queryText, StringComparison.InvariantCultureIgnoreCase)
                                                              select item);
    }
}