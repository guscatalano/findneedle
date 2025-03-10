using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using findneedle.Implementations.Locations.EventLogQueryLocation;
using FindNeedlePluginLib.Interfaces;

namespace findneedle.Implementations.Locations;

public class FileEventLogQueryLocation : IEventLogQueryLocation
{


    public string filename
    {
        get; set;
    }
    readonly List<ISearchResult> searchResults = new();

    public FileEventLogQueryLocation(string filename)
    {
        this.filename = filename;
    }

    public override string GetDescription()
    {
        return "LocalEventLogQuery";
    }
    public override string GetName()
    {
        return filename;
    }



    public override void LoadInMemory()
    {

        //This can be useful to pre-filter
        //var query = "*"; //"*[System/Level=3 or Level=4]";
        EventLogQuery eventsQuery = new EventLogQuery(filename,
                                                      PathType.FilePath
                                                      );
        EventLogReader logReader = new EventLogReader(eventsQuery);

        for (EventRecord eventdetail = logReader.ReadEvent(); eventdetail != null; eventdetail = logReader.ReadEvent())
        {
            ISearchResult result = new EventLogResult(eventdetail, this);
            searchResults.Add(result);
            numRecordsInMemory++;
        }

    }

    public override List<ISearchResult> Search(ISearchQuery? searchQuery)
    {
        numRecordsInLastResult = 0;
        List<ISearchResult> filteredResults = new List<ISearchResult>();
        foreach (ISearchResult result in searchResults)
        {
            var passAll = true;
            if (searchQuery != null)
            {
                foreach (ISearchFilter filter in searchQuery.GetFilters())
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
