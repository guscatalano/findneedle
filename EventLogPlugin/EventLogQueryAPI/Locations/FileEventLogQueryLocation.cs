using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using findneedle.Implementations.Locations.EventLogQueryLocation;
using FindNeedlePluginLib;

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



    public override void LoadInMemory(System.Threading.CancellationToken cancellationToken = default)
    {

        EventLogQuery eventsQuery = new EventLogQuery(filename,
                                                      PathType.FilePath
                                                      );
        EventLogReader logReader = new EventLogReader(eventsQuery);

        for (EventRecord eventdetail = logReader.ReadEvent(); eventdetail != null; eventdetail = logReader.ReadEvent())
        {
            if (cancellationToken.IsCancellationRequested) return;
            ISearchResult result = new EventRecordResult(eventdetail, this);
            searchResults.Add(result);
            numRecordsInMemory++;
        }

    }

    public override List<ISearchResult> Search(System.Threading.CancellationToken cancellationToken = default)
    {
        numRecordsInLastResult = 0;
        List<ISearchResult> filteredResults = new List<ISearchResult>();
        foreach (ISearchResult result in searchResults)
        {
            if (cancellationToken.IsCancellationRequested) break;
            filteredResults.Add(result);
            numRecordsInLastResult++;
        }
        return filteredResults;
    }

    public override void ClearStatistics() => throw new NotImplementedException();
    public override List<ReportFromComponent> ReportStatistics() => throw new NotImplementedException();
}
