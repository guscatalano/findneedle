﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle.Implementations.Locations.EventLogQueryLocation;
using FindNeedlePluginLib.Interfaces;

namespace findneedle.Implementations;



public class LocalEventLogQueryLocation : IEventLogQueryLocation
{

    public override void SetNotificationCallback(SearchProgressSink sink)
    {

    }
    public override void SetSearchStatistics(SearchStatistics stats) { 
    }


    public string eventLogName
    { get; set;
    }
    readonly List<SearchResult> searchResults = new();
    public LocalEventLogQueryLocation()
    {
        eventLogName = "Application";
    }

    public LocalEventLogQueryLocation(string name)
    {
        eventLogName = name;
    }

    public override string GetDescription()
    {
        return "LocalEventLogQuery";
    }
    public override string GetName()
    {
        return eventLogName;
    }



    public override void LoadInMemory()
    {

        //This can be useful to pre-filter
        var query = "*"; //"*[System/Level=3 or Level=4]";
        EventLogQuery eventsQuery = new EventLogQuery(eventLogName, PathType.LogName, query);
        EventLogReader logReader = new EventLogReader(eventsQuery);

        for (EventRecord eventdetail = logReader.ReadEvent(); eventdetail != null; eventdetail = logReader.ReadEvent())
        {
            SearchResult result = new EventLogResult(eventdetail, this);
            searchResults.Add(result);
            numRecordsInMemory++;
        }
       
    }

    public override List<SearchResult> Search(ISearchQuery? searchQuery)
    {
        numRecordsInLastResult = 0;
        List<SearchResult> filteredResults = new List<SearchResult>();
        foreach (SearchResult result in searchResults)
        {
            var passAll = true;
            if (searchQuery != null)
            {
                foreach (SearchFilter filter in searchQuery.GetFilters())
                {
                    if (!filter.Filter(result))
                    {
                        passAll = false;
                        continue;
                    }
                }
            }
            if (passAll)
            {
                filteredResults.Add(result);
                numRecordsInLastResult++;
            }
        }
        return filteredResults;
    }
}