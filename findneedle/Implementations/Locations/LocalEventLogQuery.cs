using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace findneedle.Implementations
{

    public class LocalEventLogRecord : SearchResult
    {
        EventRecord entry;
        LocalEventLogQueryLocation location;
        string eventdata = "";
        string systemdata = "";
        public LocalEventLogRecord(EventRecord entry, LocalEventLogQueryLocation location)
        {
            this.entry = entry;
            this.location = location;

            if (location.GetSearchDepth() != SearchLocationDepth.Shallow)
            {
                var doc = entry.ToXml();

                //Parse eventdata
                int first = doc.IndexOf("<EventData>") + "<EventData>".Length;
                int last = doc.IndexOf("</EventData>");
                if (first > 0 && last > 0)
                {
                    eventdata = doc.Substring(first, last - first);
                }


                //Parse system data
                first = doc.IndexOf("<System>") + "<System>".Length;
                last = doc.IndexOf("</System>");
                if (first > 0 && last > 0)
                {
                    systemdata = doc.Substring(first, last - first);
                }
            }
        }


        public Level GetLevel()
        {
            switch (entry.LevelDisplayName.ToLower())
            {
                case "warning":
                    return Level.Warning;
                case "error":
                    return Level.Error;
                case "information":
                    return Level.Info;
                default:
                    return Level.Verbose;

            }
        }

        public DateTime GetLogTime()
        {
            if (entry.TimeCreated == null)
            {
                throw new NotSupportedException("Log time can be empty");
            }
            return (DateTime)entry.TimeCreated;
        }

        public string GetMachineName()
        {
            return entry.MachineName;
        }

        public string GetOpCode()
        {
            throw new NotImplementedException();
        }

        public string GetSource()
        {
            return entry.ProviderName;
        }

        public string GetTaskName()
        {
            return entry.TaskDisplayName;
        }

        public string GetUsername()
        {

            string sid = entry.UserId.ToString();
            try
            {
                string account = new System.Security.Principal.SecurityIdentifier(sid).Translate(typeof(System.Security.Principal.NTAccount)).ToString();
                return account.ToString();
            }
            catch (Exception)
            {
                //return sid if we couldn't translate it (from another machine?)
                return sid;
            }
        }

        public string GetMessage()
        {
            return eventdata;
        }

        //This is usually the message, but it can be more. This is likely not readable
        public string GetSearchableData()
        {

            return string.Join(' ', eventdata, systemdata);
        }

        public void WriteToConsole()
        {
            Console.WriteLine(eventdata);
        }
    }


    public class LocalEventLogQueryLocation : SearchLocation
    {
        bool deepsearch = false;
        string eventLogName = "Application";
        List<SearchResult> searchResults = new List<SearchResult>();
        public LocalEventLogQueryLocation()
        {

        }

        public override void LoadInMemory(bool prefilter = false)
        {

            //This can be useful to pre-filter
            string query = "*"; //"*[System/Level=3 or Level=4]";
            EventLogQuery eventsQuery = new EventLogQuery(eventLogName, PathType.LogName, query);
            EventLogReader logReader = new EventLogReader(eventsQuery);

            for (EventRecord eventdetail = logReader.ReadEvent(); eventdetail != null; eventdetail = logReader.ReadEvent())
            {
                SearchResult result = new LocalEventLogRecord(eventdetail, this);
                searchResults.Add(result);
                numRecordsInMemory++;
            }
           
        }

        public override List<SearchResult> Search(SearchQuery searchQuery)
        {
            numRecordsInLastResult = 0;
            List<SearchResult> filteredResults = new List<SearchResult>();
            foreach (SearchResult result in searchResults)
            {
                bool passAll = true;
                foreach (SearchFilter filter in searchQuery.GetFilters())
                {
                    if (!filter.Filter(result))
                    {
                        passAll = false;
                        continue;
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
}
